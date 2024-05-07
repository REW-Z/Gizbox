using System;
using System.Collections.Generic;
using System.Text;

using System.Linq;

using System.Linq.Expressions;


namespace FanLang
{
    /// <summary>
    /// 语法分析树（CST）    
    /// </summary>
    public class ParseTree
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


        public ParseTree()
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
                string lineChar = "┖   ";
                for (int i = 0; i < node.depth; ++i)
                {
                    brace += "    ";
                }
                strb.AppendLine(brace + lineChar + (node.isLeaf ? ("<" + node.name + ">") : node.name));
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
    /// 抽象语法树（AST）  
    /// </summary>
    public class SyntaxTree
    {
        public abstract class Node 
        {
            private Node[] children;

            private Node parent;

            public int depth = -1;

            public Dictionary<string, object> attributes;

            public Node[] Children 
            {
                get
                {
                    if (this.children == null)
                        this.children = GetChildren();
                    return this.children;
                }

            }

            public Node Parent
            {
                get
                {
                    return this.parent;
                }
                set 
                {
                    this.parent = value;
                    this.depth = this.parent.depth + 1;
                }
            }

            protected virtual Node[] GetChildren()
            {
                List<Node> nodes = new List<Node>();

                System.Reflection.FieldInfo[] fields = this.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                foreach (var field in fields)
                {
                    if(field.FieldType .IsSubclassOf(typeof(Node)))
                    {
                        nodes.Add(field.GetValue(this) as Node);
                    }

                    if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        Type genericArgument = field.FieldType.GetGenericArguments()[0];
                        if (genericArgument.IsSubclassOf (typeof(Node)))
                        {
                            nodes.AddRange(((IEnumerable<Node>)field.GetValue(this)).Cast<Node>());
                        }
                    }
                }

                return nodes.ToArray();
            }

            public override string ToString()
            {
                return this.GetType().Name.ToString();
            }
        }

        // ******************** ROOT ******************************

        public class ProgramNode : Node
        {
            public List<ImportNode> importNodes;
            public StatementsNode statementsNode;
        }


        // ******************** IMPT NODES ******************************

        public class ImportNode : Node
        {
            public string uri;
        }

        // ******************** STMT NODES ******************************


        public class StatementsNode : Node
        {
            public List<StmtNode> statements = new List<StmtNode>();
        }

        public class StatementBlockNode : StmtNode
        {
            public List<StmtNode> statements;
        }

        public abstract class StmtNode : Node { }


        public abstract class DeclareNode : StmtNode { }

        public class VarDeclareNode : DeclareNode
        {
            public TypeNode typeNode;
            public IdentityNode identifierNode;
            public ExprNode initializerNode;
        }


        public class ExternFuncDeclareNode : DeclareNode
        {
            public TypeNode returnTypeNode;
            public IdentityNode identifierNode;
            public ParameterListNode parametersNode;
        }
        public class FuncDeclareNode : DeclareNode
        {
            public TypeNode returnTypeNode;
            public IdentityNode identifierNode;
            public ParameterListNode parametersNode;
            public StatementsNode statementsNode;
        }

        public class ClassDeclareNode : DeclareNode
        {
            public IdentityNode classNameNode;
            public List<DeclareNode> memberDelareNodes;
        }


        public class SingleExprStmtNode : StmtNode//单个特殊表达式(new、assign、call、increase)的语句  
        {
            public SpecialExprNode exprNode;
        }

        public class BreakStmtNode : StmtNode
        {
        }

        public class ReturnStmtNode : StmtNode
        {
            public ExprNode returnExprNode;
        }

        public class WhileStmtNode : StmtNode
        {
            public ExprNode conditionNode;

            public StmtNode stmtNode;
        }

        public class ForStmtNode : StmtNode
        {
            public StmtNode initializerNode;
            public ExprNode conditionNode;
            public SpecialExprNode iteratorNode;

            public StmtNode stmtNode;
        }

        public class IfStmtNode : StmtNode
        {
            public List<ConditionClauseNode> conditionClauseList;

            public ElseClauseNode elseClause;

            protected override Node[] GetChildren()
            {
                if(elseClause == null)
                {
                    return conditionClauseList.ToArray();
                }
                else
                {
                    Node[] nodes = new Node[conditionClauseList.Count + 1];
                    for (int i = 0; i < conditionClauseList.Count; ++i)
                    {
                        nodes[i] = conditionClauseList[i];
                    }
                    nodes[nodes.Length - 1] = elseClause;
                    return nodes;
                }
            }
        }

        // ******************** CONDITION CLAUSE NODES ******************************
        
        public class ConditionClauseNode : Node
        {
            public ExprNode conditionNode;
            public StmtNode thenNode;
        }
        public class ElseClauseNode : Node
        {
            public StmtNode stmt;
        }

