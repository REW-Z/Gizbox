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
        private int markCounter = 0;//防重复标签自增  



        public IRGenerator(SyntaxTree ast, IRUnit ir, bool isMainUnit)
        {
            this.ilUnit = ir;
            this.ast = ast;
            this.topFunc = $"__top_{ir.name}__";
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
                        envStackTemp.Push(blockNode.attributes[AstAttr.env] as SymbolTable);
                        EnvBegin(blockNode.attributes[AstAttr.env] as SymbolTable);


                        foreach (var stmt in blockNode.statements)
                        {
                            GenNode(stmt);
                        }


                        // 作用域退出 -> 删除存活的Owner类型  
                        if(blockNode.attributes.ContainsKey(AstAttr.drop_var_exit_env))
                        {
                            var toDelete = blockNode.attributes[AstAttr.drop_var_exit_env] as List<(LifetimeInfo.VarStatus status, string varname)>;
                            if(toDelete != null)
                            {
                                foreach(var (s, v) in toDelete)
                                {
                                    EmitOwnConditionalDropCode(s, v);
                                }
                            }
                            blockNode.attributes.Remove(AstAttr.drop_var_exit_env);
                        }

                        envStackTemp.Pop();
                        EnvEnd(blockNode.attributes[AstAttr.env] as SymbolTable);
                    }
                    break;

                // *******************  语句节点 *********************************


                //类声明  
                case ClassDeclareNode classDeclNode:
                    {
                        if (classDeclNode.isTemplateClass)
                            break;

                        string className = classDeclNode.classNameNode.FullName;
                        //GenerateCode("JUMP", "%LABEL:class_end:" + className);//顶级语句重排之后不再需要跳过

                        envStackTemp.Push(classDeclNode.attributes[AstAttr.env] as SymbolTable);
                        EnvBegin(classDeclNode.attributes[AstAttr.env] as SymbolTable);

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

                        EnvEnd(classDeclNode.attributes[AstAttr.env] as SymbolTable);
                        envStackTemp.Pop();
                    }
                    break;
                //函数声明
                case FuncDeclareNode funcDeclNode:
                    {
                        // skip template function definitions
                        if (funcDeclNode.isTemplateFunction)
                            break;
                        //是否是实例成员函数  
                        bool isMethod = envStackTemp.Peek().tableCatagory == SymbolTable.TableCatagory.ClassScope;

                        //函数全名(修饰)    
                        string funcFinalName;
                        if (isMethod)
                            funcFinalName = envStackTemp.Peek().name + "." + (string)funcDeclNode.attributes[AstAttr.mangled_name];
                        else
                            funcFinalName = (string)funcDeclNode.attributes[AstAttr.mangled_name];


                        //函数开始    
                        
                        envStackTemp.Push(funcDeclNode.attributes[AstAttr.env] as SymbolTable);
                        EnvBegin(funcDeclNode.attributes[AstAttr.env] as SymbolTable);

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
                        if(funcDeclNode.attributes.ContainsKey(AstAttr.drop_var_exit_env))
                        {
                            var toDelete = funcDeclNode.attributes[AstAttr.drop_var_exit_env] as List<(LifetimeInfo.VarStatus, string)>;
                            if(toDelete != null)
                            {
                                foreach(var (s, v) in toDelete)
                                {
                                    EmitOwnConditionalDropCode(s, v);
                                }
                            }
                            funcDeclNode.attributes.Remove(AstAttr.drop_var_exit_env);
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

                        EnvEnd(funcDeclNode.attributes[AstAttr.env] as SymbolTable);
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
                        if(varDeclNode.attributes.ContainsKey(AstAttr.set_null_after_stmt))
                        {
                            var toNull = varDeclNode.attributes[AstAttr.set_null_after_stmt] as List<string>;
                            if(toNull != null)
                            {
                                foreach(var v in toNull)
                                {
                                    EmitCode("=", v, "%LITNULL:");
                                }
                            }
                            varDeclNode.attributes.Remove(AstAttr.set_null_after_stmt);
                        }

                        // 被移动字段置NULL（作为drop-flag）
                        if(varDeclNode.attributes.ContainsKey(AstAttr.set_null_field_after_stmt))
                        {
                            if(varDeclNode.attributes[AstAttr.set_null_field_after_stmt] is SyntaxTree.ObjectMemberAccessNode fieldNode)
                            {
                                // ensure object expr ret exists
                                GenNode(fieldNode.objectNode);
                                string obj = TrimName(GetRet(fieldNode.objectNode));
                                string field = fieldNode.memberNode.FullName;
                                EmitCode("=", obj + "->" + field, "%LITNULL:");
                            }
                            varDeclNode.attributes.Remove(AstAttr.set_null_field_after_stmt);
                        }
                    }
                    break;
                case OwnershipCaptureStmtNode captureNode:
                    {
                        GenNode(captureNode.lIdentifier);
                        GenNode(captureNode.rIdentifier);

                        var tac = EmitCode("=", GetRet(captureNode.lIdentifier), GetRet(captureNode.rIdentifier));

                        // 被捕获变量置NULL  
                        if(captureNode.attributes.ContainsKey(AstAttr.set_null_after_stmt))
                        {
                            var toNull = captureNode.attributes[AstAttr.set_null_after_stmt] as List<string>;
                            if(toNull != null)
                            {
                                foreach(var v in toNull)
                                {
                                    EmitCode("=", v, "%LITNULL:");
                                }
                            }
                            captureNode.attributes.Remove(AstAttr.set_null_after_stmt);
                        }
                    }
                    break;
                case OwnershipLeakStmtNode leakNode:
                    {
                        GenNode(leakNode.lIdentifier);
                        GenNode(leakNode.rIdentifier);

                        var tac = EmitCode("=", GetRet(leakNode.lIdentifier), GetRet(leakNode.rIdentifier));

                        // 被泄露变量置NULL  
                        if(leakNode.attributes.ContainsKey(AstAttr.set_null_after_stmt))
                        {
                            var toNull = leakNode.attributes[AstAttr.set_null_after_stmt] as List<string>;
                            if(toNull != null)
                            {
                                foreach(var v in toNull)
                                {
                                    EmitCode("=", v, "%LITNULL:");
                                }
                            }
                            leakNode.attributes.Remove(AstAttr.set_null_after_stmt);
                        }
                    }
                    break;

                case ReturnStmtNode returnNode:
                    {
                        if(returnNode.returnExprNode != null)
                        {
                            GenNode(returnNode.returnExprNode);
                        }

                        // owner-return 的字段 move-out：return 前把字段置NULL（drop-flag）
                        if(returnNode.attributes.ContainsKey(AstAttr.set_null_field_after_stmt))
                        {
                            if(returnNode.attributes[AstAttr.set_null_field_after_stmt] is SyntaxTree.ObjectMemberAccessNode fieldNode)
                            {
                                GenNode(fieldNode.objectNode);
                                string obj = TrimName(GetRet(fieldNode.objectNode));
                                string field = fieldNode.memberNode.FullName;
                                EmitCode("=", obj + "->" + field, "%LITNULL:");
                            }
                            returnNode.attributes.Remove(AstAttr.set_null_field_after_stmt);
                        }

                        // return之前先回收需要回收的Owner类型  
                        if(returnNode.attributes.ContainsKey(AstAttr.drop_var_before_return))
                        {
                            var toDelete = returnNode.attributes[AstAttr.drop_var_before_return] as List<(LifetimeInfo.VarStatus, string)>;
                            if(toDelete != null)
                            {
                                foreach(var (s, v) in toDelete)
                                {
                                    EmitOwnConditionalDropCode(s, v);
                                }
                            }
                            returnNode.attributes.Remove(AstAttr.drop_var_before_return);
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
                            EmitDeleteVarCode(GetRet(deleteNode.objToDelete));
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
                        if(singleExprNode.attributes.ContainsKey(AstAttr.drop_expr_result_after_stmt))
                        {
                            var exprs = singleExprNode.attributes[AstAttr.drop_expr_result_after_stmt] as List<SyntaxTree.ExprNode>;
                            if(exprs != null)
                            {
                                foreach(var e in exprs)
                                {
                                    EmitOwnDropCode(GetRet(e));
                                }
                            }
                            singleExprNode.attributes.Remove(AstAttr.drop_expr_result_after_stmt);
                        }
                    }
                    break;

                case IfStmtNode ifNode:
                    {
                        int ifCounter = (int)ifNode.attributes[AstAttr.uid];

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
                        int whileCounter = (int)whileNode.attributes[AstAttr.uid];

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
                        int forCounter = (int)forNode.attributes[AstAttr.uid];

                        envStackTemp.Push(forNode.attributes[AstAttr.env] as SymbolTable);
                        EnvBegin(forNode.attributes[AstAttr.env] as SymbolTable);

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

                        EnvEnd(forNode.attributes[AstAttr.env] as SymbolTable);
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
                case DefaultValueNode defaultNode:
                    {
                        string typeExpr = defaultNode.typeNode.TypeExpression();
                        string temp = NewTemp(typeExpr);
                        EmitCode("=", temp, GenDefaultOperandStr(typeExpr));
                        SetRet(defaultNode, temp);
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
                            string valueType = (string)objMemberAccess.attributes[AstAttr.type];
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

                        if (binaryOp.leftNode.attributes.ContainsKey(AstAttr.type) == false)
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
                            SetRet(binaryOp, NewTemp((string)binaryOp.leftNode.attributes[AstAttr.type]));
                        }
                        

                        EmitCode(binaryOp.op, GetRet(binaryOp), GetRet(binaryOp.leftNode), GetRet(binaryOp.rightNode));
                    }
                    break;
                case UnaryOpNode unaryOp:
                    {
                        GenNode(unaryOp.exprNode);

                        //表达式的返回变量  
                        SetRet(unaryOp, NewTemp((string)unaryOp.exprNode.attributes[AstAttr.type]));

                    EmitCode(unaryOp.op, GetRet(unaryOp), GetRet(unaryOp.exprNode));
                    }
                    break;
                case AssignNode assignNode:
                    {
                        GenNode(assignNode.lvalueNode);
                        GenNode(assignNode.rvalueNode);


                        // 赋值前先删除之前的Owner
                        if(assignNode.attributes.ContainsKey(AstAttr.drop_var_before_assign_stmt))
                        {
                            var toDelete = assignNode.attributes[AstAttr.drop_var_before_assign_stmt] as List<(LifetimeInfo.VarStatus, string)>;
                            if(toDelete != null)
                            {
                                foreach(var (s, v) in toDelete)
                                {
                                    EmitOwnConditionalDropCode(s, v);
                                }
                            }
                            assignNode.attributes.Remove(AstAttr.drop_var_before_assign_stmt);
                        }
                        // 成员字段赋值前先条件删除  
                        if(assignNode.attributes.ContainsKey(AstAttr.drop_field_before_assign_stmt))
                        {
                            var accessNode = assignNode.lvalueNode as SyntaxTree.ObjectMemberAccessNode;
                            var obj = GetRet(accessNode.objectNode);
                            var field = accessNode.memberNode.FullName;
                            EmitOwnDropField(obj, field);

                            assignNode.attributes.Remove(AstAttr.drop_field_before_assign_stmt);
                        }

                        // 复合赋值表达式的返回变量为左值    
                        EmitCode(assignNode.op, GetRet(assignNode.lvalueNode), GetRet(assignNode.rvalueNode));


                        // 移动源置NULL  
                        if(assignNode.attributes.ContainsKey(AstAttr.set_null_after_stmt))
                        {
                            var toNull = assignNode.attributes[AstAttr.set_null_after_stmt] as List<string>;
                            if(toNull != null)
                            {
                                foreach(var v in toNull)
                                {
                                    EmitCode("=", v, "%LITNULL:");
                                }
                            }
                            assignNode.attributes.Remove(AstAttr.set_null_after_stmt);
                        }

                        // 被移动字段置NULL（作为drop-flag）
                        if(assignNode.attributes.ContainsKey(AstAttr.set_null_field_after_stmt))
                        {
                            if(assignNode.attributes[AstAttr.set_null_field_after_stmt] is SyntaxTree.ObjectMemberAccessNode fieldNode)
                            {
                                GenNode(fieldNode.objectNode);
                                string obj = TrimName(GetRet(fieldNode.objectNode));
                                string field = fieldNode.memberNode.FullName;
                                EmitCode("=", obj + "->" + field, "%LITNULL:");
                            }
                            assignNode.attributes.Remove(AstAttr.set_null_field_after_stmt);
                        }
                    }
                    break;
                case ReplaceNode replaceNode:
                    {
                        if(replaceNode.targetNode is not ObjectMemberAccessNode targetAccess)
                            throw new SemanticException(ExceptioName.OwnershipError, replaceNode, "replace target must be a field access.");

                        GenNode(targetAccess.objectNode);
                        string obj = TrimName(GetRet(targetAccess.objectNode));
                        string field = targetAccess.memberNode.FullName;
                        string accessExpr = obj + "->" + field;

                        string returnType = (string)replaceNode.attributes[AstAttr.type];
                        string tmp = NewTemp(returnType);
                        EmitCode("=", tmp, accessExpr);

                        GenNode(replaceNode.newValueNode);
                        EmitCode("=", accessExpr, GetRet(replaceNode.newValueNode));

                        SetRet(replaceNode, tmp);

                        if(replaceNode.attributes.ContainsKey(AstAttr.set_null_after_stmt))
                        {
                            var toNull = replaceNode.attributes[AstAttr.set_null_after_stmt] as List<string>;
                            if(toNull != null)
                            {
                                foreach(var v in toNull)
                                {
                                    EmitCode("=", v, "%LITNULL:");
                                }
                            }
                            replaceNode.attributes.Remove(AstAttr.set_null_after_stmt);
                        }
                    }
                    break;
                case CallNode callNode:
                    {
                        //函数全名  
                        string fullName;
                        if(callNode.attributes.TryGetValue(AstAttr.mangled_name, out object oMangleName))
                        {
                            fullName = (string)oMangleName;
                        }
                        else if(callNode.attributes.TryGetValue(AstAttr.extern_name, out object oExternName))
                        {
                            fullName= (string)oExternName;
                        }
                        else
                        {
                            throw new SemanticException(ExceptioName.FunctionObfuscationNameNotSet, callNode, "Mangled function name and extern function name not found.");
                        }


                        //函数返回类型    
                        string returnType = (string)callNode.attributes[AstAttr.type];

                        //是否有返回值且作为右值  
                        bool returnTypeNotVoid = GType.Parse(returnType).Category != GType.Kind.Void;
                        bool isSingleExprStmt = (callNode.Parent is SingleExprStmtNode stmt && stmt.exprNode == callNode);


                        //表达式的返回变量  
                        bool store_result = callNode.attributes.ContainsKey(AstAttr.store_expr_result);
                        if(store_result)
                        {
                            callNode.attributes.Remove(AstAttr.store_expr_result);
                            SetRet(callNode, NewTemp(returnType));
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
                        }
                        else
                        {
                            EmitCode("CALL", fullName, "%LITINT:" + argCount);
                        }


                        //返回非void
                        if(returnTypeNotVoid == true)
                        {
                            // 不是单表达式节点 || 有store_expr_result属性    
                            if(isSingleExprStmt == false || store_result)
                            {
                                EmitCode("=", GetRet(callNode), "%RET");
                            }
                        }


                        //调用后把moved-from的实参变量置NULL(作为drop-flag)  
                        if(callNode.attributes.ContainsKey(AstAttr.set_null_after_call))
                        {
                            var toNull = callNode.attributes[AstAttr.set_null_after_call] as List<string>;
                            if(toNull != null)
                            {
                                foreach(var v in toNull)
                                {
                                    EmitCode("=", v, "%LITNULL:");
                                }
                            }
                            callNode.attributes.Remove(AstAttr.set_null_after_call);
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
                            string elemType = (string)eleAccessNode.attributes[AstAttr.type];
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
                        if(newObjNode.attributes.ContainsKey(AstAttr.store_expr_result))
                        {
                            newObjNode.attributes.Remove(AstAttr.store_expr_result);
                        }

                        EmitCode("ALLOC", GetRet(newObjNode), className);
                        EmitCode("PARAM", GetRet(newObjNode));
                        EmitCode("CALL", className + "::ctor", "%LITINT:" + 1);
                    }
                    break;
                case NewArrayNode newArrNode:
                    {
                        //长度计算  
                        GenNode(newArrNode.lengthNode);

                        //表达式的返回变量 (始终存到临时变量)    
                        SetRet(newArrNode, NewTemp(newArrNode.typeNode.TypeExpression() + "[]"));
                        if(newArrNode.attributes.ContainsKey(AstAttr.store_expr_result))
                        {
                            newArrNode.attributes.Remove(AstAttr.store_expr_result);
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
                case SizeOfNode sizeofNode:
                    {
                        GType t = GType.Parse(sizeofNode.typeNode.TypeExpression());
                        SetRet(sizeofNode, $"%LITINT:{t.Size}");
                    }
                    break;
                case TypeOfNode typeofNode:
                    {
                        SetRet(typeofNode, $"%CONSTTYPE:{typeofNode.typeNode.TypeExpression()}");
                    }
                    break;
                default:
                    throw new SemanticException(ExceptioName.Undefine, node, "IR generation not implemtented:" + node.GetType().Name);
            }


            // 确保所有的临时属性已起效  
            for(AstAttr i = AstAttr.__codegen_mark_min + 1; i < AstAttr.__codegen_mark_max; ++i)
            {
                if(node.attributes.ContainsKey(i))
                {
                    throw new GizboxException(ExceptioName.Undefine, $"node attribute not clear: {i.ToString()}.");
                }
            }
        }

        public TAC EmitCode(string op, object arg1 = null, object arg2 = null, object arg3 = null, string comment = null)
        {
            var newCode = new TAC() { op = op, arg0 = arg1?.ToString(), arg1 = arg2?.ToString(), arg2 = arg3?.ToString() };
            newCode.comment = comment;

            ilUnit.codes.Add(newCode);

            return newCode;
        }

        public void EmitDeleteArrayCode(string expr)
        {
            //目前语言只支持原子类型和引用类型的元素，删除数组不需要调用析构函数。  
            //以后支持结构体类型元素后，也不会支持结构体析构/构造行为。    
            EmitCode("DEALLOC_ARRAY", expr);
        }
        public void EmitDeleteVarCode(string varname)
        {
            //需要确保参数是变量名，而不是复杂表达式  
            //delete复杂表达式时，确保表达式节点SetRet的返回是临时变量    

            if(string.IsNullOrEmpty(varname))
                throw new GizboxException(ExceptioName.Undefine, "delete expr is empty.");

            if(varname == "%LITNULL:")
                throw new GizboxException(ExceptioName.Undefine, "Cannot delete null literal.");

            //推导要delete的对象类型  
            var objRec = Query(varname);
            string objType = objRec?.typeExpression;
            if(string.IsNullOrEmpty(objType))
                throw new GizboxException(ExceptioName.Undefine, "Cannot find the type of delete expr.");
            GType gtype = GType.Parse(objType);

            // 不能用delete删除数组类型 (应该放在语义分析阶段判断)
            if(gtype.IsArray)
                throw new GizboxException(ExceptioName.Undefine, "Cannot use delete to delete array type.");

            // 字符串类型当前设计没有隐式dtor ->  直接释放
            if(gtype.Category == GType.Kind.String)
            {
                EmitCode("DEALLOC", varname);
                return;
            }

            //class对象 ->  先调用dtor再释放内存  
            EmitCode("PARAM", varname);
            EmitCode("CALL", gtype.ToString() + "::dtor", "%LITINT:1");
            EmitCode("DEALLOC", varname);
        }


        public void EmitOwnDropCode(string varname)
        {
            EmitDeleteVarCode(varname);
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
                string label = $"_owner_skip_drop_{varname}_{markCounter++}";
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

            //if (obj->field != null) { delete obj->field; obj->field = null; }  //null作为dropflag  
            string tmp = RentTemp("bool", "drop_field_flag");
            EmitCode("!=", tmp, accessExpr, "%LITNULL:");

            string label = $"_owner_skip_drop_field_{fieldName}_{markCounter++}";
            EmitCode("IF_FALSE_JUMP", tmp, "%LABEL:" + label);

            //赋值到共用临时变量再删除  
            if(false)
            {
                var objRec = Query(varname);
                var objType = objRec.typeExpression;
                var classRec = Query(objType);
                var fieldRec = classRec.envPtr.GetRecord(fieldName);
                string tmp2 = RentTemp(fieldRec.typeExpression, "objfield_ptr");
                EmitCode("=", tmp2, accessExpr);
                EmitDeleteVarCode(tmp2);
            }
            //直接生成DEALLOC成员访问表达式的语句  
            else
            {
                var objRec = Query(varname);
                var objType = objRec.typeExpression;
                var classRec = Query(objType);
                var fieldRec = classRec.envPtr.GetRecord(fieldName);

                GType gtype = GType.Parse(fieldRec.typeExpression);

                // 不能用delete删除数组类型 (应该放在语义分析阶段判断)
                if(gtype.IsArray)
                    throw new GizboxException(ExceptioName.Undefine, "Cannot use delete to delete array type.");

                // 字符串类型当前设计没有隐式dtor ->  直接释放
                if(gtype.Category == GType.Kind.String)
                {
                    EmitCode("DEALLOC", accessExpr);
                    return;
                }

                //class对象 ->  先调用dtor再释放内存  
                EmitCode("PARAM", accessExpr);
                EmitCode("CALL", gtype.ToString() + "::dtor", "%LITINT:1");
                EmitCode("DEALLOC", accessExpr);
            }



            EmitCode("=", accessExpr, "%LITNULL:");

            EmitCode("").label = label;
        }

        public void EmitCtor(ClassDeclareNode classDeclNode)
        {
            //隐式构造函数 
            //全名    
            string funcFullName = classDeclNode.classNameNode.FullName + "::ctor";

            //函数开始    
            var ctorEnv = envStackTemp.Peek().GetTableInChildren(classDeclNode.classNameNode.FullName + "::ctor");
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
                EmitCode("CALL", baseClassName + "::ctor", "%LITINT:1");
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
            //隐式析构函数
            string funcFullName = classDeclNode.classNameNode.FullName + "::dtor";

            //函数开始    
            var dtorEnv = envStackTemp.Peek().GetTableInChildren(funcFullName);
            envStackTemp.Push(dtorEnv);
            EnvBegin(dtorEnv);

            EmitCode("").label = "entry:" + funcFullName;
            EmitCode("FUNC_BEGIN", funcFullName).label = "func_begin:" + funcFullName;

            //先析构派生类的Owner字段（逆序）
            //仅处理：变量声明节点 + 该字段是 OwnerVar（由语义分析标记）
            for(int i = classDeclNode.memberDelareNodes.Count - 1; i >= 0; --i)
            {
                if(classDeclNode.memberDelareNodes[i] is not VarDeclareNode fieldDecl)
                    continue;

                var classEnv = envStackTemp[envStackTemp.Count - 2]; // 当前类的 env（dtor env 之下一级）
                if(classEnv == null)
                    continue;

                if(classEnv.ContainRecordName(fieldDecl.identifierNode.FullName) == false)
                    continue;

                var fieldRec = classEnv.GetRecord(fieldDecl.identifierNode.FullName);
                if(fieldRec.flags.HasFlag(SymbolTable.RecordFlag.OwnerVar) == false)
                    continue;

                EmitOwnDropField("this", fieldDecl.identifierNode.FullName);
            }

            //再调用基类析构  
            if(classDeclNode.baseClassNameNode != null)
            {
                var baseClassName = classDeclNode.baseClassNameNode.FullName;

                EmitCode("PARAM", "this");
                EmitCode("CALL", baseClassName + "::dtor", "%LITINT:1");
            }

            EmitCode("RETURN");

            EmitCode("FUNC_END").label = "func_end:" + funcFullName;
            EmitCode("").label = "exit:" + funcFullName;

            EnvEnd(dtorEnv);
            envStackTemp.Pop();
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

            if (exprNode.attributes.ContainsKey(AstAttr.ret))
            {
                return (string)exprNode.attributes[AstAttr.ret];
            }
            else
            {
                throw new SemanticException(ExceptioName.SubExpressionNoReturnVariable, exprNode, "");
            }
        }
        private void SetRet(SyntaxTree.Node node, string val)
        {
            node.attributes[AstAttr.ret] = val;
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

            if (literalNode.attributes.ContainsKey(AstAttr.type))
            {
                string typeName = (string)literalNode.attributes[AstAttr.type];

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

        private string GenDefaultOperandStr(string typeExpr)
        {
            var gtype = GType.Parse(typeExpr);
            if (gtype.IsReferenceType)
            {
                return "%LITNULL:";
            }

            switch (typeExpr)
            {
                case "bool":
                    return "%LITBOOL:false";
                case "char":
                    return "%LITCHAR:'\\0'";
                case "byte":
                    return "%LITINT:0";
                case "uint":
                    return "%LITUINT:0u";
                case "float":
                    return "%LITFLOAT:0.0f";
                case "double":
                    return "%LITDOUBLE:0.0d";
                case "ulong":
                    return "%LITULONG:0ul";
                case "long":
                    return "%LITLONG:0L";
                case "int":
                    return "%LITINT:0";
                case "string":
                    return "%LITNULL:";
            }

            return "%LITNULL:";
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
