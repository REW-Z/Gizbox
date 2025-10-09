using System;
using System.Collections.Generic;
using System.Text;

using System.Linq;

using System.Linq.Expressions;
using System.Net.Http.Headers;
using System.Net;


namespace Gizbox
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

        public void CompleteBuild()
        {
            this.root.depth = 0;
            this.Traversal((node) => {
                foreach (var c in node.children)
                {
                    //深度设置  
                    c.depth = node.depth + 1;
                }
            });
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
        public static string[] compareOprators = new string[] { ">", "<", ">=", "<=", "==", "!=", };


        public abstract class Node 
        {
            private Node[] children;

            private Node parent;

            public Node overrideNode = null;

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
                        var n = field.GetValue(this);
                        if (n != null)
                        {
                            nodes.Add(n as Node);
                        }
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


            private Token startToken;
            private Token endToken;
            public Token StartToken()
            {
                if(this.attributes != null)
                {
                    this.attributes.TryGetValue("start", out object startTokenObj);
                    this.startToken = startTokenObj as Token;
                }
                else
                {
                    for(int i = 0 ; i < Children.Length; i++)
                    {
                        this.startToken = Children[i].StartToken();
                        if(this.startToken != null) break;
                    }
                }
                return this.startToken;
            }
            public Token EndToken()
            {
                if(this.attributes != null)
                {
                    this.attributes.TryGetValue("end", out object endTokenObj);
                    this.endToken = endTokenObj as Token;
                }
                else
                {
                    for(int i = Children.Length - 1; i > -1; i--)
                    {
                        this.endToken = Children[i].EndToken();
                        if(this.endToken != null)
                            break;
                    }
                }
                return this.endToken;
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
            public List<UsingNode> usingNamespaceNodes;
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


        public class UsingNode: Node
        {
            public IdentityNode namespaceNameNode;
        }

        public class NamespaceNode : StmtNode
        {
            public IdentityNode namepsaceNode;
            public StatementsNode stmtsNode;
        }
        
        public abstract class DeclareNode : StmtNode { }

        public class ConstantDeclareNode : DeclareNode
        {
            public TypeNode typeNode;
            public IdentityNode identifierNode;
            public LiteralNode litValNode;
        }

        public enum VarModifiers
        {
            None = 0,
            Own = 2,
            Bor = 4,
        }
        public enum TypeModifiers
        {
            None = 0,
            Own = 2,
        }
        public class VarDeclareNode : DeclareNode
        {

            public VarModifiers flags;
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

        public enum FunctionKind
        {
            Normal,
            OperatorOverload,
        }
        public class FuncDeclareNode : DeclareNode
        {
            public FunctionKind funcType;
            public VarModifiers returnFlags;

            public TypeNode returnTypeNode;
            public IdentityNode identifierNode;
            public ParameterListNode parametersNode;
            public StatementsNode statementsNode;
        }

        public class ClassDeclareNode : DeclareNode
        {
            public TypeModifiers flags;

            public IdentityNode classNameNode;
            public IdentityNode baseClassNameNode;
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

        public class DeleteStmtNode : StmtNode
        {
            public ExprNode objToDelete;
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
            public enum IdType
            {
                Undefined,
                Namespace,
                Class,
                VariableOrField,
                FunctionOrMethod,
            }

            public Token token;

            public IdType identiferType = IdType.Undefined;

            public bool isMemberIdentifier = false;

            private string fullname = null;

            public string FullName
            {
                get 
                {
                    if (fullname == null)
                        return token.attribute;
                    else
                        return fullname;
                }
            }

            public void SetPrefix(string prefix)
            {
                if(string.IsNullOrEmpty(prefix) == false)
                {
                    this.fullname = prefix + "::" + token.attribute;
                }
                else
                {
                    this.fullname = token.attribute;
                }
            }

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

            public bool IsCompare
                => compareOprators.Contains(op);
            public string GetOpName()
            {
                switch(op)
                {
                    case "+": return "add";
                    case "-": return "sub";
                    case "*": return "mul";
                    case "/": return "div";
                    case "%": return "mod";
                }
                return null;
            }
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

        public class ElementAccessNode : ExprNode
        {
            public ExprNode containerNode;//id or memberaccesss  

            public ExprNode indexNode;
        }


        public class ObjectMemberAccessNode : ExprNode
        {
            //attributes: memberType func/var/property
            public ExprNode objectNode;
            public IdentityNode memberNode;
        }

        public class ThisNode : ExprNode
        {
            public Token token;
        }

        // ******************** TYPE NODES ******************************

        public abstract class TypeNode : Node { public abstract string TypeExpression(); }

        public class ArrayTypeNode : TypeNode
        {
            public TypeNode elemtentType;

            public override string TypeExpression()
            {
                return elemtentType.TypeExpression() + "[]";
            }
        }

        public class ClassTypeNode : TypeNode
        {
            public IdentityNode classname;

            public override string TypeExpression()
            {
                return classname.FullName;
            }
        }
        public class PrimitiveTypeNode : TypeNode
        {
            public Token token;

            public override string TypeExpression()
            {
                return token.name;
            }
        }

        public class InferTypeNode : TypeNode
        {
            public override string TypeExpression()
            {
                return "var";
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
            public VarModifiers flags;
            public TypeNode typeNode;
            public IdentityNode identifierNode;
        }


        
        // ******************** Instance Members ******************************
        public ProgramNode rootNode;
        
        public List<SyntaxTree.Node> leafNodes = new List<SyntaxTree.Node>();
        public List<IdentityNode> identityNodes = new List<IdentityNode>();
        public List<LiteralNode> literalNodes = new List<LiteralNode>();


        public SyntaxTree(ProgramNode root)
        {
            this.rootNode = root;

            this.rootNode.depth = 0;

            //建立联系并计算深度      
            Traversal((n) => {

                //叶子节点收集  
                if(n.Children.Length == 0)
                {
                    leafNodes.Add(n);
                }
                foreach(var child in n.Children)
                {
                    //父子关系  
                    if(child == null)
                    {
                        throw new Exception("NULL CHILD of" + n.ToString());
                    }
                    child.Parent = n;

                    //标识符收集  
                    switch(child)
                    {
                        case SyntaxTree.IdentityNode:
                            identityNodes.Add(child as SyntaxTree.IdentityNode);
                            break;
                        case SyntaxTree.LiteralNode:
                            literalNodes.Add(child as SyntaxTree.LiteralNode);
                            break;
                    }
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
