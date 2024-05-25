using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using Gizbox;

namespace Gizbox.IL
{
    [DataContract]
    public class TAC
    {
        [DataMember]
        public string label;

        [DataMember]
        public string op;
        [DataMember]
        public string arg1;
        [DataMember]
        public string arg2;
        [DataMember]
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
    [DataContract(IsReference = true)]
    public class Scope
    {
        [DataMember]
        public int lineFrom;
        [DataMember]
        public int lineTo;
        [DataMember]
        public SymbolTable env;
    }

    [Serializable]
    [DataContract(IsReference = true)]
    public class ILUnit
    {
        //名称    
        [DataMember]
        public string name;

        //依赖库名称  
        [DataMember]
        public List<string> dependencies = new List<string>();

        //代码  
        [DataMember]
        public List<TAC> codes = new List<TAC>();

        //作用域状态数组  
        [DataMember]
        public int[] scopeStatusArr;

        //作用域列表  
        [DataMember]
        public List<Scope> scopes = new List<Scope>();

        //全局作用域  
        [DataMember]
        public Scope globalScope;

        //虚函数表  
        [DataMember]
        public Dictionary<string, VTable> vtables = new Dictionary<string, VTable>();

        //标号 -> 行数 查询表  
        [DataMember]
        public Dictionary<string, int> label2Line = new Dictionary<string, int>();

        //行数 -> 符号表链 查询表
        [DataMember]
        public Dictionary<int, Gizbox.GStack<SymbolTable>> stackDic;

        //静态数据区 - 常量  
        [DataMember]
        public List<object> constData = new List<object>();


        //(不序列化) 临时载入的依赖      
        public List<ILUnit> dependencyLibs = new List<ILUnit>();
        //(不序列化) 
        public List<ILUnit> libsDenpendThis = new List<ILUnit>();




        //构造函数  
        public ILUnit()
        {
            var globalSymbolTable = new SymbolTable("global", SymbolTable.TableCatagory.GlobalScope);
            this.globalScope = new Scope() { env = globalSymbolTable };
        }



        //添加依赖  
        public void AddDependencyLib(ILUnit dep)
        {
            if (dep == null) throw new GizboxException(ExceptionType.LibraryDependencyCannotBeEmpty);

            if (this.dependencyLibs == null) this.dependencyLibs = new List<ILUnit>();
            this.dependencyLibs.Add(dep);

            if (dep.libsDenpendThis == null) dep.libsDenpendThis = new List<ILUnit>();
            dep.libsDenpendThis.Add(this);
        }

        //完成构建  
        public void Complete()
        {
            globalScope.lineFrom = 0;
            globalScope.lineTo = codes.Count - 1;

            FillLabelDic();
            BuildMarkArray();
            CacheEnvStack();
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
                    label2Line[codes[i].label] = i;
                }
            }
        }

        //缓存符号表栈  
        private void CacheEnvStack()
        {
            this.stackDic = new Dictionary<int, GStack<SymbolTable>>();

            List<SymbolTable> tempList = new List<SymbolTable>();

            int prevStatus = -1;
            for (int i = 0; i < this.codes.Count; ++i)
            {
                if(scopeStatusArr[i] != prevStatus)
                {
                    int newstate = scopeStatusArr[i];
                    if (stackDic.ContainsKey(newstate) == false)
                    {
                        EnvHits(i, tempList);
                        tempList.Sort((e1, e2) => e1.depth - e2.depth);
                        var newEnvStack = new GStack<SymbolTable>();
                        foreach (var env in tempList)
                        {
                            newEnvStack.Push(env);
                        }
                        stackDic[newstate] = newEnvStack;
                    }
                    prevStatus = newstate;
                }
            }
        }

        //所在符号表链  
        private void EnvHits(int currentLine, List<SymbolTable> envs)
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

        //查询顶层符号  
        public SymbolTable.Record QueryTopSymbol(string name, bool ignoreMangle = false)
        {
            //本单元查找  
            if(ignoreMangle == false)
            {
                if (globalScope.env.ContainRecordName(name))
                {
                    return globalScope.env.GetRecord(name);
                }
            }
            else
            {
                if (globalScope.env.ContainRecordRawName(name))
                {
                    return globalScope.env.GetRecordByRawname(name);
                }
            }

            //依赖中查找  
            if(this.dependencyLibs != null)
            {
                foreach (var dep in dependencyLibs)
                {
                    var result = dep.QueryTopSymbol(name, ignoreMangle);
                    if (result != null) return result;
                }
            }

            return null;
        }

