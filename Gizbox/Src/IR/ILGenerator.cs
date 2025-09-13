using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static Gizbox.SyntaxTree;

namespace Gizbox.IR
{
    /// <summary>
    /// 中间代码生成器  
    /// </summary>
    public class ILGenerator
    {
        //public  
        public SyntaxTree ast;

        public IRUnit ilUnit;


        //temp env  
        private SymbolTable mainEnv;

        private GTree<Scope> scopeTree = new();
        private GTree<Scope>.Node currScopeNode = null;

        //temp info  
        private GStack<SymbolTable> envStackTemp = new GStack<SymbolTable>();

        //status  
        private int tmpCounter = 0;//临时变量自增    
        private GStack<string> loopExitStack = new GStack<string>();


        public ILGenerator(SyntaxTree ast, IRUnit ir)
        {
            this.ilUnit = ir;
            this.ast = ast;
        }

        public IRUnit Generate()
        {
            tmpCounter = 0;

            envStackTemp.Push(ilUnit.globalScope.env);
            scopeTree.root.value = ilUnit.globalScope;
            currScopeNode = scopeTree.root;

            //从根节点生成   
            GenNode(ast.rootNode);

            //生成一个空指令  
            GenerateCode("");

            //指令重整
            ResortCodes();

            //完成指令构建
            ilUnit.Complete();


            if (Compiler.enableLogILGenerator) ilUnit.globalScope.env.Print();
            Compiler.Pause("符号表建立完毕");

            PrintCodes();
            Compiler.Pause("中间代码生成完毕");

            return ilUnit;
        }

