using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FanLang;

namespace FanLang.IR
{
    public class TAC
    {
        public string label;

        public string op;
        public string arg1;
        public string arg2;
        public string arg3;

        public string ToExpression(bool showlabel = true)
        {
            string str = "";

            if(showlabel)
            {
                if (string.IsNullOrEmpty(label))
                {
                    str += new string(' ', 20);
                }
                else
                {
                    str += (label + ":").PadRight(20);
                }
            }
            str += op;

            if(string.IsNullOrEmpty(arg1) == false)
            {
                str += (" " + arg1);
            }
            if (string.IsNullOrEmpty(arg2) == false)
            {
                str += (" " + arg2);
            }
            if (string.IsNullOrEmpty(arg3) == false)
            {
                str += (" " + arg3);
            }
            return str;
        }
    }

    public class Scope
    {
        public int lineFrom;
        public int lineTo;
        public SymbolTable env;
    }

    public class IntermediateCodes
    {
        //中间代码信息  
        public Dictionary<string, int> labelDic = new Dictionary<string, int>();
        public List<TAC> codes = new List<TAC>();
        public int[] scopeStatusArr;
        public int codeEntry;
        public List<Scope> scopes = new List<Scope>();
        public Scope globalScope = new Scope();

        //完成构建  
        public void Complete()
        {
            globalScope.lineFrom = 0;
            globalScope.lineTo = codes.Count - 1;

            FillLabelDic();
            BuildMarkArray();
        }
        //填充作用域标记  
        private void BuildMarkArray()
        {
            scopes.Sort((s1, s2) => s1.env.depth - s2.env.depth);

            this.scopeStatusArr = new int[this.codes.Count];
            for (int i = 0; i < this.scopeStatusArr.Length; i++)
            {
                this.scopeStatusArr[i] = 0;
            }
            int status = 0; ;
            foreach (var scope in scopes)
            {
                status++;
                for (int i = scope.lineFrom; i <= scope.lineTo; ++i)
                {
                    this.scopeStatusArr[i] = status;
                }
            }
        }
        //填充字典  
        private void FillLabelDic()
        {
            for (int i = 0; i < codes.Count; ++i)
            {
                if (string.IsNullOrEmpty(codes[i].label) == false)
                {
                    labelDic[codes[i].label] = i;
                }
            }
        }

        //跳转行号时是否需要刷新作用域  
        public bool NeedRefreshStack(int prevLine, int currLine)
        {
            return this.scopeStatusArr[prevLine] != this.scopeStatusArr[currLine];
        }
        //所在符号表链  
        public void EnvHits(int currentLine, List<SymbolTable> envs)
        {
            envs.Clear();
            envs.Add(globalScope.env);
            foreach(var scope in scopes)
            {
                if(scope.lineFrom <= currentLine && currentLine <= scope.lineTo)
                {
                    envs.Add(scope.env);
                }
            }
        }
    }


    /// <summary>
    /// 中间代码生成器  
    /// </summary>
    public class ILGenerator
    {
        //public  
        public Compiler complierContext; 

        public SyntaxTree ast;

        public IntermediateCodes il;

        //log  
        private static bool enableLog = false;

        //temp info  
        private FanLang.Stack<SymbolTable> envStack = new Stack<SymbolTable>();

        //status  
        private int tmpCounter = 0;//临时变量自增    
        private Stack<string> loopExitStack = new Stack<string>();


        public ILGenerator(SyntaxTree ast, Compiler compilerContext)
        {
            this.complierContext = compilerContext;
            this.ast = ast;
        }
        
        public void Generate()
        {
            this.il = new IntermediateCodes();

            this.il.globalScope = new Scope() { env = complierContext.globalSymbolTable };

            this.tmpCounter = 0;

            this.envStack.Push(complierContext.globalSymbolTable);

            GenNode(ast.rootNode);

            this.il.Complete();


            if(enableLog) complierContext.globalSymbolTable.Print();
            Compiler.Pause("符号表建立完毕");

            this.PrintCodes();
            Compiler.Pause("中间代码生成完毕");

        }

