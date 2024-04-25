using System;
using System.Collections.Generic;
using System.Text;
using FanLang;

namespace FanLang.IL
{
    public struct TAC
    {
        public string op;
        public string arg1;
        public string arg2;
        public string arg3;

        public string ToExpression()
        {
            string str = op;
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
        public Compiler context; 

        public SyntaxTree ast;

        public List<TAC> codes;

        public ILGenerator(SyntaxTree ast, Compiler compilerContext)
        {
            this.context = compilerContext;
            this.ast = ast;
        }
        public void Generate()
        {
            this.codes = new List<TAC>();
            ExecuteNode(ast.rootNode);
        }


        public void ExecuteNode(SyntaxTree.Node node)
        {
            switch (node)
            {
                case SyntaxTree.ProgramNode programNode:
                    {
                        ExecuteNode(programNode.statementsNode);
                    }
                    break;
                case SyntaxTree.StatementsNode stmtsNode:
                    {
                        foreach(var stmt in stmtsNode.statements)
                        {
                            ExecuteNode(stmt);
                        }
                    }
                    break;
                case SyntaxTree.SingleExprStmtNode singleExprNode:
                    {
                        ExecuteNode(singleExprNode.exprNode);
                    }
                    break;

                case SyntaxTree.IdentityNode id:
                    {
                        id.attributes["ret"] = id.token.attribute;
                    }
                    break;
                case SyntaxTree.LiteralNode literalNode:
                    {
                        literalNode.attributes["ret"] = literalNode.token.attribute;
                    }
                    break;
                case SyntaxTree.BinaryOpNode binaryOp:
                    {
                        ExecuteNode(binaryOp.leftNode);
                        ExecuteNode(binaryOp.rightNode);

                        binaryOp.attributes["ret"] = NewTemp();

                        GeneratorCode(binaryOp.op, (string)binaryOp.attributes["ret"], (string)binaryOp.leftNode.attributes["ret"], (string)binaryOp.leftNode.attributes["ret"]);

                    }
                    break;
                case SyntaxTree.VarDeclareNode varDeclNode:
                    {
                        ExecuteNode(varDeclNode.identifierNode);
                        ExecuteNode(varDeclNode.initializerNode);

                        GeneratorCode("=", (string)varDeclNode.identifierNode.attributes["ret"], (string)varDeclNode.initializerNode.attributes["ret"]);
                    }
                    break;
                case SyntaxTree.AssignNode assignNode:
                    {
                        ExecuteNode(assignNode.lvalueNode);
                        ExecuteNode(assignNode.rvalueNode);
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
                default:
                    throw new Exception("中间代码生成未实现:" + node.GetType().Name);
            }
        }

        public int GeneratorCode(string op, string arg1 = "", string arg2 = "", string arg3 = "")
        {
            int idx = codes.Count - 1;
            codes.Add(new TAC() { op = op, arg1 = arg1, arg2 = arg2, arg3 = arg3 });
            return idx;
        }

        private int counter = 0;
        public string NewTemp()
        {
            //Add To Symbol  
            return "tmp" + counter++;
        }

        public void PrintCodes()
        {
            for (int i = 0; i < codes.Count; ++i)
            {
                Console.WriteLine(i.ToString() + "\t|" + codes[i].ToExpression());
            }
        }
    }
}