        public void GenNode(SyntaxTree.Node node)
        {
            if (node.overrideNode != null)
            {
                GenNode(node.overrideNode);
                return;
            }


            //节点代码生成  
            switch (node)
            {
                case ProgramNode programNode:
                    {
                        GenNode(programNode.statementsNode);
                    }
                    break;
                case NamespaceNode namespaceNode:
                    {
                        GenNode(namespaceNode.stmtsNode);
                    }
                    break;
                case StatementsNode stmtsNode:
                    {
                        foreach (var stmt in stmtsNode.statements)
                        {
                            GenNode(stmt);
                        }
                    }
                    break;
                case StatementBlockNode blockNode:
                    {
                        Log("--------------------");
                        foreach (var key in blockNode.attributes.Keys)
                        {
                            Log("KEY:" + key);
                        }
                        envStackTemp.Push(blockNode.attributes["env"] as SymbolTable);
                        EnvBegin(blockNode.attributes["env"] as SymbolTable);


                        foreach (var stmt in blockNode.statements)
                        {
                            GenNode(stmt);
                        }

                        envStackTemp.Pop();
                        EnvEnd(blockNode.attributes["env"] as SymbolTable);
                    }
                    break;

                // *******************  语句节点 *********************************


                //类声明  
                case ClassDeclareNode classDeclNode:
                    {
                        string className = classDeclNode.classNameNode.FullName;
                        //GenerateCode("JUMP", "%LABEL:class_end:" + className);//顶级语句重排之后不再需要跳过

                        envStackTemp.Push(classDeclNode.attributes["env"] as SymbolTable);
                        EnvBegin(classDeclNode.attributes["env"] as SymbolTable);

                        GenerateCode("").label = "class_begin:" + className;

                        //构造函数（默认）  
                        {
                            //构造函数全名    
                            string funcFullName = classDeclNode.classNameNode.FullName + ".ctor";
                            //string funcFullName = "ctor";

                            ////跳过声明  
                            //GenerateCode("JUMP", "%LABEL:exit:" + funcFullName);

                            //函数开始    
                            EnvBegin(envStackTemp.Peek().GetTableInChildren(classDeclNode.classNameNode.FullName + ".ctor"));

                            GenerateCode("").label = "entry:" + funcFullName;
                            
                            GenerateCode("FUNC_BEGIN", funcFullName).label = "func_begin:" + funcFullName;
                            

                            //基类构造函数调用  
                            if (classDeclNode.baseClassNameNode != null)
                            {
                                var baseClassName = classDeclNode.baseClassNameNode.FullName;
                                var baseRec = Query(baseClassName);
                                var baseEnv = baseRec.envPtr;

                                GenerateCode("PARAM", "this");
                                GenerateCode("CALL", baseClassName + ".ctor", "%LITINT:1");
                            }

                            //成员变量初始化
                            foreach (var memberDecl in classDeclNode.memberDelareNodes)
                            {
                                if (memberDecl is VarDeclareNode)
                                {
                                    var fieldDecl = memberDecl as VarDeclareNode;

                                    GenNode(fieldDecl.initializerNode);
                                    GenerateCode("=", "this->" + fieldDecl.identifierNode.FullName, GetRet(fieldDecl.initializerNode));
                                }
                            }

                            //其他语句(Not Implement)  
                            //...  

                            GenerateCode("RETURN");

                            GenerateCode("FUNC_END").label = "func_end:" + funcFullName;
                            
                            GenerateCode("").label = "exit:" + funcFullName;

                            EnvEnd(envStackTemp.Peek().GetTableInChildren(classDeclNode.classNameNode.FullName + ".ctor"));
                        }

                        //成员函数
                        foreach (var memberDecl in classDeclNode.memberDelareNodes)
                        {
                            if (memberDecl is FuncDeclareNode)
                            {
                                GenNode(memberDecl as FuncDeclareNode);
                            }
                        }


                        GenerateCode("").label = "class_end:" + className;

                        EnvEnd(classDeclNode.attributes["env"] as SymbolTable);
                        envStackTemp.Pop();
                    }
                    break;
                //函数声明
                case FuncDeclareNode funcDeclNode:
                    {
                        //是否是实例成员函数  
                        bool isMethod = envStackTemp.Peek().tableCatagory == SymbolTable.TableCatagory.ClassScope;

                        //函数全名(修饰)    
                        string funcFinalName;
                        if (isMethod)
                            funcFinalName = envStackTemp.Peek().name + "." + (string)funcDeclNode.attributes["mangled_name"];
                        else
                            funcFinalName = (string)funcDeclNode.attributes["mangled_name"];


                        //函数开始    
                        
                        envStackTemp.Push(funcDeclNode.attributes["env"] as SymbolTable);
                        EnvBegin(funcDeclNode.attributes["env"] as SymbolTable);

                        GenerateCode("").label = "entry:" + funcFinalName;


                        //不再使用METHOD_BEGIN
                        //if (isMethod)
                        //    GenerateCode("METHOD_BEGIN", funcFinalName);
                        //else
                        //    GenerateCode("FUNC_BEGIN", funcFinalName);

                        GenerateCode("FUNC_BEGIN", funcFinalName).label = "func_begin:" + funcFinalName;

                        //语句  
                        foreach (var stmt in funcDeclNode.statementsNode.statements)
                        {
                            GenNode(stmt);
                        }


                        if (funcDeclNode.statementsNode.statements.Any(s => s is ReturnStmtNode) == false)
                            GenerateCode("RETURN");

                        //不再使用METHOD_END
                        //if (isMethod)
                        //    GenerateCode("METHOD_END", funcFinalName);
                        //else
                        //    GenerateCode("FUNC_END", funcFinalName);

                        GenerateCode("FUNC_END", funcFinalName).label = "func_end:" + funcFinalName;

                        GenerateCode("").label = "exit:" + funcFinalName;

                        EnvEnd(funcDeclNode.attributes["env"] as SymbolTable);
                        envStackTemp.Pop();
                    }
                    break;
                //extern函数声明  
                case ExternFuncDeclareNode externFuncDeclNode:
                    {
                        //函数全名    
                        string funcFullName = (string)externFuncDeclNode.attributes["mangled_name"];

                        //函数开始    

                        envStackTemp.Push(externFuncDeclNode.attributes["env"] as SymbolTable);
                        EnvBegin(externFuncDeclNode.attributes["env"] as SymbolTable);

                        GenerateCode("").label = "entry:" + funcFullName;

                        GenerateCode("FUNC_BEGIN", funcFullName).label = "func_begin:" + funcFullName;
                        ;


                        GenerateCode("EXTERN_IMPL", externFuncDeclNode.identifierNode.FullName);


                        GenerateCode("FUNC_END", funcFullName).label = "func_end:" + funcFullName;
                        ;


                        GenerateCode("").label = "exit:" + funcFullName;

                        EnvEnd(externFuncDeclNode.attributes["env"] as SymbolTable);
                        envStackTemp.Pop();
                    }
                    break;
                //常量声明
                case ConstantDeclareNode constDeclNode:
                    {
                        //不生成中间代码  
                    }
                    break;
                //变量声明
                case VarDeclareNode varDeclNode:
                    {
                        GenNode(varDeclNode.identifierNode);
                        GenNode(varDeclNode.initializerNode);

                        var tac = GenerateCode("=", GetRet(varDeclNode.identifierNode), GetRet(varDeclNode.initializerNode));
                    }
                    break;

                case ReturnStmtNode returnNode:
                    {
                        if (returnNode.returnExprNode != null)
                        {
                            GenNode(returnNode.returnExprNode);
                            GenerateCode("RETURN", GetRet(returnNode.returnExprNode));
                        }
                        else
                        {
                            GenerateCode("RETURN");
                        }
                    }
                    break;
                case DeleteStmtNode deleteNode:
                    {
                        GenNode(deleteNode.objToDelete);
                        GenerateCode("DEL", GetRet(deleteNode.objToDelete));
                    }
                    break;

                case SingleExprStmtNode singleExprNode:
                    {
                        GenNode(singleExprNode.exprNode);
                    }
                    break;

                case IfStmtNode ifNode:
                    {
                        int ifCounter = (int)ifNode.attributes["uid"];

                        GenerateCode("").label = "If_" + ifCounter;

                        var elseClause = ifNode.elseClause;

                        for (int i = 0; i < ifNode.conditionClauseList.Count; ++i)
                        {
                            var clause = ifNode.conditionClauseList[i];

                            string falseGotoLabel;
                            if (i != ifNode.conditionClauseList.Count - 1)
                            {
                                falseGotoLabel = "IfCondition_" + ifCounter + "_" + (i + 1);
                            }
                            else
                            {
                                if (elseClause != null)
                                {
                                    falseGotoLabel = "ElseStmt_" + ifCounter;
                                }
                                else
                                {
                                    falseGotoLabel = "EndIf_" + ifCounter;
                                }
                            }

                            GenerateCode("").label = "IfCondition_" + ifCounter + "_" + i;

                            GenNode(clause.conditionNode);
                            GenerateCode("IF_FALSE_JUMP", GetRet(clause.conditionNode), "%LABEL:" + falseGotoLabel);

                            GenerateCode("").label = "IfStmt_" + ifCounter + "_" + i;

                            GenNode(clause.thenNode);

                            GenerateCode("JUMP", "%LABEL:EndIf_" + ifCounter);
                        }

                        if (elseClause != null)
                        {
                            GenerateCode("").label = "ElseStmt_" + ifCounter;
                            GenNode(elseClause.stmt);
                        }


                        GenerateCode("").label = "EndIf_" + ifCounter;
                    }
                    break;
                case WhileStmtNode whileNode:
                    {
                        int whileCounter = (int)whileNode.attributes["uid"];

                        GenerateCode("").label = "While_" + whileCounter;

                        GenNode(whileNode.conditionNode);

                        GenerateCode("IF_FALSE_JUMP", GetRet(whileNode.conditionNode), "%LABEL:EndWhile_" + whileCounter);

                        loopExitStack.Push("EndWhile_" + whileCounter);

                        GenNode(whileNode.stmtNode);

                        loopExitStack.Pop();

                        GenerateCode("JUMP", "%LABEL:While_" + whileCounter);

                        GenerateCode("").label = "EndWhile_" + whileCounter;
                    }
                    break;
                case ForStmtNode forNode:
                    {
                        int forCounter = (int)forNode.attributes["uid"];

                        envStackTemp.Push(forNode.attributes["env"] as SymbolTable);
                        EnvBegin(forNode.attributes["env"] as SymbolTable);

                        //initializer  
                        GenNode(forNode.initializerNode);

                        //start loop  
                        GenerateCode("").label = "For_" + forCounter;

                        //condition  
                        GenNode(forNode.conditionNode);
                        GenerateCode("IF_FALSE_JUMP", GetRet(forNode.conditionNode), "%LABEL:EndFor_" + forCounter);

                        loopExitStack.Push("EndFor_" + forCounter);

                        GenNode(forNode.stmtNode);

                        //iterator  
                        GenNode(forNode.iteratorNode);


                        loopExitStack.Pop();

                        GenerateCode("JUMP", "%LABEL:For_" + forCounter);

                        GenerateCode("").label = "EndFor_" + forCounter;

                        EnvEnd(forNode.attributes["env"] as SymbolTable);
                        envStackTemp.Pop();
                    }
                    break;

                case BreakStmtNode breakNode:
                    {
                        GenerateCode("JUMP", "%LABEL:" + loopExitStack.Peek());
                    }
                    break;





                // *******************  表达式节点 *********************************

                case IdentityNode idNode:
                    {
                        //标识符表达式的返回变量（本身）    
                        SetRet(idNode, idNode.FullName);
                    }
                    break;
                case ThisNode thisnode:
                    {
                        SetRet(thisnode, "this");
                    }
                    break;
                case LiteralNode literalNode:
                    {
                        var litret = GenLitOperandStr(literalNode);

                        SetRet(literalNode, litret);
                    }
                    break;
                case ObjectMemberAccessNode objMemberAccess:
                    {
                        GenNode(objMemberAccess.objectNode);

                        // 取出objec的返回表达式
                        string objExpr = TrimName(GetRet(objMemberAccess.objectNode));

                        bool IsAccess(ExprNode node)
                        {
                            if(node is ElementAccessNode)
                                return true;
                            if(node is ObjectMemberAccessNode)
                                return true;
                            return false;
                        }

                        // 作为左值使用
                        bool isLeftValue = (objMemberAccess.Parent is AssignNode assign) && (assign.lvalueNode == objMemberAccess);
                        if(isLeftValue)
                        {
                            SetRet(objMemberAccess, objExpr + "->" + objMemberAccess.memberNode.FullName);
                        }
                        else
                        {
                            // 右值：读一次到临时变量，返回该临时
                            string valueType = (string)objMemberAccess.attributes["type"];
                            string tmp = NewTemp(valueType);
                            GenerateCode("=", tmp, objExpr + "->" + objMemberAccess.memberNode.FullName);
                            SetRet(objMemberAccess, tmp);
                        }


                    }
                    break;
                case CastNode castNode:
                    {
                        GenNode(castNode.factorNode);

                        //表达式的返回变量  
                        SetRet(castNode, NewTemp(castNode.typeNode.TypeExpression()));

                        GenerateCode("CAST", GetRet(castNode), castNode.typeNode.TypeExpression(), GetRet(castNode.factorNode));
                    }
                    break;
                case BinaryOpNode binaryOp:
                    {
                        GenNode(binaryOp.leftNode);
                        GenNode(binaryOp.rightNode);

                        if (binaryOp.leftNode.attributes.ContainsKey("type") == false)
                            throw new SemanticException(ExceptioName.TypeNotSet, binaryOp.leftNode, "");

                        //if (binaryOp.rightNode.attributes.ContainsKey("type") == false)
                        //    throw new SemanticException(binaryOp.rightNode, "type未设置!");

                        //表达式的返回变量  
                        SetRet(binaryOp, NewTemp((string)binaryOp.leftNode.attributes["type"]));

                        GenerateCode(binaryOp.op, GetRet(binaryOp), GetRet(binaryOp.leftNode), GetRet(binaryOp.rightNode));
                    }
                    break;
                case UnaryOpNode unaryOp:
                    {
                        GenNode(unaryOp.exprNode);

                        //表达式的返回变量  
                        SetRet(unaryOp, NewTemp((string)unaryOp.exprNode.attributes["type"]));

                        GenerateCode(unaryOp.op, GetRet(unaryOp), GetRet(unaryOp));
                    }
                    break;
                case AssignNode assignNode:
                    {
                        GenNode(assignNode.lvalueNode);
                        GenNode(assignNode.rvalueNode);


                        //复合赋值表达式的返回变量为左值    
                        GenerateCode(assignNode.op, GetRet(assignNode.lvalueNode), GetRet(assignNode.rvalueNode));
                    }
                    break;
                case CallNode callNode:
                    {
                        if (callNode.attributes.ContainsKey("mangled_name") == false)
                            throw new SemanticException(ExceptioName.FunctionObfuscationNameNotSet, callNode, "");
                        //函数全名  
                        string mangledName = (string)callNode.attributes["mangled_name"];
                        //函数返回类型    
                        string returnType = (string)callNode.attributes["type"];

                        //表达式的返回变量  
                        SetRet(callNode, NewTemp(returnType));

                        //参数数量  
                        int argCount;
                        if (callNode.isMemberAccessFunction == true)
                        {
                            argCount = callNode.argumantsNode.arguments.Count + 1;
                        }
                        else
                        {
                            argCount = callNode.argumantsNode.arguments.Count;
                        }


                        //一定要先计算参数再Param，否则连续调用aaa().bbb()会出错！

                        //实参计算
                        for (int i = callNode.argumantsNode.arguments.Count - 1; i >= 0; --i)
                        {
                            //计算参数表达式的值  
                            GenNode(callNode.argumantsNode.arguments[i]);
                        }

                        //this实参计算（成员方法）    
                        if (callNode.isMemberAccessFunction == true)
                        {
                            if (callNode.funcNode is ObjectMemberAccessNode)
                            {
                                var objNode = (callNode.funcNode as ObjectMemberAccessNode).objectNode;
                                GenNode(objNode);
                            }
                        }

                        //实参倒序压栈  
                        for (int i = callNode.argumantsNode.arguments.Count - 1; i >= 0; --i)
                        {
                            GenerateCode("PARAM", GetRet(callNode.argumantsNode.arguments[i]));
                        }
                        //this实参压栈（成员方法）    
                        if (callNode.isMemberAccessFunction == true)
                        {
                            if (callNode.funcNode is ObjectMemberAccessNode)
                            {
                                var objNode = (callNode.funcNode as ObjectMemberAccessNode).objectNode;
                                GenerateCode("PARAM", GetRet(objNode));
                            }
                            else
                            {
                                GenerateCode("PARAM", "this");
                            }
                        }



                        if (callNode.isMemberAccessFunction == true)
                        {
                            GenerateCode("MCALL", mangledName, "%LITINT:" + argCount);
                            GenerateCode("=", GetRet(callNode), "%RET");
                        }
                        else
                        {
                            GenerateCode("CALL", mangledName, "%LITINT:" + argCount);
                            GenerateCode("=", GetRet(callNode), "%RET");
                        }
                    }
                    break;
                case ElementAccessNode eleAccessNode:
                    {
                        GenNode(eleAccessNode.indexNode);
                        GenNode(eleAccessNode.containerNode);

                        string container = TrimName(GetRet(eleAccessNode.containerNode));
                        string index = TrimName(GetRet(eleAccessNode.indexNode));

                        // 是左值  
                        bool isLeftValue = (eleAccessNode.Parent is AssignNode assign) && (assign.lvalueNode == eleAccessNode);

                        if(isLeftValue)
                        {
                            string accessExpr = container + "[" + index + "]";
                            // 直接作为左值返回
                            SetRet(eleAccessNode, accessExpr);
                        }
                        else
                        {

                            string accessExpr = container + "[" + index + "]";
                            string elemType = (string)eleAccessNode.attributes["type"];
                            string tmp = NewTemp(elemType);
                            GenerateCode("=", tmp, accessExpr); // tmp = [container[index]]
                            SetRet(eleAccessNode, tmp);
                        }
                    }
                    break;
                case NewObjectNode newObjNode:
                    {
                        string className = newObjNode.className.FullName;

                        //表达式的返回变量  
                        SetRet(newObjNode, NewTemp(className));

                        GenerateCode("ALLOC", GetRet(newObjNode), className);
                        GenerateCode("PARAM", GetRet(newObjNode));
                        GenerateCode("CALL", className + ".ctor", "%LITINT:" + 1);
                    }
                    break;
                case NewArrayNode newArrNode:
                    {
                        //长度计算  
                        GenNode(newArrNode.lengthNode);

                        //表达式的返回变量  
                        SetRet(newArrNode, NewTemp(newArrNode.typeNode.TypeExpression() + "[]"));

                        GenerateCode("ALLOC_ARRAY", GetRet(newArrNode), GetRet(newArrNode.lengthNode));
                    }
                    break;
                case IncDecNode incDecNode:
                    {
                        string identifierName = incDecNode.identifierNode.FullName;
                        if (incDecNode.isOperatorFront)//++i
                        {
                            GenerateCode(incDecNode.op, identifierName);
                            SetRet(incDecNode, identifierName);
                        }
                        else//i++
                        {
                            SetRet(incDecNode, NewTemp(Query(identifierName).typeExpression));
                            GenerateCode("=", GetRet(incDecNode), identifierName);
                            GenerateCode(incDecNode.op, identifierName);
                        }
                    }
                    break;
                default:
                    throw new SemanticException(ExceptioName.Undefine, node, "IR generation not implemtented:" + node.GetType().Name);
            }
        }