        public void GenNode(SyntaxTree.Node node)
        {
            //节点代码生成  
            switch (node)
            {
                case SyntaxTree.ProgramNode programNode:
                    {
                        GenNode(programNode.statementsNode);
                    }
                    break;
                case SyntaxTree.StatementsNode stmtsNode:
                    {
                        foreach(var stmt in stmtsNode.statements)
                        {
                            GenNode(stmt);
                        }
                    }
                    break;
                case SyntaxTree.StatementBlockNode blockNode:
                    {
                        Log("--------------------");
                        foreach (var key in blockNode.attributes.Keys)
                        {
                            Log("KEY:" + key);
                        }
                        envStack.Push(blockNode.attributes["env"] as SymbolTable);
                        EnvBegin(blockNode.attributes["env"] as SymbolTable);


                        foreach (var stmt in blockNode.statements)
                        {
                            GenNode(stmt);
                        }

                        envStack.Pop();
                        EnvEnd(blockNode.attributes["env"] as SymbolTable);
                    }
                    break;

                // *******************  语句节点 *********************************


                //类声明  
                case SyntaxTree.ClassDeclareNode classDeclNode:
                    {
                        string className = classDeclNode.classNameNode.token.attribute;
                        GeneratorCode("JUMP", "exit:" + className);
                        GeneratorCode(" ").label = className;
                        //GeneratorCode("CLASS_BEGIN", classDeclNode.classNameNode.token.attribute);
                        envStack.Push(classDeclNode.attributes["env"] as SymbolTable);
                        EnvBegin(classDeclNode.attributes["env"] as SymbolTable);

                        //构造函数（默认）  
                        {
                            //构造函数全名    
                            string funcFullName = classDeclNode.classNameNode.token.attribute + ".ctor";

                            //跳过声明  
                            GeneratorCode("JUMP", "exit:" + funcFullName);

                            //函数开始    
                            GeneratorCode(" ").label = "entry:" + funcFullName;
                            EnvBegin(envStack.Peek().GetTableInChildren(classDeclNode.classNameNode.token.attribute + ".ctor"));
                            GeneratorCode("METHOD_BEGIN", funcFullName);


                            //成员变量初始化
                            foreach (var memberDecl in classDeclNode.memberDelareNodes)
                            {
                                if (memberDecl is SyntaxTree.VarDeclareNode)
                                {
                                    var fieldDecl = memberDecl as SyntaxTree.VarDeclareNode;

                                    GenNode(fieldDecl.initializerNode);
                                    GeneratorCode("=", "[this." + fieldDecl.identifierNode.token.attribute + "]", fieldDecl.initializerNode.attributes["ret"]);
                                }
                            }

                            //其他语句(Not Implement)  
                            //...  

                            GeneratorCode("RETURN");

                            GeneratorCode("METHOD_END");
                            EnvEnd(envStack.Peek().GetTableInChildren(classDeclNode.classNameNode.token.attribute + ".ctor"));
                            GeneratorCode(" ").label = "exit:" + funcFullName;
                        }

                        //成员函数
                        foreach (var memberDecl in classDeclNode.memberDelareNodes)
                        {
                            if (memberDecl is SyntaxTree.FuncDeclareNode)
                            {
                                GenNode(memberDecl as SyntaxTree.FuncDeclareNode);
                            }
                        }


                        EnvEnd(classDeclNode.attributes["env"] as SymbolTable);
                        envStack.Pop();
                        //GeneratorCode("CLASS_END");
                        GeneratorCode(" ").label = "exit:" + className;
                    }
                    break;
                //函数声明
                case SyntaxTree.FuncDeclareNode funcDeclNode:
                    {
                        //是否是实例成员函数  
                        bool isMethod = envStack.Peek().tableCatagory == SymbolTable.TableCatagory.ClassScope;

                        //函数全名    
                        string funcFullName;
                        if (isMethod)
                            funcFullName = envStack.Peek().name + "." + funcDeclNode.identifierNode.token.attribute;
                        else
                            funcFullName = funcDeclNode.identifierNode.token.attribute;

                        //跳过声明  
                        GeneratorCode("JUMP", "exit:" + funcFullName);


                        //函数开始    
                        GeneratorCode(" ").label = "entry:" + funcFullName;

                        envStack.Push(funcDeclNode.attributes["env"] as SymbolTable);
                        EnvBegin(funcDeclNode.attributes["env"] as SymbolTable);

                        if (isMethod)
                            GeneratorCode("METHOD_BEGIN", funcFullName);
                        else
                            GeneratorCode("FUNC_BEGIN", funcFullName);


                        //语句  
                        foreach (var stmt in funcDeclNode.statementsNode.statements)
                        {
                            GenNode(stmt);
                        }


                        if(funcDeclNode.statementsNode.statements.Any(s => s is SyntaxTree.ReturnStmtNode) == false)
                            GeneratorCode("RETURN");


                        if (isMethod)
                            GeneratorCode("METHOD_END", funcFullName);
                        else
                            GeneratorCode("FUNC_END", funcFullName);


                        EnvEnd(funcDeclNode.attributes["env"] as SymbolTable);
                        envStack.Pop();

                        GeneratorCode(" ").label = "exit:" + funcFullName;
                    }
                    break;
                case SyntaxTree.ExternFuncDeclareNode externFuncDeclNode:
                    {
                        //函数全名    
                        string funcFullName = externFuncDeclNode.identifierNode.token.attribute; 
                        

                        //跳过声明  
                        GeneratorCode("JUMP", "exit:" + funcFullName);

                        //函数开始    
                        GeneratorCode(" ").label = "entry:" + funcFullName;

                        envStack.Push(externFuncDeclNode.attributes["env"] as SymbolTable);
                        EnvBegin(externFuncDeclNode.attributes["env"] as SymbolTable);

                        GeneratorCode("FUNC_BEGIN", funcFullName);


                        GeneratorCode("EXTERN_IMPL", externFuncDeclNode.identifierNode.token.attribute);


                        GeneratorCode("FUNC_END", funcFullName);


                        EnvEnd(externFuncDeclNode.attributes["env"] as SymbolTable);
                        envStack.Pop();

                        GeneratorCode(" ").label = "exit:" + funcFullName;
                    }
                    break;
                //变量声明
                case SyntaxTree.VarDeclareNode varDeclNode:
                    {
                        GenNode(varDeclNode.identifierNode);
                        GenNode(varDeclNode.initializerNode);

                        if(varDeclNode.initializerNode.attributes.ContainsKey("ret") == false)
                        {
                            throw new Exception("子表达式节点无返回变量：" + varDeclNode.identifierNode.token.attribute);
                        }

                        GeneratorCode("=", (string)varDeclNode.identifierNode.attributes["ret"], (string)varDeclNode.initializerNode.attributes["ret"]);
                    }
                    break;

                case SyntaxTree.ReturnStmtNode returnNode:
                    {
                        if(returnNode.returnExprNode != null)
                        {
                            GenNode(returnNode.returnExprNode);
                            GeneratorCode("RETURN", returnNode.returnExprNode.attributes["ret"]);
                        }
                        else
                        {
                            GeneratorCode("RETURN");
                        }
                    }
                    break;

                case SyntaxTree.SingleExprStmtNode singleExprNode:
                    {
                        GenNode(singleExprNode.exprNode);
                    }
                    break;

                case SyntaxTree.IfStmtNode ifNode:
                    {
                        int ifCounter = (int)ifNode.attributes["uid"];

                        GeneratorCode(" ").label = ("If_" + ifCounter);

                        var elseClause = ifNode.elseClause;

                        for (int i = 0; i < ifNode.conditionClauseList.Count; ++i)
                        {
                            var clause = ifNode.conditionClauseList[i];

                            string falseGotoLabel;
                            if (i != ifNode.conditionClauseList.Count - 1)
                            {
                                falseGotoLabel = ("IfCondition_" + ifCounter + "_" + (i + 1));
                            }
                            else
                            {
                                if(elseClause != null)
                                {
                                    falseGotoLabel = ("ElseStmt_" + ifCounter);
                                }
                                else
                                {
                                    falseGotoLabel = ("EndIf_" + ifCounter);
                                }
                            }

                            GeneratorCode(" ").label = "IfCondition_" + ifCounter + "_" + i;

                            GenNode(clause.conditionNode);
                            GeneratorCode("IF_FALSE_JUMP", clause.conditionNode.attributes["ret"], falseGotoLabel);

                            GeneratorCode(" ").label = ("IfStmt_" + ifCounter + "_" + i);

                            GenNode(clause.thenNode);

                            GeneratorCode("JUMP", ("EndIf_" + ifCounter));
                        }

                        if(elseClause != null)
                        {
                            GeneratorCode(" ").label = ("ElseStmt_" + ifCounter);
                            GenNode(elseClause.stmt);
                        }


                        GeneratorCode(" ").label = ("EndIf_" + ifCounter);
                    }
                    break;
                case SyntaxTree.WhileStmtNode whileNode:
                    {
                        int whileCounter = (int)whileNode.attributes["uid"];

                        GeneratorCode(" ").label = ("While_" + whileCounter);

                        GenNode(whileNode.conditionNode);

                        GeneratorCode("IF_FALSE_JUMP", whileNode.conditionNode.attributes["ret"], "EndWhile_" + whileCounter);

                        loopExitStack.Push("EndWhile_" + whileCounter);

                        GenNode(whileNode.stmtNode);

                        loopExitStack.Pop();

                        GeneratorCode("JUMP", ("While_" + whileCounter));

                        GeneratorCode(" ").label = "EndWhile_" + whileCounter;
                    }
                    break;
                case SyntaxTree.ForStmtNode forNode:
                    {
                        int forCounter = (int)forNode.attributes["uid"];

                        envStack.Push(forNode.attributes["env"] as SymbolTable);
                        EnvBegin(forNode.attributes["env"] as SymbolTable);

                        //initializer  
                        GenNode(forNode.initializerNode);

                        //start loop  
                        GeneratorCode(" ").label = ("For_" + forCounter);

                        //condition  
                        GenNode(forNode.conditionNode);
                        GeneratorCode("IF_FALSE_JUMP", forNode.conditionNode.attributes["ret"], "EndFor_" + forCounter);

                        loopExitStack.Push("EndFor_" + forCounter);

                        GenNode(forNode.stmtNode);

                        //iterator  
                        GenNode(forNode.iteratorNode);


                        loopExitStack.Pop();

                        GeneratorCode("JUMP", ("For_" + forCounter));

                        GeneratorCode(" ").label = "EndFor_" + forCounter;

                        EnvEnd(forNode.attributes["env"] as SymbolTable);
                        envStack.Pop();
                    }
                    break;

                case SyntaxTree.BreakStmtNode breakNode:
                    {
                        GeneratorCode("JUMP", loopExitStack.Peek());
                    }
                    break;





                    // *******************  表达式节点 *********************************

                case SyntaxTree.IdentityNode idNode:
                    {
                        //标识符表达式的返回变量（本身）    
                        idNode.attributes["ret"] = "[" + idNode.token.attribute + "]";
                    }
                    break;
                case SyntaxTree.ObjectMemberAccessNode objMemberAccess:
                    {
                        GenNode(objMemberAccess.objectNode);

                        //成员表达式的返回变量(X.Y格式)  
                        string objName = TrimName((string)objMemberAccess.objectNode.attributes["ret"]);
                        objMemberAccess.attributes["ret"] = "[" + objName + "." + objMemberAccess.memberNode.token.attribute + "]";
                    }
                    break;
                case SyntaxTree.ThisMemberAccessNode thisMemberAccess:
                    {
                        //表达式的返回变量  
                        thisMemberAccess.attributes["ret"] = "[this." + thisMemberAccess.memberNode.token.attribute + "]";
                    }
                    break;
                case SyntaxTree.LiteralNode literalNode:
                    {
                        //表达式的返回变量  
                        literalNode.attributes["ret"] = literalNode.token.name + ":" + literalNode.token.attribute;
                    }
                    break;
                case SyntaxTree.CastNode castNode:
                    {
                        GenNode(castNode.factorNode);

                        //表达式的返回变量  
                        castNode.attributes["ret"] = NewTemp(castNode.typeNode.ToExpression());

                        GeneratorCode("CAST", castNode.attributes["ret"], castNode.typeNode.ToExpression(), castNode.factorNode.attributes["ret"]);
                    }
                    break;
                case SyntaxTree.BinaryOpNode binaryOp:
                    {
                        GenNode(binaryOp.leftNode);
                        GenNode(binaryOp.rightNode);


                        //表达式的返回变量  
                        binaryOp.attributes["ret"] = NewTemp((string)binaryOp.leftNode.attributes["type"]);

                        GeneratorCode(binaryOp.op, (string)binaryOp.attributes["ret"], (string)binaryOp.leftNode.attributes["ret"], (string)binaryOp.rightNode.attributes["ret"]);
                    }
                    break;
                case SyntaxTree.UnaryOpNode unaryOp:
                    {
                        GenNode(unaryOp.exprNode);

                        //表达式的返回变量  
                        unaryOp.attributes["ret"] = NewTemp((string)unaryOp.exprNode.attributes["type"]);

                        GeneratorCode(unaryOp.op, (string)unaryOp.attributes["ret"], (string)unaryOp.exprNode.attributes["ret"]);
                    }
                    break;
                case SyntaxTree.AssignNode assignNode:
                    {
                        GenNode(assignNode.lvalueNode);
                        GenNode(assignNode.rvalueNode);

                        //复合赋值表达式的返回变量为左值    
                        GeneratorCode(assignNode.op, (string)assignNode.lvalueNode.attributes["ret"], (string)assignNode.rvalueNode.attributes["ret"]);
                    }
                    break;
                case SyntaxTree.CallNode callNode:
                    {
                        //函数全名  
                        string funcFullName;
                        string returnType;
                        if(callNode.isMemberAccessFunction == true)
                        {
                            string className = (string)(callNode.funcNode as SyntaxTree.MemberAccessNode).attributes["class"];
                            string funcName = (string)(callNode.funcNode as SyntaxTree.MemberAccessNode).attributes["member_name"];
                            funcFullName = className + "." + funcName;

                            var funcRec = Query(className, funcName);
                            returnType = funcRec.typeExpression.Split(' ').LastOrDefault();
                        }
                        else
                        {
                            funcFullName = (callNode.funcNode as SyntaxTree.IdentityNode).token.attribute;

                            var funcRec = Query(funcFullName);
                            returnType = funcRec.typeExpression.Split(' ').LastOrDefault();
                        }

                        //表达式的返回变量  
                        callNode.attributes["ret"] = NewTemp(returnType);

                        //参数数量  
                        int argCount;
                        if (callNode.isMemberAccessFunction == true)
                            argCount = callNode.argumantsNode.arguments.Count + 1;
                        else
                            argCount = callNode.argumantsNode.arguments.Count;


                        //显式定义的参数（倒序压栈）    
                        for(int i = callNode.argumantsNode.arguments.Count - 1; i >= 0 ; --i)
                        {
                            //计算参数表达式的值  
                            GenNode(callNode.argumantsNode.arguments[i]);
                            GeneratorCode("PARAM", (string)callNode.argumantsNode.arguments[i].attributes["ret"]);
                        }

                        //隐藏的参数  -  对象指针  
                        if (callNode.isMemberAccessFunction == true)
                        {
                            if (callNode.funcNode is SyntaxTree.ObjectMemberAccessNode)
                            {
                                var objNode = (callNode.funcNode as SyntaxTree.ObjectMemberAccessNode).objectNode;
                                GenNode(objNode);
                                GeneratorCode("PARAM", (string)objNode.attributes["ret"]);
                            }
                            else
                            {
                                GeneratorCode("PARAM", "[this]");
                            }
                        }

                        GeneratorCode("CALL", "[" + funcFullName + "]", argCount);
                        GeneratorCode("=", callNode.attributes["ret"], "RET");
                    }
                    break;
                case SyntaxTree.NewObjectNode newObjNode:
                    {
                        string className = newObjNode.className.token.attribute;

                        //表达式的返回变量  
                        newObjNode.attributes["ret"] = NewTemp(className);

                        GeneratorCode("ALLOC", newObjNode.attributes["ret"]);
                        GeneratorCode("PARAM", newObjNode.attributes["ret"]);
                        GeneratorCode("CALL", "[" + className + ".ctor]", 0);
                    }
                    break;
                case SyntaxTree.IncDecNode incDecNode:
                    {
                        string identifierName = incDecNode.identifierNode.token.attribute;
                        if (incDecNode.isOperatorFront)//++i
                        {
                            GeneratorCode(incDecNode.op, "[" + identifierName + "]");
                            incDecNode.attributes["ret"] = "[" + identifierName + "]";
                        }
                        else//i++
                        {
                            incDecNode.attributes["ret"] = NewTemp(Query(identifierName).typeExpression);
                            GeneratorCode("=", incDecNode.attributes["ret"], "[" + identifierName + "]");
                            GeneratorCode(incDecNode.op, "[" + identifierName + "]");
                        }
                    }
                    break;
                default:
                    throw new Exception("中间代码生成未实现:" + node.GetType().Name);
            }
        }