        // ******************** EXPR NODES ******************************
        public abstract class ExprNode : Node { }

        public class IdentityNode : ExprNode
        {
            public Token token;

            public override string ToString()
            {
                return this.GetType().Name + "(\"" + token.attribute + "\")";
            }
        }


        public class LiteralNode : ExprNode
        {
            public Token token;
        }

        public class BinaryOpNode : ExprNode
        {
            public string op;
            public ExprNode leftNode;
            public ExprNode rightNode;

            public override string ToString()
            {
                return this.GetType().Name + "(" + op + ")";
            }
        }

        public class UnaryOpNode : ExprNode
        {
            public string op;
            public ExprNode exprNode;
        }

        public abstract class SpecialExprNode : ExprNode { }//特殊表达式(new、assign、call、increase)（带有副作用）     

        public class AssignNode : SpecialExprNode//赋值表达式（高优先级）
        {
            public string op;//=、+=、-=、*=、/=、%=  
            public ExprNode lvalueNode;
            public ExprNode rvalueNode;
        }

        public class CallNode : SpecialExprNode//调用表达式（低优先级）
        {
            public bool isMemberAccessFunction;

            public ExprNode funcNode;//id or memberaccesss  

            public ArgumentListNode argumantsNode;
        }



        public class IncDecNode : SpecialExprNode//自增自减表达式（低优先级）
        {
            public bool isOperatorFront;//操作符在标识符前
            public string op;//++、--  
            public IdentityNode identifierNode;
        }

        public class NewObjectNode : SpecialExprNode
        {
            public IdentityNode className;
        }

        public class NewArrayNode : SpecialExprNode
        {
            public TypeNode typeNode;

            public ExprNode lengthNode;
        }

        public class CastNode : ExprNode
        {
            public TypeNode typeNode;
            public ExprNode factorNode;
        }

        public class IndexAccessNode : ExprNode
        {
            public bool isMemberAccessContainer;

            public ExprNode containerNode;//id or memberaccesss  

            public ExprNode indexNode;
        }


        public abstract class MemberAccessNode : ExprNode { }

        public class ObjectMemberAccessNode : MemberAccessNode
        {
            public ExprNode objectNode;
            public IdentityNode memberNode;
        }
        public class ThisMemberAccessNode : MemberAccessNode
        {
            public IdentityNode memberNode;
        }

        // ******************** TYPE NODES ******************************

        public abstract class TypeNode : Node { public abstract string ToExpression(); }

        public class ArrayTypeNode : TypeNode
        {
            public TypeNode baseType;

            public override string ToExpression()
            {
                return baseType.ToExpression() + "[]";
            }
        }

        public class ClassTypeNode : TypeNode
        {
            public IdentityNode classname;

            public override string ToExpression()
            {
                return classname.token.attribute;
            }
        }
        public class PrimitiveNode : TypeNode
        {
            public Token token;

            public override string ToExpression()
            {
                return token.name;
            }
        }



        // ******************** OTHER NODES ******************************
        public class ArgumentListNode : Node
        {
            public List<ExprNode> arguments;

            //protected override Node[] GetChildren()
            //{
            //    return arguments.ToArray();
            //}
        }

        public class ParameterListNode : Node
        {
            public List<ParameterNode> parameterNodes;
        }

        public class ParameterNode : Node
        {
            public TypeNode typeNode;
            public IdentityNode identifierNode;
        }


        public class IndexerNode : Node
        {
            public ExprNode indexNode;
        }

        // ******************** Instance Members ******************************
        public ProgramNode rootNode;
        public Node allNodes;
        public SyntaxTree(ProgramNode root)
        {
            this.rootNode = root;

            this.rootNode.depth = 0;

            //建立联系并计算深度      
            Traversal((n) => {
                foreach(var child in n.Children)
                {
                    if(child == null)
                    {
                        throw new Exception("NULL CHILD of" + n.ToString());
                    }
                    child.Parent = n;
                }
            });
        }
        public void Traversal(Action<Node> operation)
        {
            TraversalNode(rootNode, operation);
        }

        private void TraversalNode(Node node, Action<Node> operation)
        {
            operation(node);

            foreach (var child in node.Children)
            {
                TraversalNode(child, operation);
            }
        }


        public string Serialize()
        {
            System.Text.StringBuilder strb = new System.Text.StringBuilder();

            Traversal((node) => {
                string brace = "";
                string lineChar = "┖   ";
                for (int i = 0; i < node.depth; ++i)
                {
                    brace += "    ";
                }
                strb.AppendLine(brace + lineChar + ((node is LiteralNode) ? ((node as LiteralNode).token.ToString()) : node.ToString()));
            });

            return strb.ToString();
        }
    }
}