        public TAC GenerateCode(string op, object arg1 = null, object arg2 = null, object arg3 = null)
        {
            var newCode = new TAC() { op = op, arg0 = arg1?.ToString(), arg1 = arg2?.ToString(), arg2 = arg3?.ToString() };

            ilUnit.codes.Add(newCode);

            return newCode;
        }


        private void ResortCodes()
        {
            //ScopeDesc补全  
            foreach(var desc in scopeDescs)
            {
                desc.tacFrom = ilUnit.codes[desc.tmpLineFrom];
                desc.tacTo = ilUnit.codes[desc.tmpLineTo];
            }

            //重排指令、并修改Scopes的起止行  
            Stack<TAC> temp1 = new();
            Stack<TAC> temp2 = new();
            void TakeLast(int fromIndex, Stack<TAC> to)
            {
                if(fromIndex > ilUnit.codes.Count - 1)
                    return;
                for(int i = ilUnit.codes.Count - 1; i >= fromIndex; i--)
                {
                    to.Push(ilUnit.codes[i]);
                    ilUnit.codes.RemoveAt(i);
                }
            }
            void AppendLast(int count, Stack<TAC> from)
            {
                for(int i = 0; i < count; ++i)
                {
                    var tac = from.Pop();
                    ilUnit.codes.Add(tac);
                }
            }


            var globalEnv = ilUnit.globalScope.env;
            List<ScopeDesc> scopesToMove = new ();
            List<ScopeDesc> scopesMain = new ();
            //仅移动第二层scope  
            foreach(var scopeDesc in scopeDescs)
            {
                if(scopeDesc.env.parent != globalEnv)
                    continue;

                //类作用域和函数作用域
                if(scopeDesc.env.tableCatagory == SymbolTable.TableCatagory.FuncScope || scopeDesc.env.tableCatagory == SymbolTable.TableCatagory.ClassScope)
                {
                    scopesToMove.Add(scopeDesc);
                }
                //其他块作用域  
                else
                {
                    scopesMain.Add(scopeDesc);
                }
            }
            //scope排序  
            scopesToMove.Sort((s1, s2) => -(s1.tmpLineTo.CompareTo(s2.tmpLineTo)));
            //开始移动  
            foreach(var scope in scopesToMove)
            {
                TakeLast(scope.tmpLineTo + 1, temp1);
                TakeLast(scope.tmpLineFrom, temp2);
            }
            TakeLast(0, temp1);

            //函数和类定义部分  
            AppendLast(temp2.Count, temp2);

            //顶级语句部分-组装Main函数    
            var mainEntry = new TAC() { op = string.Empty, label = "entry:Main" };
            ilUnit.codes.Add(mainEntry);
            ilUnit.codes.Add(new TAC() { op = "FUNC_BEGIN", arg0 = "Main", label = "func_begin:Main" });
            AppendLast(temp1.Count, temp1);
            ilUnit.codes.Add(new TAC() { op = "FUNC_END", arg0 = "Main", label = "func_end:Main" });
            var mainExit = new TAC() { op = string.Empty, label = "exit:Main" };
            ilUnit.codes.Add(mainExit);

            //Main作用域构建
            var mainEnv = new SymbolTable("Main", SymbolTable.TableCatagory.FuncScope, globalEnv);
            var mainScope = new Scope() { env = mainEnv };
            var mainScopeDesc = new ScopeDesc() { env = mainEnv, finalScope = mainScope, tacFrom = mainEntry, tacTo = mainExit };
            var mainNode = scopeTree.root.Add(mainScope);
            scopeDescs.Add(mainScopeDesc);

            //global作用域中的普通块作用域移动到Main作用域
            List<GTree<Scope>.Node> moveList = new();
            foreach(var node in scopeTree.root.children)
            {
                if(node == mainNode)
                    continue;
                if(node.value.env.tableCatagory == SymbolTable.TableCatagory.FuncScope)
                    continue;
                if(node.value.env.tableCatagory == SymbolTable.TableCatagory.ClassScope)
                    continue;
                moveList.Add(node);
            }
            foreach(var n in moveList)
            {
                scopeTree.root.Remove(n);
                mainNode.Add(n);

                n.value.env.RemoveFromParent();
                mainNode.value.env.AddChildren(n.value.env);
            }
            globalEnv.RefreshDepth();

            //global作用域中的临时变量复制到Main作用域  
            List<SymbolTable.Record> tempVars = new();
            var globalVarRecs = globalEnv.GetByCategory(SymbolTable.RecordCatagory.Variable);
            if(globalVarRecs != null)
            {
                foreach(var rec in globalVarRecs)
                {
                    if(rec.name.StartsWith("tmp@"))
                    {
                        tempVars.Add(rec);
                    }
                }
            }
            foreach(var rec in tempVars)
            {
                var moveRec = globalEnv.RemoveRecord(rec.name);
                mainEnv.AddRecord(moveRec.name, moveRec);
            }

            //global符号表中添加Main函数记录  
            var mainRec = globalEnv.NewRecord("Main", SymbolTable.RecordCatagory.Function, "-> void", mainEnv);


            //tac下标  
            for(int i = 0; i < ilUnit.codes.Count; ++i)
            {
                ilUnit.codes[i].line = i;
            }

            //Scope范围修正  
            foreach(var scopeDesc in scopeDescs)
            {
                scopeDesc.finalScope.lineFrom = scopeDesc.tacFrom.line;
                scopeDesc.finalScope.lineTo = scopeDesc.tacTo.line;
            }
            ilUnit.globalScope.lineFrom = 0;
            ilUnit.globalScope.lineTo = (ilUnit.codes.Count - 1);

            //scopeTree加入ir.scopes
            foreach(var s in scopeTree.TraverseDepthFirst())
            {
                ilUnit.scopes.Add(s);
            }
        }