        public TAC GeneratorCode(string op, object arg1 = null, object arg2 = null, object arg3 = null)
        {
            var newCode = new TAC() { op = op, arg1 = arg1?.ToString(), arg2 = arg2?.ToString(), arg3 = arg3?.ToString() };

            il.codes.Add(newCode);

            return newCode;
        }

        public string NewTemp(string type) 
        { 
            string tempVarName = "tmp" + tmpCounter++;

            envStack.Peek().NewRecord(tempVarName, SymbolTable.RecordCatagory.Var, type);

            return "[" + tempVarName + "]";
        }

        public List<Scope> scopesTemp = new List<Scope>();

        public void EnvBegin(SymbolTable env)
        {
            Scope newScope = new Scope() { env = env , lineFrom = il.codes.Count - 1 + 1 };
            scopesTemp.Add(newScope);
        }
        public void EnvEnd(SymbolTable env)
        {
            var scope = scopesTemp.FirstOrDefault(s => s.env == env);
            scope.lineTo = il.codes.Count - 1;
            scopesTemp.Remove(scope);
            il.scopes.Add(scope);
        }


        public SymbolTable.Record Query(string id)
        {
            for (int i = envStack.Count - 1; i >= 0; --i)
            {
                if (envStack[i].ContainRecordName(id))
                {
                    //Log("在" + envStack[i].name + "中查询：" + id + "成功");
                    var rec = envStack[i].GetRecord(id);
                    return rec;
                }
                //Log("在" + envStack[i].name + "中查询：" + id + "失败");
            }
            throw new Exception("查询不到" + id + "的类型！");
        }
        public SymbolTable.Record Query(string className, string id)
        {
            var classEnv = complierContext.globalSymbolTable.children.FirstOrDefault(c => c.name == className);
            if(classEnv == null) throw new Exception("查询不到" + id + "的类型！");
            return classEnv.GetRecord(id);
        }



        public string TrimName(string input)
        {
            if (input[0] != '[') throw new Exception("无法Trim:" + input);
            return input.Substring(1, input.Length - 2);
        }

        public void PrintCodes()
        {
            Log("中间代码输出：(" + il.codes.Count + "行)");
            Log(new string('-', 50));
            for (int i = 0; i < il.codes.Count; ++i)
            {
                Log($"{i.ToString().PadRight(4)}|status {il.scopeStatusArr[i].ToString().PadRight(3)}|{il.codes[i].ToExpression()}");
            }
            Log(new string('-', 50));


            Log("作用域：");
            foreach(var scope in il.scopes)
            {
                Log("scope:" + scope.env.name + ":  " + scope.lineFrom + " ~ "  + scope.lineTo);
            }
            Log("未封口：");
            foreach (var scope in scopesTemp)
            {
                Log("scope:" + scope.env.name + ":  " + scope.lineFrom + " ~  ???");
            }
        }

        public static void Log(object content)
        {
            if(!enableLog) return;
            Console.WriteLine("ILGen >>>" + content);
        }

    }
}
