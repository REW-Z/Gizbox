using System;
using System.Collections.Generic;
using System.Text;
using FanLang;

namespace FanLang.IL
{
    public class TAC
    {
        public string label;

        public string op;
        public string arg1;
        public string arg2;
        public string arg3;

        public string ToExpression()
        {
            string str = "";

            if(string.IsNullOrEmpty(label))
            {
                str += new string(' ', 20);
            }
            else
            {
                str += (label + ":").PadRight(20);
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

    /// <summary>
    /// 中间代码生成器  
    /// </summary>
    public class ILGenerator
    {
        //public  
        public Compiler complierContext; 

        public SyntaxTree ast;

        public Dictionary<int, string> labelDic = new Dictionary<int, string>();

        public List<TAC> codes = new List<TAC>();

        public int codeEntry;


        //temp info  
        private FanLang.Stack<SymbolTable> envStack = new Stack<SymbolTable>();




        public ILGenerator(SyntaxTree ast, Compiler compilerContext)
        {
            this.complierContext = compilerContext;
            this.ast = ast;
        }
        public void Generate()
        {
            this.envStack.Push(complierContext.globalSymbolTable);

            GenNode(ast.rootNode);
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
                        GeneratorCode("env", (blockNode.attributes["env"] as SymbolTable).name);

                        foreach (var stmt in blockNode.statements)
                        {
                            GenNode(stmt);
                        }

                        GeneratorCode("envpop", (blockNode.attributes["env"] as SymbolTable).name);
                    }
                    break;

                //类声明  
                case SyntaxTree.ClassDeclareNode classDeclNode:
                    {
                        string className = classDeclNode.classNameNode.token.attribute;
                        GeneratorCode("JUMP", "End " + className);
                        GeneratorCode(" ").label = className;
                        GeneratorCode("env", (classDeclNode.attributes["env"] as SymbolTable).name);


                        //...


                        GeneratorCode("envpop");
                        GeneratorCode(" ").label = className + "End";
                    }
                    break;
                //函数声明
                case SyntaxTree.FuncDeclareNode funcDeclNode:
                    {
                        string funcFullName;
                        if (envStack.Peek().tableCatagory == SymbolTable.TableCatagory.ClassScope)
                            funcFullName = envStack.Peek().name + "." + funcDeclNode.identifierNode.token.attribute;
                        else
                            funcFullName = funcDeclNode.identifierNode.token.attribute;


                        GeneratorCode("JUMP", "End " + funcFullName);
                        GeneratorCode(" ").label = funcFullName;
                        GeneratorCode("env", (funcDeclNode.attributes["env"] as SymbolTable).name);


                        //...


                        GeneratorCode("envpop");
                        GeneratorCode(" ").label = funcFullName + "End";
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



                case SyntaxTree.SingleExprStmtNode singleExprNode:
                    {
                        GenNode(singleExprNode.exprNode);
                    }
                    break;
                case SyntaxTree.IdentityNode idNode:
                    {
                        idNode.attributes["ret"] = idNode.token.attribute;
                    }
                    break;
                case SyntaxTree.LiteralNode literalNode:
                    {
                        literalNode.attributes["ret"] = literalNode.token.attribute;
                    }
                    break;
                case SyntaxTree.BinaryOpNode binaryOp:
                    {
                        GenNode(binaryOp.leftNode);
                        GenNode(binaryOp.rightNode);

                        binaryOp.attributes["ret"] = NewTemp();

                        GeneratorCode(binaryOp.op, (string)binaryOp.attributes["ret"], (string)binaryOp.leftNode.attributes["ret"], (string)binaryOp.leftNode.attributes["ret"]);

                    }
                    break;
                case SyntaxTree.AssignNode assignNode:
                    {
                        GenNode(assignNode.lvalueNode);
                        GenNode(assignNode.rvalueNode);
                        foreach(var key in assignNode.lvalueNode.attributes)
                        {
                            Console.WriteLine("Key:" + key);
                        }
                        foreach (var key in assignNode.rvalueNode.attributes)
                        {
                            Console.WriteLine("Key:" + key);
                        }
                        GeneratorCode("=", (string)assignNode.lvalueNode.attributes["ret"], (string)assignNode.rvalueNode.attributes["ret"]);
                    }
                    break;
                case SyntaxTree.CallNode callNode:
                    {
                        string funcFullName;
                        if(callNode.isMemberAccessFunction == false)
                        {
                            funcFullName = (callNode.funcNode as SyntaxTree.IdentityNode).token.attribute;
                        }
                        else
                        {
                            string className = (string)(callNode.funcNode as SyntaxTree.MemberAccessNode).attributes["class"];
                            string funcName = (string)(callNode.funcNode as SyntaxTree.MemberAccessNode).attributes["member_name"];
                            funcFullName = className + "," + funcName;
                        }

                        for(int i = 0; i < callNode.argumantsNode.arguments.Count; ++i)
                        {
                            //计算参数表达式的值  
                            GenNode(callNode.argumantsNode.arguments[i]);

                            GeneratorCode("param", (string)callNode.argumantsNode.arguments[i].attributes["ret"]);
                        }

                        callNode.attributes["ret"] = NewTemp();

                        GeneratorCode("call", callNode.attributes["ret"], funcFullName, callNode.argumantsNode.arguments.Count);
                    }
                    break;
                case SyntaxTree.NewObjectNode newObjNode:
                    {
                        newObjNode.attributes["ret"] = NewTemp();
                    }
                    break;
                default:
                    throw new Exception("中间代码生成未实现:" + node.GetType().Name);
            }
        }

        public TAC GeneratorCode(string op, object arg1 = null, object arg2 = null, object arg3 = null)
        {
            var newCode = new TAC() { op = op, arg1 = arg1?.ToString(), arg2 = arg2?.ToString(), arg3 = arg3?.ToString() };

            codes.Add(newCode);

            return newCode;
        }



        private int counter = 0;
        public string NewTemp()
        {
            //Add To Symbol  
            return "tmp" + counter++;
        }

        public void PrintCodes()
        {
            Console.WriteLine("中间代码输出：");
            Console.WriteLine(new string('-', 50));
            for (int i = 0; i < codes.Count; ++i)
            {
                Console.WriteLine($"{i.ToString().PadRight(4)}|{codes[i].ToExpression()}");
            }
            Console.WriteLine(new string('-', 50));
        }
    }
}