        private string GetRet(SyntaxTree.Node exprNode)
        {
            if (exprNode.overrideNode != null)
            {
                return GetRet(exprNode.overrideNode);
            }

            if (exprNode.attributes.ContainsKey("ret"))
            {
                return (string)exprNode.attributes["ret"];
            }
            else
            {
                throw new SemanticException(ExceptioName.SubExpressionNoReturnVariable, exprNode, "");
            }
        }
        private void SetRet(SyntaxTree.Node node, string val)
        {
            node.attributes["ret"] = val;
        }

        public string NewTemp(string type)
        {
            string tempVarName = "tmp@" + tmpCounter++;

            envStackTemp.Peek().NewRecord(tempVarName, SymbolTable.RecordCatagory.Variable, type);

            return tempVarName;
        }

        private string GenLitOperandStr(LiteralNode literalNode)
        {
            //表达式的返回变量  
            string operandStr;

            if (literalNode.attributes.ContainsKey("type"))
            {
                string typeName = (string)literalNode.attributes["type"];

                if (typeName != "null")
                {
                    if (typeName == "string")
                    {
                        string lex = literalNode.token.attribute;
                        string conststr = lex.Substring(1, lex.Length - 2);
                        ilUnit.constData.Add(("string", conststr));
                        int ptr = ilUnit.constData.Count - 1;

                        if (Compiler.enableLogILGenerator)
                            Log("新的字符串常量：" + lex + " 指针：" + ptr);

                        operandStr = "%CONSTSTRING:" + ptr;
                    }
                    else
                    {
                        operandStr = "%LIT" + typeName.ToUpper() + ":" + literalNode.token.attribute;
                    }
                }
                else
                {
                    operandStr = "%LITNULL:";
                }
            }
            else
            {
                throw new SemanticException(ExceptioName.LiteralTypeUnknown, literalNode, literalNode.token.ToString());
            }


            return operandStr;
        }


