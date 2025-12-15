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
    public class IRGenerator
    {
        //public  
        public SyntaxTree ast;

        public IRUnit ilUnit;
        private string topFunc;


        //temp env  
        private SymbolTable mainEnv;

        private GTree<Scope> scopeTree = new();
        private GTree<Scope>.Node currScopeNode = null;

        //temp info  
        private GStack<SymbolTable> envStackTemp = new GStack<SymbolTable>();

        //status  
        private int tmpCounter = 0;//临时变量自增    
        private GStack<string> loopExitStack = new GStack<string>();


        public IRGenerator(SyntaxTree ast, IRUnit ir, bool isMainUnit)
        {
            this.ilUnit = ir;
            this.ast = ast;
            this.topFunc = $"__top__";
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
            EmitCode("");

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
                throw new GizboxException(ExceptioName.Undefine, "override node not applied!");
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
                        envStackTemp.Push(blockNode.attributes[eAttr.env] as SymbolTable);
                        EnvBegin(blockNode.attributes[eAttr.env] as SymbolTable);


                        foreach (var stmt in blockNode.statements)
                        {
                            GenNode(stmt);
                        }


                        // 作用域退出 -> 删除存活的Owner类型  
                        if(blockNode.attributes.ContainsKey(eAttr.drop_var_exit_env))
                        {
                            var toDelete = blockNode.attributes[eAttr.drop_var_exit_env] as List<(LifetimeInfo.VarStatus status, string varname)>;
                            if(toDelete != null)
                            {
                                foreach(var (s, v) in toDelete)
                                {
                                    EmitOwnConditionalDropCode(s, v);
                                }
                            }
                            blockNode.attributes.Remove(eAttr.drop_var_exit_env);
                        }

                        envStackTemp.Pop();
                        EnvEnd(blockNode.attributes[eAttr.env] as SymbolTable);
                    }
                    break;

                // *******************  语句节点 *********************************


                //类声明  
                case ClassDeclareNode classDeclNode:
                    {
                        string className = classDeclNode.classNameNode.FullName;
                        //GenerateCode("JUMP", "%LABEL:class_end:" + className);//顶级语句重排之后不再需要跳过

                        envStackTemp.Push(classDeclNode.attributes[eAttr.env] as SymbolTable);
                        EnvBegin(classDeclNode.attributes[eAttr.env] as SymbolTable);

                        EmitCode("").label = "class_begin:" + className;

                        //生成隐式构造函数  
                        EmitCtor(classDeclNode);

                        //生成隐式析构函数  
                        EmitDtor(classDeclNode);

                        //成员函数
                        foreach(var memberDecl in classDeclNode.memberDelareNodes)
                        {
                            if(memberDecl is FuncDeclareNode)
                            {
                                GenNode(memberDecl as FuncDeclareNode);
                            }
                        }


                        EmitCode("").label = "class_end:" + className;

                        EnvEnd(classDeclNode.attributes[eAttr.env] as SymbolTable);
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
                            funcFinalName = envStackTemp.Peek().name + "." + (string)funcDeclNode.attributes[eAttr.mangled_name];
                        else
                            funcFinalName = (string)funcDeclNode.attributes[eAttr.mangled_name];


                        //函数开始    
                        
                        envStackTemp.Push(funcDeclNode.attributes[eAttr.env] as SymbolTable);
                        EnvBegin(funcDeclNode.attributes[eAttr.env] as SymbolTable);

                        EmitCode("").label = "entry:" + funcFinalName;


                        //不再使用METHOD_BEGIN
                        //if (isMethod)
                        //    GenerateCode("METHOD_BEGIN", funcFinalName);
                        //else
                        //    GenerateCode("FUNC_BEGIN", funcFinalName);

                        EmitCode("FUNC_BEGIN", funcFinalName).label = "func_begin:" + funcFinalName;

                        //语句  
                        foreach (var stmt in funcDeclNode.statementsNode.statements)
                        {
                            GenNode(stmt);
                        }

                        // 函数正常退出需要回收的 Owner
                        if(funcDeclNode.attributes.ContainsKey(eAttr.drop_var_exit_env))
                        {
                            var toDelete = funcDeclNode.attributes[eAttr.drop_var_exit_env] as List<(LifetimeInfo.VarStatus, string)>;
                            if(toDelete != null)
                            {
                                foreach(var (s, v) in toDelete)
                                {
                                    EmitOwnConditionalDropCode(s, v);
                                }
                            }
                            funcDeclNode.attributes.Remove(eAttr.drop_var_exit_env);
                        }


                        if (funcDeclNode.statementsNode.statements.Any(s => s is ReturnStmtNode) == false)
                            EmitCode("RETURN");

                        //不再使用METHOD_END
                        //if (isMethod)
                        //    GenerateCode("METHOD_END", funcFinalName);
                        //else
                        //    GenerateCode("FUNC_END", funcFinalName);

                        EmitCode("FUNC_END", funcFinalName).label = "func_end:" + funcFinalName;

                        EmitCode("").label = "exit:" + funcFinalName;

                        EnvEnd(funcDeclNode.attributes[eAttr.env] as SymbolTable);
                        envStackTemp.Pop();
                    }
                    break;
                //extern函数声明  
                case ExternFuncDeclareNode externFuncDeclNode:
                    {
                        //外部函数声明不再生成中间代码  
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

                        var tac = EmitCode("=", GetRet(varDeclNode.identifierNode), GetRet(varDeclNode.initializerNode));


                        // 被移动变量置NULL  
                        if(varDeclNode.attributes.ContainsKey(eAttr.set_null_after_stmt))
                        {
                            var toNull = varDeclNode.attributes[eAttr.set_null_after_stmt] as List<string>;
                            if(toNull != null)
                            {
                                foreach(var v in toNull)
                                {
                                    EmitCode("=", v, "%LITNULL:");
                                }
                            }
                            varDeclNode.attributes.Remove(eAttr.set_null_after_stmt);
                        }
                    }
                    break;

                case ReturnStmtNode returnNode:
                    {
                        if(returnNode.returnExprNode != null)
                        {
                            GenNode(returnNode.returnExprNode);
                        }

                        // return之前先回收需要回收的Owner类型  
                        if(returnNode.attributes.ContainsKey(eAttr.drop_var_before_return))
                        {
                            var toDelete = returnNode.attributes[eAttr.drop_var_before_return] as List<(LifetimeInfo.VarStatus, string)>;
                            if(toDelete != null)
                            {
                                foreach(var (s, v) in toDelete)
                                {
                                    EmitOwnConditionalDropCode(s, v);
                                }
                            }
                            returnNode.attributes.Remove(eAttr.drop_var_before_return);
                        }

                        if(returnNode.returnExprNode != null)
                        {
                            EmitCode("RETURN", GetRet(returnNode.returnExprNode));
                        }
                        else
                        {
                            EmitCode("RETURN");
                        }
                    }
                    break;
                case DeleteStmtNode deleteNode:
                    {
                        GenNode(deleteNode.objToDelete);

                        if(deleteNode.isArrayDelete == false)
                        {
                            EmitDeleteCode(GetRet(deleteNode.objToDelete));
                        }
                        else
                        {
                            EmitDeleteArrayCode(GetRet(deleteNode.objToDelete));
                        }
                    }
                    break;

                case SingleExprStmtNode singleExprNode:
                    {
                        GenNode(singleExprNode.exprNode);

                        // 语句末尾删除临时的所有权结果
                        if(singleExprNode.attributes.ContainsKey(eAttr.drop_expr_result_after_stmt))
                        {
                            var exprs = singleExprNode.attributes[eAttr.drop_expr_result_after_stmt] as List<SyntaxTree.ExprNode>;
                            if(exprs != null)
                            {
                                foreach(var e in exprs)
                                {
                                    EmitOwnDropCode(GetRet(e));
                                }
                            }
                            singleExprNode.attributes.Remove(eAttr.drop_expr_result_after_stmt);
                        }
                    }
                    break;

                case IfStmtNode ifNode:
                    {
                        int ifCounter = (int)ifNode.attributes[eAttr.uid];

                        EmitCode("").label = "If_" + ifCounter;

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

                            EmitCode("").label = "IfCondition_" + ifCounter + "_" + i;

                            GenNode(clause.conditionNode);
                            EmitCode("IF_FALSE_JUMP", GetRet(clause.conditionNode), "%LABEL:" + falseGotoLabel);

                            EmitCode("").label = "IfStmt_" + ifCounter + "_" + i;

                            GenNode(clause.thenNode);

                            EmitCode("JUMP", "%LABEL:EndIf_" + ifCounter);
                        }

                        if (elseClause != null)
                        {
                            EmitCode("").label = "ElseStmt_" + ifCounter;
                            GenNode(elseClause.stmt);
                        }


                        EmitCode("").label = "EndIf_" + ifCounter;
                    }
                    break;
                case WhileStmtNode whileNode:
                    {
                        int whileCounter = (int)whileNode.attributes[eAttr.uid];

                        EmitCode("").label = "While_" + whileCounter;

                        GenNode(whileNode.conditionNode);

                        EmitCode("IF_FALSE_JUMP", GetRet(whileNode.conditionNode), "%LABEL:EndWhile_" + whileCounter);

                        loopExitStack.Push("EndWhile_" + whileCounter);

                        GenNode(whileNode.stmtNode);

                        loopExitStack.Pop();

                        EmitCode("JUMP", "%LABEL:While_" + whileCounter);

                        EmitCode("").label = "EndWhile_" + whileCounter;
                    }
                    break;
                case ForStmtNode forNode:
                    {
                        int forCounter = (int)forNode.attributes[eAttr.uid];

                        envStackTemp.Push(forNode.attributes[eAttr.env] as SymbolTable);
                        EnvBegin(forNode.attributes[eAttr.env] as SymbolTable);

                        //initializer  
                        GenNode(forNode.initializerNode);

                        //start loop  
                        EmitCode("").label = "For_" + forCounter;

                        //condition  
                        GenNode(forNode.conditionNode);
                        EmitCode("IF_FALSE_JUMP", GetRet(forNode.conditionNode), "%LABEL:EndFor_" + forCounter);

                        loopExitStack.Push("EndFor_" + forCounter);

                        GenNode(forNode.stmtNode);

                        //iterator  
                        GenNode(forNode.iteratorNode);


                        loopExitStack.Pop();

                        EmitCode("JUMP", "%LABEL:For_" + forCounter);

                        EmitCode("").label = "EndFor_" + forCounter;

                        EnvEnd(forNode.attributes[eAttr.env] as SymbolTable);
                        envStackTemp.Pop();
                    }
                    break;

                case BreakStmtNode breakNode:
                    {
                        EmitCode("JUMP", "%LABEL:" + loopExitStack.Peek());
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
                            string valueType = (string)objMemberAccess.attributes[eAttr.type];
                            string tmp = NewTemp(valueType);
                            EmitCode("=", tmp, objExpr + "->" + objMemberAccess.memberNode.FullName);
                            SetRet(objMemberAccess, tmp);
                        }


                    }
                    break;
                case CastNode castNode:
                    {
                        GenNode(castNode.factorNode);

                        //表达式的返回变量  
                        SetRet(castNode, NewTemp(castNode.typeNode.TypeExpression()));

                        EmitCode("CAST", GetRet(castNode), castNode.typeNode.TypeExpression(), GetRet(castNode.factorNode));
                    }
                    break;
                case BinaryOpNode binaryOp:
                    {
                        GenNode(binaryOp.leftNode);
                        GenNode(binaryOp.rightNode);

                        if (binaryOp.leftNode.attributes.ContainsKey(eAttr.type) == false)
                            throw new SemanticException(ExceptioName.TypeNotSet, binaryOp.leftNode, "");

                        //if (binaryOp.rightNode.attributes.ContainsKey("type") == false)
                        //    throw new SemanticException(binaryOp.rightNode, "type未设置!");

                        //表达式的返回变量  
                        if(binaryOp.IsCompare)
                        {
                            SetRet(binaryOp, NewTemp("bool"));
                        }
                        else
                        {
                            SetRet(binaryOp, NewTemp((string)binaryOp.leftNode.attributes[eAttr.type]));
                        }
                        

                        EmitCode(binaryOp.op, GetRet(binaryOp), GetRet(binaryOp.leftNode), GetRet(binaryOp.rightNode));
                    }
                    break;
                case UnaryOpNode unaryOp:
                    {
                        GenNode(unaryOp.exprNode);

                        //表达式的返回变量  
                        SetRet(unaryOp, NewTemp((string)unaryOp.exprNode.attributes[eAttr.type]));

                        EmitCode(unaryOp.op, GetRet(unaryOp), GetRet(unaryOp));
                    }
                    break;
                case AssignNode assignNode:
                    {
                        GenNode(assignNode.lvalueNode);
                        GenNode(assignNode.rvalueNode);


                        // 赋值前先删除之前的Owner
                        if(assignNode.attributes.ContainsKey(eAttr.drop_var_before_assign_stmt))
                        {
                            var toDelete = assignNode.attributes[eAttr.drop_var_before_assign_stmt] as List<(LifetimeInfo.VarStatus, string)>;
                            if(toDelete != null)
                            {
                                foreach(var (s, v) in toDelete)
                                {
                                    EmitOwnConditionalDropCode(s, v);
                                }
                            }
                            assignNode.attributes.Remove(eAttr.drop_var_before_assign_stmt);
                        }
                        // 成员字段赋值前先条件删除  
                        if(assignNode.attributes.ContainsKey(eAttr.drop_field_before_assign_stmt))
                        {
                            var accessNode = assignNode.lvalueNode as SyntaxTree.ObjectMemberAccessNode;
                            var obj = GetRet(accessNode.objectNode);
                            var field = accessNode.memberNode.FullName;
                            EmitOwnDropField(obj, field);

                            assignNode.attributes.Remove(eAttr.drop_field_before_assign_stmt);
                        }

                        // 复合赋值表达式的返回变量为左值    
                        EmitCode(assignNode.op, GetRet(assignNode.lvalueNode), GetRet(assignNode.rvalueNode));


                        // 移动源置NULL  
                        if(assignNode.attributes.ContainsKey(eAttr.set_null_after_stmt))
                        {
                            var toNull = assignNode.attributes[eAttr.set_null_after_stmt] as List<string>;
                            if(toNull != null)
                            {
                                foreach(var v in toNull)
                                {
                                    EmitCode("=", v, "%LITNULL:");
                                }
                            }
                            assignNode.attributes.Remove(eAttr.set_null_after_stmt);
                        }
                    }
                    break;
                case CallNode callNode:
                    {
                        //函数全名  
                        string fullName;
                        if(callNode.attributes.TryGetValue(eAttr.mangled_name, out object oMangleName))
                        {
                            fullName = (string)oMangleName;
                        }
                        else if(callNode.attributes.TryGetValue(eAttr.extern_name, out object oExternName))
                        {
                            fullName= (string)oExternName;
                        }
                        else
                        {
                            throw new SemanticException(ExceptioName.FunctionObfuscationNameNotSet, callNode, "Mangled function name and extern function name not found.");
                        }


                        //函数返回类型    
                        string returnType = (string)callNode.attributes[eAttr.type];

                        //是否有返回值且作为右值  
                        bool returnTypeNotVoid = GType.Parse(returnType).Category != GType.Kind.Void;
                        bool isSingleExprStmt = (callNode.Parent is SingleExprStmtNode stmt && stmt.exprNode == callNode);


                        //表达式的返回变量  
                        if(callNode.attributes.ContainsKey(eAttr.store_expr_result))
                        {
                            SetRet(callNode, NewTemp(returnType));
                            callNode.attributes.Remove(eAttr.store_expr_result);
                        }
                        else if(returnTypeNotVoid == true && isSingleExprStmt == false)
                        {
                            SetRet(callNode, NewTemp(returnType));
                        }

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
                            EmitCode("PARAM", GetRet(callNode.argumantsNode.arguments[i]));
                        }
                        //this实参压栈（成员方法）    
                        if (callNode.isMemberAccessFunction == true)
                        {
                            if (callNode.funcNode is ObjectMemberAccessNode)
                            {
                                var objNode = (callNode.funcNode as ObjectMemberAccessNode).objectNode;
                                EmitCode("PARAM", GetRet(objNode));
                            }
                            else
                            {
                                EmitCode("PARAM", "this");
                            }
                        }

                        //调用  
                        if (callNode.isMemberAccessFunction == true)
                        {
                            EmitCode("MCALL", fullName, "%LITINT:" + argCount);

                            if(returnTypeNotVoid == true && isSingleExprStmt == false)//返回值不为null且不是单表达式节点  
                            {
                                EmitCode("=", GetRet(callNode), "%RET");
                            }
                        }
                        else
                        {
                            EmitCode("CALL", fullName, "%LITINT:" + argCount);

                            if(returnTypeNotVoid == true && isSingleExprStmt == false)//返回值不为null且不是单表达式节点  
                            {
                                EmitCode("=", GetRet(callNode), "%RET");
                            }
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
                            string elemType = (string)eleAccessNode.attributes[eAttr.type];
                            string tmp = NewTemp(elemType);
                            EmitCode("=", tmp, accessExpr); // tmp = [container[index]]
                            SetRet(eleAccessNode, tmp);
                        }
                    }
                    break;
                case NewObjectNode newObjNode:
                    {
                        string className = newObjNode.className.FullName;

                        //表达式的返回变量 (始终存到临时变量)    
                        SetRet(newObjNode, NewTemp(className));
                        if(newObjNode.attributes.ContainsKey(eAttr.store_expr_result))
                        {
                            newObjNode.attributes.Remove(eAttr.store_expr_result);
                        }

                        EmitCode("ALLOC", GetRet(newObjNode), className);
                        EmitCode("PARAM", GetRet(newObjNode));
                        EmitCode("CALL", className + ".ctor", "%LITINT:" + 1);
                    }
                    break;
                case NewArrayNode newArrNode:
                    {
                        //长度计算  
                        GenNode(newArrNode.lengthNode);

                        //表达式的返回变量 (始终存到临时变量)    
                        SetRet(newArrNode, NewTemp(newArrNode.typeNode.TypeExpression() + "[]"));
                        if(newArrNode.attributes.ContainsKey(eAttr.store_expr_result))
                        {
                            newArrNode.attributes.Remove(eAttr.store_expr_result);
                        }

                        EmitCode("ALLOC_ARRAY", GetRet(newArrNode), GetRet(newArrNode.lengthNode));
                    }
                    break;
                case IncDecNode incDecNode:
                    {
                        string identifierName = incDecNode.identifierNode.FullName;
                        if (incDecNode.isOperatorFront)//++i
                        {
                            EmitCode(incDecNode.op, identifierName);
                            SetRet(incDecNode, identifierName);
                        }
                        else//i++
                        {
                            SetRet(incDecNode, NewTemp(Query(identifierName).typeExpression));

                            bool isSingleExprStmt = incDecNode.Parent is SingleExprStmtNode;
                            if(isSingleExprStmt == false)
                            {
                                EmitCode("=", GetRet(incDecNode), identifierName);
                            }
                            
                            EmitCode(incDecNode.op, identifierName);
                        }
                    }
                    break;
                default:
                    throw new SemanticException(ExceptioName.Undefine, node, "IR generation not implemtented:" + node.GetType().Name);
            }


            // 确保所有的临时属性已起效  
            for(eAttr i = eAttr.__codegen_mark_min + 1; i < eAttr.__codegen_mark_max; ++i)
            {
                if(node.attributes.ContainsKey(i))
                {
                    throw new GizboxException(ExceptioName.Undefine, $"node attribute not clear: {i.ToString()}.");
                }
            }
        }

        public TAC EmitCode(string op, object arg1 = null, object arg2 = null, object arg3 = null)
        {
            var newCode = new TAC() { op = op, arg0 = arg1?.ToString(), arg1 = arg2?.ToString(), arg2 = arg3?.ToString() };

            ilUnit.codes.Add(newCode);

            return newCode;
        }

        public void EmitDeleteArrayCode(string expr)
        {
            //todo: 如果是类对象数组，逐元素调用析构  
            EmitCode("DEALLOC_ARRAY", expr);
        }
        public void EmitDeleteCode(string expr)
        {
            //todo:调用析构  
            EmitCode("DEALLOC", expr);
        }
        public void EmitOwnDropCode(string varname)
        {
            EmitDeleteCode(varname);
        }
        public void EmitOwnConditionalDropCode(LifetimeInfo.VarStatus status, string varname)
        {
            if(status == LifetimeInfo.VarStatus.Alive) //// 无条件删除：delete aaa;
            {
                EmitOwnDropCode(varname);
            }
            else if(status == LifetimeInfo.VarStatus.PossiblyDead)  //// 条件删除：if (aaa != null) delete aaa;  
            {
                string tmp = RentTemp("bool", "drop_flag"); //借用共享的临时bool变量
                EmitCode("!=", tmp, varname, "%LITNULL:");
                string label = $"_owner_skip_drop_{varname}_{envStackTemp.Count}"; //用变量名和作用域深度避免重名  
                EmitCode("IF_FALSE_JUMP", tmp, "%LABEL:" + label);
                EmitOwnDropCode(varname);
                EmitCode("").label = label;
            }
            else
            {
                throw new GizboxException(ExceptioName.Undefine, "Cannot generate DEL code for dead owner variable.");
            }
        }

        public void EmitOwnDropField(string varname, string fieldName)
        {
            string accessExpr = varname + "->" + fieldName;

            string tempFlag = RentTemp("bool", "drop_field");
            EmitCode("!=", tempFlag, accessExpr, "%LITNULL:");
            string label = $"_owner_field_skip_{fieldName}_{envStackTemp.Count}_{tmpCounter}";
            EmitCode("IF_FALSE_JUMP", tempFlag, "%LABEL:" + label);

            EmitOwnDropCode(accessExpr);
            //EmitCode("=", accessExpr, "%LITNULL:"); //是否必要？  
            EmitCode("").label = label;
        }

        public void EmitCtor(ClassDeclareNode classDeclNode)
        {
            //隐式构造函数 
            //全名    
            string funcFullName = classDeclNode.classNameNode.FullName + ".ctor";

            ////跳过声明  
            //GenerateCode("JUMP", "%LABEL:exit:" + funcFullName);

            //函数开始    
            var ctorEnv = envStackTemp.Peek().GetTableInChildren(classDeclNode.classNameNode.FullName + ".ctor");
            envStackTemp.Push(ctorEnv);
            EnvBegin(ctorEnv);

            EmitCode("").label = "entry:" + funcFullName;

            EmitCode("FUNC_BEGIN", funcFullName).label = "func_begin:" + funcFullName;


            //基类构造函数调用  
            if(classDeclNode.baseClassNameNode != null)
            {
                var baseClassName = classDeclNode.baseClassNameNode.FullName;
                var baseRec = Query(baseClassName);
                var baseEnv = baseRec.envPtr;

                EmitCode("PARAM", "this");
                EmitCode("CALL", baseClassName + ".ctor", "%LITINT:1");
            }

            //成员变量初始化
            foreach(var memberDecl in classDeclNode.memberDelareNodes)
            {
                if(memberDecl is VarDeclareNode)
                {
                    var fieldDecl = memberDecl as VarDeclareNode;

                    GenNode(fieldDecl.initializerNode);
                    EmitCode("=", "this->" + fieldDecl.identifierNode.FullName, GetRet(fieldDecl.initializerNode));
                }
            }

            //其他语句(Not Implement)  
            //...  

            EmitCode("RETURN");

            EmitCode("FUNC_END").label = "func_end:" + funcFullName;

            EmitCode("").label = "exit:" + funcFullName;

            EnvEnd(ctorEnv);
            envStackTemp.Pop();
        }

        public void EmitDtor(ClassDeclareNode classDeclNode)
        {
            //todo:析构函数  
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
            var mainEntry = new TAC() { op = string.Empty, label = $"entry:{this.topFunc}" };
            ilUnit.codes.Add(mainEntry);
            ilUnit.codes.Add(new TAC() { op = "FUNC_BEGIN", arg0 = $"{this.topFunc}", label = $"func_begin:{this.topFunc}" });
            AppendLast(temp1.Count, temp1);
            ilUnit.codes.Add(new TAC() { op = "FUNC_END", arg0 = $"{this.topFunc}", label = $"func_end:{this.topFunc}" });
            var mainExit = new TAC() { op = string.Empty, label = $"exit:{this.topFunc}" };
            ilUnit.codes.Add(mainExit);

            //Main作用域构建
            var mainEnv = new SymbolTable($"{this.topFunc}", SymbolTable.TableCatagory.FuncScope, globalEnv);
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
            var mainRec = globalEnv.NewRecord($"{this.topFunc}", SymbolTable.RecordCatagory.Function, "-> void", mainEnv);


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

            if (exprNode.attributes.ContainsKey(eAttr.ret))
            {
                return (string)exprNode.attributes[eAttr.ret];
            }
            else
            {
                throw new SemanticException(ExceptioName.SubExpressionNoReturnVariable, exprNode, "");
            }
        }
        private void SetRet(SyntaxTree.Node node, string val)
        {
            node.attributes[eAttr.ret] = val;
        }

        public string NewTemp(string type)
        {
            string tempVarName = "tmp@" + tmpCounter++;

            var env = envStackTemp.Peek();

            env.NewRecord(tempVarName, SymbolTable.RecordCatagory.Variable, type);

            return tempVarName;
        }

        public string RentTemp(string type, string name)
        {
            string tempVarName = "tmp@" + name;

            var env = envStackTemp.Peek();

            if(env.ContainRecordName(tempVarName) == false)
            {
                env.NewRecord(tempVarName, SymbolTable.RecordCatagory.Variable, type);
            }

            return tempVarName;
        }

        private string GenLitOperandStr(LiteralNode literalNode)
        {
            //表达式的返回变量  
            string operandStr;

            if (literalNode.attributes.ContainsKey(eAttr.type))
            {
                string typeName = (string)literalNode.attributes[eAttr.type];

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