        //查询虚函数表  
        public VTable QueryVTable(string name)
        {
            //本单元查找  
            if (vtables.ContainsKey(name))
            {
                return vtables[name];
            }

            //依赖中查找  
            if (this.dependencyLibs != null)
            {
                foreach (var dep in dependencyLibs)
                {
                    var result = dep.QueryVTable(name);
                    if (result != null) return result;
                }
            }

            return null;
        }


        //查询所有全局作用域   
        public List<SymbolTable> GetAllGlobalEnvs()
        {
            List<SymbolTable> envs = new List<SymbolTable>();
            AddGlobalEnvsToList(this, envs);
            return envs;
        }
        private void AddGlobalEnvsToList(ILUnit unit, List<SymbolTable> list)
        {
            if (list.Contains(unit.globalScope.env)) return;

            list.Add(unit.globalScope.env);
            foreach (var dep in this.dependencyLibs)
            {
                AddGlobalEnvsToList(dep, list);
            }
        }


        //打印  
        public void Print()
        {
            Debug.LogLine("中间代码输出：(" + this.codes.Count + "行)");
            Debug.LogLine(new string('-', 50));
            for (int i = 0; i < this.codes.Count; ++i)
            {
                Debug.LogLine($"{i.ToString().PadRight(4)}|status {this.scopeStatusArr[i].ToString().PadRight(3)}|{this.codes[i].ToExpression()}");
            }
            Debug.LogLine(new string('-', 50));


            Debug.LogLine("作用域：");
            foreach (var scope in this.scopes)
            {
                Debug.LogLine("scope:" + scope.env.name + ":  " + scope.lineFrom + " ~ " + scope.lineTo);
            }
        }
    }


    /// <summary>
    /// 中间代码生成器  
    /// </summary>
    public class ILGenerator
    {
        //public  
        public SyntaxTree ast;

        public ILUnit ilUnit;


        //temp info  
        private Gizbox.GStack<SymbolTable> envStack = new GStack<SymbolTable>();

        //status  
        private int tmpCounter = 0;//临时变量自增    
        private GStack<string> loopExitStack = new GStack<string>();


        public ILGenerator(SyntaxTree ast, ILUnit ilUnit)
        {
            this.ilUnit = ilUnit;
            this.ast = ast;
        }
        
        public void Generate()
        {
            this.tmpCounter = 0;

            this.envStack.Push(ilUnit.globalScope.env);


            //从根节点生成   
            GenNode(ast.rootNode);

            //生成一个空指令  
            GenerateCode("");

            this.ilUnit.Complete();


            if(Compiler.enableLogILGenerator) ilUnit.globalScope.env.Print();
            Compiler.Pause("符号表建立完毕");

            this.PrintCodes();
            Compiler.Pause("中间代码生成完毕");

        }

        public void GenNode(SyntaxTree.Node node)
        {
            if(node.replacement != null)
            {
                GenNode(node.replacement);
                return;
            }


            //节点代码生成  
            switch (node)
            {
                case SyntaxTree.ProgramNode programNode:
                    {
                        GenNode(programNode.statementsNode);
                    }
                    break;
                case SyntaxTree.NamespaceNode namespaceNode:
                    {
                        GenNode(namespaceNode.stmtsNode);
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
                        string className = classDeclNode.classNameNode.FullName;
                        GenerateCode("JUMP", "exit:" + className);
                        GenerateCode(" ").label = className;
                        //GeneratorCode("CLASS_BEGIN", classDeclNode.classNameNode.token.attribute);
                        envStack.Push(classDeclNode.attributes["env"] as SymbolTable);
                        EnvBegin(classDeclNode.attributes["env"] as SymbolTable);

                        //构造函数（默认）  
                        {
                            //构造函数全名    
                            string funcFullName = classDeclNode.classNameNode.FullName + ".ctor";
                            //string funcFullName = "ctor";

                            //跳过声明  
                            GenerateCode("JUMP", "exit:" + funcFullName);

                            //函数开始    
                            GenerateCode(" ").label = "entry:" + funcFullName;
                            EnvBegin(envStack.Peek().GetTableInChildren(classDeclNode.classNameNode.FullName + ".ctor"));
                            GenerateCode("METHOD_BEGIN", funcFullName);

                            //基类构造函数调用  
                            if (classDeclNode.baseClassNameNode != null)
                            {
                                var baseClassName = classDeclNode.baseClassNameNode.FullName;
                                var baseRec = Query(baseClassName);
                                var baseEnv = baseRec.envPtr;

                                GenerateCode("PARAM", "[this]");
                                GenerateCode("CALL", "[" + baseClassName + ".ctor]", "LITINT:1");
                            }
                            
                            //成员变量初始化
                            foreach (var memberDecl in classDeclNode.memberDelareNodes)
                            {
                                if (memberDecl is SyntaxTree.VarDeclareNode)
                                {
                                    var fieldDecl = memberDecl as SyntaxTree.VarDeclareNode;

                                    GenNode(fieldDecl.initializerNode);
                                    GenerateCode("=", "[this." + fieldDecl.identifierNode.FullName + "]", fieldDecl.initializerNode.attributes["ret"]);
                                }
                            }

                            //其他语句(Not Implement)  
                            //...  

                            GenerateCode("RETURN");

                            GenerateCode("METHOD_END");
                            EnvEnd(envStack.Peek().GetTableInChildren(classDeclNode.classNameNode.FullName + ".ctor"));
                            GenerateCode(" ").label = "exit:" + funcFullName;
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
                        GenerateCode(" ").label = "exit:" + className;
                    }
                    break;
                //函数声明
                case SyntaxTree.FuncDeclareNode funcDeclNode:
                    {
                        //是否是实例成员函数  
                        bool isMethod = envStack.Peek().tableCatagory == SymbolTable.TableCatagory.ClassScope;
  
                        //函数全名(修饰)    
                        string funcFinalName;
                        if(isMethod)
                            funcFinalName = envStack.Peek().name + "." + (string)funcDeclNode.attributes["mangled_name"];
                        else
                            funcFinalName = (string)funcDeclNode.attributes["mangled_name"];



                        //跳过声明  
                        GenerateCode("JUMP", "exit:" + funcFinalName);


                        //函数开始    
                        GenerateCode(" ").label = "entry:" + funcFinalName;

                        envStack.Push(funcDeclNode.attributes["env"] as SymbolTable);
                        EnvBegin(funcDeclNode.attributes["env"] as SymbolTable);

                        if (isMethod)
                            GenerateCode("METHOD_BEGIN", funcFinalName);
                        else
                            GenerateCode("FUNC_BEGIN", funcFinalName);


                        //语句  
                        foreach (var stmt in funcDeclNode.statementsNode.statements)
                        {
                            GenNode(stmt);
                        }


                        if(funcDeclNode.statementsNode.statements.Any(s => s is SyntaxTree.ReturnStmtNode) == false)
                            GenerateCode("RETURN");


                        if (isMethod)
                            GenerateCode("METHOD_END", funcFinalName);
                        else
                            GenerateCode("FUNC_END", funcFinalName);


                        EnvEnd(funcDeclNode.attributes["env"] as SymbolTable);
                        envStack.Pop();

                        GenerateCode(" ").label = "exit:" + funcFinalName;
                    }
                    break;
                case SyntaxTree.ExternFuncDeclareNode externFuncDeclNode:
                    {
                        //函数全名    
                        string funcFullName = (string)externFuncDeclNode.attributes["mangled_name"]; 
                        

                        //跳过声明  
                        GenerateCode("JUMP", "exit:" + funcFullName);

                        //函数开始    
                        GenerateCode(" ").label = "entry:" + funcFullName;

                        envStack.Push(externFuncDeclNode.attributes["env"] as SymbolTable);
                        EnvBegin(externFuncDeclNode.attributes["env"] as SymbolTable);

                        GenerateCode("FUNC_BEGIN", funcFullName);


                        GenerateCode("EXTERN_IMPL", externFuncDeclNode.identifierNode.FullName);


                        GenerateCode("FUNC_END", funcFullName);


                        EnvEnd(externFuncDeclNode.attributes["env"] as SymbolTable);
                        envStack.Pop();

                        GenerateCode(" ").label = "exit:" + funcFullName;
                    }
                    break;
                //变量声明
                case SyntaxTree.VarDeclareNode varDeclNode:
                    {
                        GenNode(varDeclNode.identifierNode);
                        GenNode(varDeclNode.initializerNode);

                        if(varDeclNode.initializerNode.attributes.ContainsKey("ret") == false)
                        {
                            throw new SemanticException(ExceptionType.SubExpressionNoReturnVariable, varDeclNode, varDeclNode.identifierNode.FullName);
                        }

                        GenerateCode("=", (string)varDeclNode.identifierNode.attributes["ret"], (string)varDeclNode.initializerNode.attributes["ret"]);
                    }
                    break;

                case SyntaxTree.ReturnStmtNode returnNode:
                    {
                        if(returnNode.returnExprNode != null)
                        {
                            GenNode(returnNode.returnExprNode);
                            GenerateCode("RETURN", returnNode.returnExprNode.attributes["ret"]);
                        }
                        else
                        {
                            GenerateCode("RETURN");
                        }
                    }
                    break;
                case SyntaxTree.DeleteStmtNode deleteNode:
                    {
                        GenNode(deleteNode.objToDelete);
                        GenerateCode("DEL", deleteNode.objToDelete.attributes["ret"]);
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

                        GenerateCode(" ").label = ("If_" + ifCounter);

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

                            GenerateCode(" ").label = "IfCondition_" + ifCounter + "_" + i;

                            GenNode(clause.conditionNode);
                            GenerateCode("IF_FALSE_JUMP", clause.conditionNode.attributes["ret"], falseGotoLabel);

                            GenerateCode(" ").label = ("IfStmt_" + ifCounter + "_" + i);

                            GenNode(clause.thenNode);

                            GenerateCode("JUMP", ("EndIf_" + ifCounter));
                        }

                        if(elseClause != null)
                        {
                            GenerateCode(" ").label = ("ElseStmt_" + ifCounter);
                            GenNode(elseClause.stmt);
                        }


                        GenerateCode(" ").label = ("EndIf_" + ifCounter);
                    }
                    break;
                case SyntaxTree.WhileStmtNode whileNode:
                    {
                        int whileCounter = (int)whileNode.attributes["uid"];

                        GenerateCode(" ").label = ("While_" + whileCounter);

                        GenNode(whileNode.conditionNode);

                        GenerateCode("IF_FALSE_JUMP", whileNode.conditionNode.attributes["ret"], "EndWhile_" + whileCounter);

                        loopExitStack.Push("EndWhile_" + whileCounter);

                        GenNode(whileNode.stmtNode);

                        loopExitStack.Pop();

                        GenerateCode("JUMP", ("While_" + whileCounter));

                        GenerateCode(" ").label = "EndWhile_" + whileCounter;
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
                        GenerateCode(" ").label = ("For_" + forCounter);

                        //condition  
                        GenNode(forNode.conditionNode);
                        GenerateCode("IF_FALSE_JUMP", forNode.conditionNode.attributes["ret"], "EndFor_" + forCounter);

                        loopExitStack.Push("EndFor_" + forCounter);

                        GenNode(forNode.stmtNode);

                        //iterator  
                        GenNode(forNode.iteratorNode);


                        loopExitStack.Pop();

                        GenerateCode("JUMP", ("For_" + forCounter));

                        GenerateCode(" ").label = "EndFor_" + forCounter;

                        EnvEnd(forNode.attributes["env"] as SymbolTable);
                        envStack.Pop();
                    }
                    break;

                case SyntaxTree.BreakStmtNode breakNode:
                    {
                        GenerateCode("JUMP", loopExitStack.Peek());
                    }
                    break;





                    // *******************  表达式节点 *********************************

                case SyntaxTree.IdentityNode idNode:
                    {
                        //标识符表达式的返回变量（本身）    
                        idNode.attributes["ret"] = "[" + idNode.FullName + "]";
                    }
                    break;
                case SyntaxTree.ThisNode thisnode:
                    {
                        thisnode.attributes["ret"] = "[this]";
                    }
                    break;
                case SyntaxTree.LiteralNode literalNode:
                    {
                        //表达式的返回变量  
                        string valStr;

                        if(literalNode.token.name != "null")
                        {
                            //字符串字面量 -> 存储为字符串常量    
                            if (literalNode.token.name == "LITSTRING")
                            {
                                string lex = literalNode.token.attribute;
                                string conststr = lex.Substring(1, lex.Length - 2);
                                this.ilUnit.constData.Add(conststr);
                                int ptr = this.ilUnit.constData.Count - 1;

                                if (Compiler.enableLogILGenerator) 
                                    Log("新的字符串常量：" + lex + " 指针：" + ptr);

                                valStr = "CONSTSTRING:" + ptr;
                            }
                            //其他类型字面量    
                            else
                            {
                                valStr = literalNode.token.name + ":" + literalNode.token.attribute;
                            }
                        }
                        else
                        {
                            valStr = "LITNULL:";
                        }

                        literalNode.attributes["ret"] = valStr;
                    }
                    break;
                case SyntaxTree.ObjectMemberAccessNode objMemberAccess:
                    {
                        GenNode(objMemberAccess.objectNode);

                        //成员表达式的返回变量(X.Y格式)  
                        string obj = TrimName((string)objMemberAccess.objectNode.attributes["ret"]);
                        objMemberAccess.attributes["ret"] = "[" + obj + "." + objMemberAccess.memberNode.FullName + "]";
                    }
                    break;
                case SyntaxTree.CastNode castNode:
                    {
                        GenNode(castNode.factorNode);

                        //表达式的返回变量  
                        castNode.attributes["ret"] = NewTemp(castNode.typeNode.ToExpression());

                        GenerateCode("CAST", castNode.attributes["ret"], castNode.typeNode.ToExpression(), castNode.factorNode.attributes["ret"]);
                    }
                    break;
                case SyntaxTree.BinaryOpNode binaryOp:
                    {
                        GenNode(binaryOp.leftNode);
                        GenNode(binaryOp.rightNode);

                        if (binaryOp.leftNode.attributes.ContainsKey("type") == false)
                            throw new SemanticException(ExceptionType.TypeNotSet, binaryOp.leftNode, "");

                        //if (binaryOp.rightNode.attributes.ContainsKey("type") == false)
                        //    throw new SemanticException(binaryOp.rightNode, "type未设置!");

                        //表达式的返回变量  
                        binaryOp.attributes["ret"] = NewTemp((string)binaryOp.leftNode.attributes["type"]);

                        GenerateCode(binaryOp.op, (string)binaryOp.attributes["ret"], (string)binaryOp.leftNode.attributes["ret"], (string)binaryOp.rightNode.attributes["ret"]);
                    }
                    break;
                case SyntaxTree.UnaryOpNode unaryOp:
                    {
                        GenNode(unaryOp.exprNode);

                        //表达式的返回变量  
                        unaryOp.attributes["ret"] = NewTemp((string)unaryOp.exprNode.attributes["type"]);

                        GenerateCode(unaryOp.op, (string)unaryOp.attributes["ret"], (string)unaryOp.exprNode.attributes["ret"]);
                    }
                    break;
                case SyntaxTree.AssignNode assignNode:
                    {
                        GenNode(assignNode.lvalueNode);
                        GenNode(assignNode.rvalueNode);


                        //复合赋值表达式的返回变量为左值    
                        GenerateCode(assignNode.op, (string)assignNode.lvalueNode.attributes["ret"], (string)assignNode.rvalueNode.attributes["ret"]);
                    }
                    break;
                case SyntaxTree.CallNode callNode:
                    {
                        if (callNode.attributes.ContainsKey("mangled_name") == false)
                            throw new SemanticException(ExceptionType.FunctionObfuscationNameNotSet, callNode, "");
                        //函数全名  
                        string mangledName = (string)callNode.attributes["mangled_name"];
                        //函数返回类型    
                        string returnType = (string)callNode.attributes["type"];

                        //表达式的返回变量  
                        callNode.attributes["ret"] = NewTemp(returnType);

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
                        for(int i = callNode.argumantsNode.arguments.Count - 1; i >= 0 ; --i)
                        {
                            //计算参数表达式的值  
                            GenNode(callNode.argumantsNode.arguments[i]);
                        }

                        //this实参计算（成员方法）    
                        if (callNode.isMemberAccessFunction == true)
                        {
                            if (callNode.funcNode is SyntaxTree.ObjectMemberAccessNode)
                            {
                                var objNode = (callNode.funcNode as SyntaxTree.ObjectMemberAccessNode).objectNode;
                                GenNode(objNode);
                            }
                        }

                        //实参倒序压栈  
                        for (int i = callNode.argumantsNode.arguments.Count - 1; i >= 0; --i)
                        {
                            GenerateCode("PARAM", (string)callNode.argumantsNode.arguments[i].attributes["ret"]);
                        }
                        //this实参压栈（成员方法）    
                        if (callNode.isMemberAccessFunction == true)
                        {
                            if (callNode.funcNode is SyntaxTree.ObjectMemberAccessNode)
                            {
                                var objNode = (callNode.funcNode as SyntaxTree.ObjectMemberAccessNode).objectNode;
                                GenerateCode("PARAM", (string)objNode.attributes["ret"]);
                            }
                            else
                            {
                                GenerateCode("PARAM", "[this]");
                            }
                        }



                        if (callNode.isMemberAccessFunction == true)
                        {
                            GenerateCode("MCALL", "[" + mangledName + "]", "LITINT:" + argCount);
                            GenerateCode("=", callNode.attributes["ret"], "RET");
                        }
                        else
                        {
                            GenerateCode("CALL", "[" + mangledName + "]", "LITINT:" + argCount);
                            GenerateCode("=", callNode.attributes["ret"], "RET");
                        }
                    }
                    break;
                case SyntaxTree.ElementAccessNode eleAccessNode:
                    {
                        GenNode(eleAccessNode.indexNode);

                        string rightval;
                        if(eleAccessNode.isMemberAccessContainer == true)
                        {
                            GenNode(eleAccessNode.containerNode);
                            rightval = "[" + TrimName((string)eleAccessNode.containerNode.attributes["ret"]) + "[" + TrimName((string)eleAccessNode.indexNode.attributes["ret"]) + "]" + "]";
                        }
                        else
                        {
                            GenNode(eleAccessNode.containerNode);
                            rightval = "[" + TrimName((string)eleAccessNode.containerNode.attributes["ret"]) + "[" + TrimName((string)eleAccessNode.indexNode.attributes["ret"]) + "]" + "]";
                        }

                        eleAccessNode.attributes["ret"] = rightval;
                    }
                    break;
                case SyntaxTree.NewObjectNode newObjNode:
                    {
                        string className = newObjNode.className.FullName;

                        //表达式的返回变量  
                        newObjNode.attributes["ret"] = NewTemp(className);

                        GenerateCode("ALLOC", newObjNode.attributes["ret"], className);
                        GenerateCode("PARAM", newObjNode.attributes["ret"]);
                        GenerateCode("CALL", "[" + className + ".ctor]", "LITINT:" + 1);
                    }
                    break;
                case SyntaxTree.NewArrayNode newArrNode:
                    {
                        //长度计算  
                        GenNode(newArrNode.lengthNode);

                        //表达式的返回变量  
                        newArrNode.attributes["ret"] = NewTemp(newArrNode.typeNode.ToExpression() + "[]");

                        GenerateCode("ALLOC_ARRAY", newArrNode.attributes["ret"], newArrNode.lengthNode.attributes["ret"]);
                    }
                    break;
                case SyntaxTree.IncDecNode incDecNode:
                    {
                        string identifierName = incDecNode.identifierNode.FullName;
                        if (incDecNode.isOperatorFront)//++i
                        {
                            GenerateCode(incDecNode.op, "[" + identifierName + "]");
                            incDecNode.attributes["ret"] = "[" + identifierName + "]";
                        }
                        else//i++
                        {
                            incDecNode.attributes["ret"] = NewTemp(Query(identifierName).typeExpression);
                            GenerateCode("=", incDecNode.attributes["ret"], "[" + identifierName + "]");
                            GenerateCode(incDecNode.op, "[" + identifierName + "]");
                        }
                    }
                    break;
                default:
                    throw new SemanticException(ExceptionType.Unknown, node, "IR generation not implemtented:" + node.GetType().Name);
            }
        }

        public TAC GenerateCode(string op, object arg1 = null, object arg2 = null, object arg3 = null)
        {
            var newCode = new TAC() { op = op, arg1 = arg1?.ToString(), arg2 = arg2?.ToString(), arg3 = arg3?.ToString() };

            ilUnit.codes.Add(newCode);

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
            Scope newScope = new Scope() { env = env , lineFrom = ilUnit.codes.Count - 1 + 1 };
            scopesTemp.Add(newScope);
        }
        public void EnvEnd(SymbolTable env)
        {
            var scope = scopesTemp.FirstOrDefault(s => s.env == env);
            scope.lineTo = ilUnit.codes.Count - 1;
            scopesTemp.Remove(scope);
            ilUnit.scopes.Add(scope);
        }


        public SymbolTable.Record Query(string id)
        {
            //本编译单元查找  
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

            //导入库查找  
            foreach(var lib in ilUnit.dependencyLibs)
            {
                var rec = lib.globalScope.env.GetRecord(id);
                if(rec != null)
                {
                    return rec;
                }
            }

            throw new GizboxException(ExceptionType.TypeNotFound, id);
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
            if(!Compiler.enableLogILGenerator) return;
            Debug.LogLine("ILGen >>>" + content);
        }

    }
}