        public List<ScopeDesc> currScopeDescs = new ();
        public List<ScopeDesc> scopeDescs = new();

        public void EnvBegin(SymbolTable env)
        {
            ScopeDesc newScope = new ScopeDesc() { env = env, tmpLineFrom = (ilUnit.codes.Count - 1 + 1) };
            Scope scope = new Scope() { env = env };
            currScopeDescs.Add(newScope);
            var newNode = currScopeNode.Add(scope);
            currScopeNode = newNode;

        }
        public void EnvEnd(SymbolTable env)
        {
            var scopeTmp = currScopeDescs.FirstOrDefault(s => s.env == env);
            scopeTmp.tmpLineTo = ilUnit.codes.Count - 1;

            scopeTmp.finalScope = currScopeNode.value;
            scopeDescs.Add(scopeTmp);

            currScopeDescs.Remove(scopeTmp);
            currScopeNode = currScopeNode.parent;
            if(currScopeNode == null)
                throw new Exception("");
        }


        public SymbolTable.Record Query(string id)
        {
            //本编译单元查找  
            for (int i = envStackTemp.Count - 1; i >= 0; --i)
            {
                if (envStackTemp[i].ContainRecordName(id))
                {
                    //Log("在" + envStack[i].name + "中查询：" + id + "成功");
                    var rec = envStackTemp[i].GetRecord(id);
                    return rec;
                }
                //Log("在" + envStack[i].name + "中查询：" + id + "失败");
            }

            //导入库查找  
            foreach (var lib in ilUnit.dependencyLibs)
            {
                var rec = lib.globalScope.env.GetRecord(id);
                if (rec != null)
                {
                    return rec;
                }
            }

            throw new GizboxException(ExceptioName.TypeNotFound, id);
        }


