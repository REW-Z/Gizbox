using System;
using System.Collections.Generic;
using System.Text;

namespace Gizbox
{

    /// <summary>
    /// 简单语法分析树  
    /// </summary>
    public class SimpleParseTree
    {
        public class Node
        {
            public bool isLeaf = false;
            public int depth = 0;
            public string name = "";

            public Node parent = null;
            public List<Node> children = new List<Node>();
        }

        public List<Node> allnodes;
        public Node root;


        public SimpleParseTree()
        {
            allnodes = new List<Node>();
            root = new Node() { isLeaf = false };
            allnodes.Add(root);
        }


        public void AppendNode(Node parent, Node newnode)
        {
            if (allnodes.Contains(parent) == false) return;

            parent.children.Add(newnode);
            newnode.parent = parent;

            newnode.depth = parent.depth + 1;

            allnodes.Add(newnode);
        }


        public string Serialize()
        {
            System.Text.StringBuilder strb = new System.Text.StringBuilder();

            Traversal((node) => {
                string brace = "";
                for (int i = 0; i < node.depth; ++i)
                {
                    brace += "    ";
                }
                strb.AppendLine(brace + (node.isLeaf ? ("<" + node.name + ">") : node.name));
            });

            return strb.ToString();
        }

        public void Traversal(Action<Node> operation)
        {
            TraversalNode(root, operation);
        }

        private void TraversalNode(Node node, Action<Node> operation)
        {
            operation(node);

            foreach (var child in node.children)
            {
                TraversalNode(child, operation);
            }
        }
    }



    /// <summary>
    /// 简单的预测分析器    
    /// </summary>
    public class SimpleParser
    {
        /// ****** 注意事项 ******  
        ///
        ///非终结符的产生式的第一项是非终结符自己，会导致左递归。  
        ///预测分析法要求FIRST(α)和FIRST(β)不相交。   
        ///
        ///

        /// ****** 文法定义 ******  
        ///stmt  ->  <var> <id> <=> expr <;>        //定义
        ///          <id> <=> expr <;>              //赋值
        ///
        ///
        ///expr  ->  term rest  
        ///          //expr + ...  （左递归）（不可行）
        ///
        ///term  ->  <id>
        ///          <num>
        ///
        ///rest  ->  <+> term
        ///          <-> term
        ///          ε
        /// *************************  


        //构造函数  
        public SimpleParser()
        {
            this.lookahead = 0;

            this.parseTree = new SimpleParseTree();
            this.currentNode = parseTree.root;
        }


        //输入和输出  
        public List<Token> input;
        public SimpleParseTree parseTree = null;


        //状态  
        private int lookahead = 0;
        private Token lookaheadToken => input[lookahead];
        private SimpleParseTree.Node currentNode = null;


        /// *********************** 简单的预测分析法***********************  

        //语法分析  
        public void Parse(List<Token> input)
        {
            this.input = input;


            int stmtCounter = 0;
            while (lookahead <= (input.Count - 1))
            {
                if (++stmtCounter > 99) break;

                stmt();
            }
        }


        //非终结符对应过程    
        public void stmt()
        {
            var stmtNode = new SimpleParseTree.Node() { isLeaf = false, name = "stmt" };
            parseTree.AppendNode(currentNode, stmtNode);
            this.currentNode = stmtNode;


            switch (lookaheadToken.name)
            {
                case "var":
                    match("var"); match("id"); match("="); expr(); match(";");
                    break;
                case "id":
                    match("id"); match("="); expr(); match(";");
                    break;
                default:
                    throw new Exception("syntax error!");
            }

            this.currentNode = stmtNode.parent;
        }
        public void expr()
        {
            var exprNode = new SimpleParseTree.Node() { isLeaf = false, name = "expr" };
            parseTree.AppendNode(currentNode, exprNode);
            this.currentNode = exprNode;

            term(); rest();

            this.currentNode = exprNode.parent;
        }
        public void term()
        {
            var termNode = new SimpleParseTree.Node() { isLeaf = false, name = "term" };
            parseTree.AppendNode(currentNode, termNode);
            this.currentNode = termNode;

            switch (lookaheadToken.name)
            {
                case "id":
                    match("id");
                    break;
                case "num":
                    match("num");
                    break;
                default:
                    throw new Exception("syntax error!");
            }

            this.currentNode = termNode.parent;
        }
        public void rest()
        {
            var restNode = new SimpleParseTree.Node() { isLeaf = false, name = "rest" };
            parseTree.AppendNode(currentNode, restNode);
            this.currentNode = restNode;

            switch (lookaheadToken.name)
            {
                case "+":
                    match("+"); term();
                    break;
                case "-":
                    match("-"); term();
                    break;
                default:
                    //ε
                    break;
            }


            this.currentNode = restNode.parent;
        }
        public void match(string terminal)
        {
            if (lookaheadToken.name == terminal)
            {
                var terminalNode = new SimpleParseTree.Node() { isLeaf = true, name = terminal };
                parseTree.AppendNode(currentNode, terminalNode);

                Debug.LogLine("成功匹配:" + terminal);

                lookahead++;
            }
            else
            {
                throw new Exception("syntax error! try match terminal:" + terminal + "  lookahead:" + lookaheadToken.name);
            }
        }
    }

}