        //public SymbolTable.Record Query(string className, string id)
        //{
        //    //本编译单元查找  
        //    if (ilUnit.globalScope.env.ContainRecordName(className))
        //    {
        //        var classEnv = ilUnit.globalScope.env.GetTableInChildren(className);
        //        if (classEnv != null)
        //        { 
        //            if(classEnv.ContainRecordName(id))
        //            {
        //                return classEnv.GetRecord(id);
        //            }
        //        }
        //    }

        //    //导入库查找  
        //    foreach (var lib in ilUnit.dependencies)
        //    {
        //        if(lib.globalScope.env.ContainRecordName(className))
        //        {
        //            var classEnv = lib.globalScope.env.GetTableInChildren(className);
        //            if (classEnv != null)
        //            {
        //                if(classEnv .ContainRecordName(id))
        //                {
        //                    Debug.Log("库中找到：" + className + "." + id);
        //                    return classEnv.GetRecord(id);
        //                }
        //            }
        //        }
        //    }

        //    throw new Exception("找不到" + className + "." + id);
        //}



        public bool IsAccess(string retExpr)//是数组元素访问或者对象访问  
        {
            if (retExpr.Contains('.')) return true;
            if (retExpr[retExpr.Length - 1] == ']' && retExpr[retExpr.Length - 2] == ']') return true;
            return false;
        }

        public string TrimName(string input)
        {
            if (input[0] != '[') return input;
            return input.Substring(1, input.Length - 2);
        }

        public void PrintCodes()
        {
            if (Compiler.enableLogILGenerator) ilUnit.Print();
        }

        public static void Log(object content)
        {
            if (!Compiler.enableLogILGenerator) return;
            GixConsole.WriteLine("ILGen >>>" + content);
        }

    }

    public class ScopeDesc
    {
        public int tmpLineFrom;
        public int tmpLineTo;
        public TAC tacFrom;
        public TAC tacTo;
        public SymbolTable env;

        public Scope finalScope;
    }
}
