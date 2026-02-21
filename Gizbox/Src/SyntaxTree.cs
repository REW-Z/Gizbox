using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using System.Linq;

using System.Linq.Expressions;
using System.Net.Http.Headers;
using System.Net;


using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Reflection;


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
    [DataContract(IsReference = true)]
    public partial class SyntaxTree
    {
        public static string[] compareOprators = new string[] { ">", "<", ">=", "<=", "==", "!=", };


        [DataContract(IsReference = true)]
        [KnownType(typeof(ProgramNode))]
        [KnownType(typeof(ImportNode))]
        [KnownType(typeof(StatementsNode))]
        [KnownType(typeof(StatementBlockNode))]
        [KnownType(typeof(UsingNode))]
        [KnownType(typeof(NamespaceNode))]
        [KnownType(typeof(ConstantDeclareNode))]
        [KnownType(typeof(VarDeclareNode))]
        [KnownType(typeof(ExternFuncDeclareNode))]
        [KnownType(typeof(FuncDeclareNode))]
        [KnownType(typeof(ClassDeclareNode))]
        [KnownType(typeof(OwnershipLeakStmtNode))]
        [KnownType(typeof(OwnershipCaptureStmtNode))]
        [KnownType(typeof(SingleExprStmtNode))]
        [KnownType(typeof(BreakStmtNode))]
        [KnownType(typeof(ReturnStmtNode))]
        [KnownType(typeof(DeleteStmtNode))]
        [KnownType(typeof(WhileStmtNode))]
        [KnownType(typeof(ForStmtNode))]
        [KnownType(typeof(IfStmtNode))]
        [KnownType(typeof(ConditionClauseNode))]
        [KnownType(typeof(ElseClauseNode))]
        [KnownType(typeof(IdentityNode))]
        [KnownType(typeof(LiteralNode))]
        [KnownType(typeof(DefaultValueNode))]
        [KnownType(typeof(BinaryOpNode))]
        [KnownType(typeof(UnaryOpNode))]
        [KnownType(typeof(AssignNode))]
        [KnownType(typeof(CallNode))]
        [KnownType(typeof(ReplaceNode))]
        [KnownType(typeof(IncDecNode))]
        [KnownType(typeof(NewObjectNode))]
        [KnownType(typeof(NewArrayNode))]
        [KnownType(typeof(CastNode))]
        [KnownType(typeof(ElementAccessNode))]
        [KnownType(typeof(ObjectMemberAccessNode))]
        [KnownType(typeof(ThisNode))]
        [KnownType(typeof(TypeOfNode))]
        [KnownType(typeof(SizeOfNode))]
        [KnownType(typeof(ArrayTypeNode))]
        [KnownType(typeof(ClassTypeNode))]
        [KnownType(typeof(PrimitiveTypeNode))]
        [KnownType(typeof(InferTypeNode))]
        [KnownType(typeof(ArgumentListNode))]
        [KnownType(typeof(ParameterListNode))]
        [KnownType(typeof(ParameterNode))]
        public abstract class Node 
        {
            [DataMember]
            protected List<Node> children_group_0; //可选附加节点列表1
            [DataMember]
            protected List<Node> children_group_1; //可选附加节点列表1
            [DataMember]
            protected List<Node> children_group_2; //可选附加节点列表2

            [IgnoreDataMember]
            private Node parent;

            [IgnoreDataMember]
            public Node overrideNode = null;
            [IgnoreDataMember]
            public Node rawNode = null;

            [DataMember]
            public bool isOverrideReplaced = false;
            [DataMember]
            public int depth = -1;

            [IgnoreDataMember]
            public Dictionary<AstAttr, object> attributes = new();



            public Node Parent
            {
                get
                {
                    return this.parent;
                }
                set 
                {
                    this.parent = value;
                    
                    if(this.parent != null)
                    {
                        this.depth = this.parent.depth + 1;
                    }
                }
            }


            public int ChildCount
            {
                get
                { 
                    int count = 0;
                    if(this.children_group_0 != null)
                    {
                        count += this.children_group_0.Count;
                    }
                    if(this.children_group_1 != null)
                    {
                        count += this.children_group_1.Count;
                    }
                    if(this.children_group_2 != null)
                    {
                        count += this.children_group_2.Count;
                    }
                    return count;
                }
            }
            public IEnumerable<Node> Children()
            {
                if(this.children_group_0 != null)
                {
                    foreach(var child in this.children_group_0)
                    {
                        yield return child;
                    }
                }
                if(this.children_group_1 != null)
                {
                    foreach(var child in this.children_group_1)
                    {
                        yield return child;
                    }
                }
                if(this.children_group_2 != null)
                {
                    foreach(var child in this.children_group_2)
                    {
                        yield return child;
                    }
                }
            }
            public Node GetChild(int idx)
            {
                if(this.children_group_0 != null)
                {
                    if(idx < this.children_group_0.Count)
                    {
                        return this.children_group_0[idx];
                    }
                    else
                    {
                        idx -= this.children_group_0.Count;
                    }
                }
                if(this.children_group_1 != null)
                {
                    if(idx < this.children_group_1.Count)
                    {
                        return this.children_group_1[idx];
                    }
                    else
                    {
                        idx -= this.children_group_1.Count;
                    }
                }
                if(this.children_group_2 != null)
                {
                    if(idx < this.children_group_2.Count)
                    {
                        return this.children_group_2[idx];
                    }
                    else
                    {
                        idx -= this.children_group_2.Count;
                    }
                }
                throw new IndexOutOfRangeException();
            }
            public void ReplaceChild(Node oldNode, Node newNode)
            {
                if(this.children_group_0 != null)
                {
                    int idx = this.children_group_0.IndexOf(oldNode);
                    if(idx != -1)
                    {
                        this.children_group_0[idx] = newNode;
                        return;
                    }
                }
                if(this.children_group_1 != null)
                {
                    int idx = this.children_group_1.IndexOf(oldNode);
                    if(idx != -1)
                    {
                        this.children_group_1[idx] = newNode;
                        return;
                    }
                }
                if(this.children_group_2 != null)
                {
                    int idx = this.children_group_2.IndexOf(oldNode);
                    if(idx != -1)
                    {
                        this.children_group_2[idx] = newNode;
                        return;
                    }
                }

                newNode.Parent = this;
                throw new ArgumentException("The specified oldNode is not a child of this node.");
            }

            private Token startToken;
            private Token endToken;
            public Token StartToken()
            {
                if(this.attributes != null && this.attributes.TryGetValue(AstAttr.start, out object startTokenObj))
                {
                    this.startToken = startTokenObj as Token;
                }

                if(this.startToken != null)
                    return this.startToken;

                if(this is IdentityNode idNode)
                    return idNode.token;
                if(this is LiteralNode litNode)
                    return litNode.token;
                if(this is ThisNode thisNode)
                    return thisNode.token;
                if(this is PrimitiveTypeNode primitiveTypeNode)
                    return primitiveTypeNode.token;

                for(int i = 0 ; i < ChildCount; i++)
                {
                    this.startToken = GetChild(i).StartToken();
                    if(this.startToken != null)
                        break;
                }

                return this.startToken;
            }
            public Token EndToken()
            {
                if(this.attributes != null && this.attributes.TryGetValue(AstAttr.end, out object endTokenObj))
                {
                    this.endToken = endTokenObj as Token;
                }

                if(this.endToken != null)
                    return this.endToken;

                if(this is IdentityNode idNode)
                    return idNode.token;
                if(this is LiteralNode litNode)
                    return litNode.token;
                if(this is ThisNode thisNode)
                    return thisNode.token;
                if(this is PrimitiveTypeNode primitiveTypeNode)
                    return primitiveTypeNode.token;

                for(int i = ChildCount - 1; i > -1; i--)
                {
                    this.endToken = GetChild(i).EndToken();
                    if(this.endToken != null)
                        break;
                }

                return this.endToken;
            }

            public override string ToString()
            {
                return this.GetType().Name.ToString();
            }

            public Node DeepClone()
            {
                return DeepClone(new Dictionary<Node, Node>());
            }

            internal virtual Node DeepClone(Dictionary<Node, Node> visited)
            {
                if(visited.TryGetValue(this, out var existing))
                    return existing;

                var clone = (Node)Activator.CreateInstance(GetType());
                visited[this] = clone;

                clone.isOverrideReplaced = isOverrideReplaced;
                clone.depth = depth;
                clone.attributes = CloneAttributes(attributes, visited);
                clone.attributes ??= new();//源字典可能为空

                clone.children_group_0 = CloneChildrenGroup(children_group_0, clone.children_group_0, visited, clone);
                clone.children_group_1 = CloneChildrenGroup(children_group_1, clone.children_group_1, visited, clone);
                clone.children_group_2 = CloneChildrenGroup(children_group_2, clone.children_group_2, visited, clone);

                clone.overrideNode = overrideNode?.DeepClone(visited);
                clone.rawNode = rawNode?.DeepClone(visited);

                CopyCustomFields(this, clone, visited);

                return clone;
            }
        }

        private static List<Node> CloneChildrenGroup(List<Node> source, List<Node> target, Dictionary<Node, Node> visited, Node parent)
        {
            if(source == null)
            {
                if(target != null)
                    target.Clear();
                return target;
            }

            if(target == null)
                target = new List<Node>();
            else
                target.Clear();

            foreach(var child in source)
            {
                var cloned = child?.DeepClone(visited);
                if(cloned != null)
                    cloned.Parent = parent;
                target.Add(cloned);
            }

            return target;
        }

        private static Dictionary<AstAttr, object> CloneAttributes(Dictionary<AstAttr, object> attributes, Dictionary<Node, Node> visited)
        {
            if(attributes == null)
                return null;

            var cloned = new Dictionary<AstAttr, object>();
            foreach(var kv in attributes)
            {
                cloned[kv.Key] = CloneAttributeValue(kv.Value, visited);
            }
            return cloned;
        }

        private static object CloneAttributeValue(object value, Dictionary<Node, Node> visited)
        {
            if(value == null)
                return null;

            if(value is Node node)
                return node.DeepClone(visited);

            if(value is Token token)
                return CloneToken(token);

            if(value is IList list)
            {
                var listType = value.GetType();
                if(listType.IsGenericType)
                {
                    var elementType = listType.GetGenericArguments()[0];
                    var newList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
                    foreach(var item in list)
                    {
                        newList.Add(CloneAttributeValue(item, visited));
                    }
                    return newList;
                }

                var copy = new ArrayList();
                foreach(var item in list)
                {
                    copy.Add(CloneAttributeValue(item, visited));
                }
                return copy;
            }

            return value;
        }

        private static void CopyCustomFields(Node source, Node target, Dictionary<Node, Node> visited)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fields = source.GetType().GetFields(flags);
            foreach(var field in fields)
            {
                if(field.DeclaringType == typeof(Node))
                    continue;

                if(field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(ChildList<>))
                    continue;

                var sourceValue = field.GetValue(source);

                if(typeof(Node).IsAssignableFrom(field.FieldType))
                {
                    field.SetValue(target, sourceValue == null ? null : ((Node)sourceValue).DeepClone(visited));
                    continue;
                }

                if(typeof(Token).IsAssignableFrom(field.FieldType))
                {
                    field.SetValue(target, CloneToken((Token)sourceValue));
                    continue;
                }

                if(typeof(IList).IsAssignableFrom(field.FieldType) && sourceValue is IList sourceList)
                {
                    var targetList = field.GetValue(target) as IList;
                    if(targetList == null)
                    {
                        if(field.IsInitOnly)
                            continue;

                        targetList = (IList)Activator.CreateInstance(field.FieldType);
                        field.SetValue(target, targetList);
                    }

                    targetList.Clear();
                    foreach(var item in sourceList)
                    {
                        targetList.Add(CloneAttributeValue(item, visited));
                    }
                    continue;
                }

                if(field.IsInitOnly)
                    continue;

                field.SetValue(target, sourceValue);
            }
        }

        private static Token CloneToken(Token token)
        {
            if(token == null)
                return null;

            return new Token(token.name, token.patternType, token.attribute, token.line, token.start, token.length);
        }

        public class ChildList<T> where T : Node
        {
            private List<Node> container;
            public ChildList(List<Node> c)
            {
                container = c;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Add(T node)
            {
                container.Add(node);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Remove(T node)
            {
                container.Remove(node);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddRange(IEnumerable<T> nodes)
            {
                container.AddRange(nodes);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Insert(int index, T node)
            {
                container.Insert(index, node);
            }

            public int Count
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => container.Count;
            }

            public T this[int index]
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => container[index] as T;
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                set => container[index] = value;
            }

            public IEnumerator<T> GetEnumerator()
            {
                foreach(var node in container)
                {
                    yield return (node as T);
                }
            }

            public IEnumerable<TResult> Select<TResult>(Func<T, TResult> selector)
            {
                foreach (var node in container)
                {
                    yield return selector(node as T);
                }
            }

            public bool Any(Func<T, bool> predicate)
            {
                foreach (var node in container)
                {
                    if (predicate(node as T))
                    {
                        return true;
                    }
                }
                return false;
            }

            public List<T> ToList()
            {
                List<T> list = new List<T>();
                foreach (var node in container)
                {
                    list.Add(node as T);
                }
                return list;
            }

        }

        // ******************** ROOT ******************************

        [DataContract(IsReference = true)]
        public class ProgramNode : Node
        {
            public readonly ChildList<ImportNode> importNodes;
            public readonly ChildList<UsingNode> usingNamespaceNodes;
            public StatementsNode statementsNode { get => (StatementsNode)children_group_2[0]; set => children_group_2[0] = value; }

            public ProgramNode()
            {
                children_group_0 = new();
                children_group_1 = new();
                children_group_2 = new();
                children_group_2.Add(null);

                importNodes = new ChildList<ImportNode>(children_group_0);
                usingNamespaceNodes = new ChildList<UsingNode>(children_group_1);
            }
        }


        // ******************** IMPT NODES ******************************

        [DataContract(IsReference = true)]
        public class ImportNode : Node
        {
            [DataMember]
            public string uri;
        }

        // ******************** STMT NODES ******************************


        [DataContract(IsReference = true)]
        public class StatementsNode : Node
        {
            public readonly ChildList<StmtNode> statements;

            public StatementsNode()
            {
                children_group_0 = new();
                statements = new ChildList<StmtNode>(children_group_0);
            }
        }

        [DataContract(IsReference = true)]
        public class StatementBlockNode : StmtNode
        {
            public readonly ChildList<StmtNode> statements;

            public StatementBlockNode()
            {
                children_group_0 = new();
                statements = new ChildList<StmtNode>(children_group_0);
            }
        }

        [DataContract(IsReference = true)]
        public abstract class StmtNode : Node { }


        [DataContract(IsReference = true)]
        public class UsingNode: Node
        {
            public IdentityNode namespaceNameNode { get => (IdentityNode)children_group_0[0]; set => children_group_0[0] = value; }

            public UsingNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
            }
        }

        [DataContract(IsReference = true)]
        public class NamespaceNode : StmtNode
        {
            public IdentityNode namepsaceNode { get => (IdentityNode)children_group_0[0]; set => children_group_0[0] = value; }
            public StatementsNode stmtsNode { get => (StatementsNode)children_group_0[1]; set => children_group_0[1] = value; }

            public NamespaceNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
                children_group_0.Add(null);
            }

        }
        
        [DataContract(IsReference = true)]
        public abstract class DeclareNode : StmtNode { }

        [DataContract(IsReference = true)]
        public class ConstantDeclareNode : DeclareNode
        {
            public TypeNode typeNode { get => (TypeNode)children_group_0[0]; set => children_group_0[0] = value; }
            public IdentityNode identifierNode { get => (IdentityNode)children_group_0[1]; set => children_group_0[1] = value; }
            public LiteralNode litValNode { get => (LiteralNode)children_group_0[2]; set => children_group_0[2] = value; }

            public ConstantDeclareNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
                children_group_0.Add(null);
                children_group_0.Add(null);
            }
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
        [DataContract(IsReference = true)]
        public class VarDeclareNode : DeclareNode
        {
            [DataMember]
            public VarModifiers flags;
            public TypeNode typeNode { get => (TypeNode)children_group_0[0]; set => children_group_0[0] = value; }
            public IdentityNode identifierNode { get => (IdentityNode)children_group_0[1]; set => children_group_0[1] = value; }
            public ExprNode initializerNode { get => (ExprNode)children_group_0[2]; set => children_group_0[2] = value; }

            public VarDeclareNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
                children_group_0.Add(null);
                children_group_0.Add(null);
            }
        }


        [DataContract(IsReference = true)]
        public class ExternFuncDeclareNode : DeclareNode
        {
            public TypeNode returnTypeNode { get => (TypeNode)children_group_0[0]; set => children_group_0[0] = value; }
            public IdentityNode identifierNode { get => (IdentityNode)children_group_0[1]; set => children_group_0[1] = value; }
            public ParameterListNode parametersNode { get => (ParameterListNode)children_group_0[2]; set => children_group_0[2] = value; }

            public ExternFuncDeclareNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
                children_group_0.Add(null);
                children_group_0.Add(null);
            }
        }

        public enum FunctionKind
        {
            Normal,
            OperatorOverload,
        }
        [DataContract(IsReference = true)]
        public class FuncDeclareNode : DeclareNode
        {
            [DataMember]
            public FunctionKind funcType;
            [DataMember]
            public VarModifiers returnFlags;
            [DataMember]
            public bool isTemplateFunction;
            [DataMember]
            public readonly List<IdentityNode> templateParameters = new();

            public TypeNode returnTypeNode { get => (TypeNode)children_group_0[0]; set => children_group_0[0] = value; }
            public IdentityNode identifierNode { get => (IdentityNode)children_group_0[1]; set => children_group_0[1] = value; }
            public ParameterListNode parametersNode { get => (ParameterListNode)children_group_0[2]; set => children_group_0[2] = value; }
            public StatementsNode statementsNode { get => (StatementsNode)children_group_0[3]; set => children_group_0[3] = value; }

            public FuncDeclareNode()
            {
                children_group_0 = new();
                children_group_0.AddRange(new Node[4]);
            }

        }

        [DataContract(IsReference = true)]
        public class ClassDeclareNode : DeclareNode
        {
            [DataMember]
            public TypeModifiers flags;
            [DataMember]
            public bool isTemplateClass;
            [DataMember]
            public readonly List<IdentityNode> templateParameters = new();

            public IdentityNode classNameNode { get => (IdentityNode)children_group_0[0]; set => children_group_0[0] = value; }
            public IdentityNode baseClassNameNode { get => (IdentityNode)children_group_0[1]; set => children_group_0[1] = value; }
            public readonly ChildList<DeclareNode> memberDelareNodes;

            public ClassDeclareNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
                children_group_0.Add(null);
                children_group_1 = new();
                memberDelareNodes = new ChildList<DeclareNode>(children_group_1);
            }

        }

        [DataContract(IsReference = true)]
        public class OwnershipLeakStmtNode : StmtNode
        {
            public TypeNode typeNode { get => (TypeNode)children_group_0[0]; set => children_group_0[0] = value; }
            public IdentityNode lIdentifier { get => (IdentityNode)children_group_0[1]; set => children_group_0[1] = value; }
            public IdentityNode rIdentifier { get => (IdentityNode)children_group_0[2]; set => children_group_0[2] = value; }

            public OwnershipLeakStmtNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
                children_group_0.Add(null);
                children_group_0.Add(null);
            }
        }
        [DataContract(IsReference = true)]
        public class OwnershipCaptureStmtNode : StmtNode
        {
            [DataMember]
            public VarModifiers flags;
            public TypeNode typeNode { get => (TypeNode)children_group_0[0]; set => children_group_0[0] = value; }
            public IdentityNode lIdentifier { get => (IdentityNode)children_group_0[1]; set => children_group_0[1] = value; }
            public IdentityNode rIdentifier { get => (IdentityNode)children_group_0[2]; set => children_group_0[2] = value; }

            public OwnershipCaptureStmtNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
                children_group_0.Add(null);
                children_group_0.Add(null);
            }
        }

        [DataContract(IsReference = true)]
        public class SingleExprStmtNode : StmtNode//单个特殊表达式(new、assign、call、increase)的语句  
        {
            public SpecialExprNode exprNode { get => (SpecialExprNode)children_group_0[0]; set => children_group_0[0] = value; }

            public SingleExprStmtNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
            }
        }

        [DataContract(IsReference = true)]
        public class BreakStmtNode : StmtNode
        {
        }

        [DataContract(IsReference = true)]
        public class ReturnStmtNode : StmtNode
        {
            public ExprNode returnExprNode { get => (ExprNode)children_group_0[0]; set => children_group_0[0] = value; }
            public ReturnStmtNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
            }
        }

        [DataContract(IsReference = true)]
        public class DeleteStmtNode : StmtNode
        {
            [DataMember]
            public bool isArrayDelete;

            public ExprNode objToDelete { get => (ExprNode)children_group_0[0]; set => children_group_0[0] = value; }
            public DeleteStmtNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
            }
        }

        [DataContract(IsReference = true)]
        public class WhileStmtNode : StmtNode
        {
            public ExprNode conditionNode { get => (ExprNode)children_group_0[0]; set => children_group_0[0] = value; }
            public StmtNode stmtNode { get => (StmtNode)children_group_0[1]; set => children_group_0[1] = value; }

            public WhileStmtNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
                children_group_0.Add(null);
            }
        }

        [DataContract(IsReference = true)]
        public class ForStmtNode : StmtNode
        {
            public StmtNode initializerNode { get => (StmtNode)children_group_0[0]; set => children_group_0[0] = value; }
            public ExprNode conditionNode { get => (ExprNode)children_group_0[1]; set => children_group_0[1] = value; }
            public SpecialExprNode iteratorNode { get => (SpecialExprNode)children_group_0[2]; set => children_group_0[2] = value; }

            public StmtNode stmtNode { get => (StmtNode)children_group_0[3]; set => children_group_0[3] = value; }

            public ForStmtNode()
            {
                children_group_0 = new();
                children_group_0.AddRange(new Node[4]);
            }
        }

        [DataContract(IsReference = true)]
        public class IfStmtNode : StmtNode
        {
            public readonly ChildList<ConditionClauseNode> conditionClauseList;

            public ElseClauseNode elseClause { get => (ElseClauseNode)children_group_1[0]; set => children_group_1[0] = value; }

            public IfStmtNode()
            {
                children_group_0 = new();
                conditionClauseList = new ChildList<ConditionClauseNode>(children_group_0);
                children_group_1 = new();
                children_group_1.Add(null);
            }
        }


        // ******************** CONDITION CLAUSE NODES ******************************

        [DataContract(IsReference = true)]
        public class ConditionClauseNode : Node
        {
            public ExprNode conditionNode { get => (ExprNode)children_group_0[0]; set => children_group_0[0] = value; }
            public StmtNode thenNode { get => (StmtNode)children_group_0[1]; set => children_group_0[1] = value; }

            public ConditionClauseNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
                children_group_0.Add(null);
            }
        }
        [DataContract(IsReference = true)]
        public class ElseClauseNode : Node
        {
            public StmtNode stmt { get => (StmtNode)children_group_0[0]; set => children_group_0[0] = value; }
            
            public ElseClauseNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
            }
        }

        // ******************** EXPR NODES ******************************
        [DataContract(IsReference = true)]
        public abstract class ExprNode : Node { }

        [DataContract(IsReference = true)]
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

            [DataMember]
            public Token token;

            [DataMember]
            public IdType identiferType = IdType.Undefined;

            [DataMember]
            public bool isMemberIdentifier = false;

            [DataMember]
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
                    this.fullname = null;
                }
            }

            public override string ToString()
            {
                return this.GetType().Name + "(\"" + token.attribute + "\")";
            }
        }


        [DataContract(IsReference = true)]
        public class LiteralNode : ExprNode
        {
            [DataMember]
            public Token token;
        }

        [DataContract(IsReference = true)]
        public class DefaultValueNode : ExprNode
        {
            public TypeNode typeNode { get => (TypeNode)children_group_0[0]; set => children_group_0[0] = value; }

            public DefaultValueNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
            }
        }

        [DataContract(IsReference = true)]
        public class BinaryOpNode : ExprNode
        {
            [DataMember]
            public string op;
            public ExprNode leftNode { get => (ExprNode)children_group_0[0]; set => children_group_0[0] = value; }
            public ExprNode rightNode { get => (ExprNode)children_group_0[1]; set => children_group_0[1] = value; }

            public BinaryOpNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
                children_group_0.Add(null);
            }

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

        [DataContract(IsReference = true)]
        public class UnaryOpNode : ExprNode
        {
            [DataMember]
            public string op;
            public ExprNode exprNode { get => (ExprNode)children_group_0[0]; set => children_group_0[0] = value; }

            public UnaryOpNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
            }
        }

        [DataContract(IsReference = true)]
        public abstract class SpecialExprNode : ExprNode { }//特殊表达式(new、assign、call、increase)（带有副作用）     

        [DataContract(IsReference = true)]
        public class AssignNode : SpecialExprNode//赋值表达式（高优先级）
        {
            [DataMember]
            public string op;//=、+=、-=、*=、/=、%=  
            public ExprNode lvalueNode { get => (ExprNode)children_group_0[0]; set => children_group_0[0] = value; }
            public ExprNode rvalueNode { get => (ExprNode)children_group_0[1]; set => children_group_0[1] = value; }

            public AssignNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
                children_group_0.Add(null);
            }
        }

        [DataContract(IsReference = true)]
        public class CallNode : SpecialExprNode//调用表达式（低优先级）
        {
            [DataMember]
            public bool isMemberAccessFunction;
            [DataMember]
            public readonly List<TypeNode> genericArguments = new();

            //id or memberaccesss  
            public ExprNode funcNode { get => (ExprNode)children_group_0[0]; set => children_group_0[0] = value; }
            public ArgumentListNode argumantsNode { get => (ArgumentListNode)children_group_0[1]; set => children_group_0[1] = value; }

            public CallNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
                children_group_0.Add(null);
            }
        }

        [DataContract(IsReference = true)]
        public class ReplaceNode : SpecialExprNode//replace(member, newValue)
        {
            public ExprNode targetNode { get => (ExprNode)children_group_0[0]; set => children_group_0[0] = value; }
            public ExprNode newValueNode { get => (ExprNode)children_group_0[1]; set => children_group_0[1] = value; }

            public ReplaceNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
                children_group_0.Add(null);
            }
        }



        [DataContract(IsReference = true)]
        public class IncDecNode : SpecialExprNode//自增自减表达式（低优先级）
        {
            [DataMember]
            public bool isOperatorFront;//操作符在标识符前
            [DataMember]
            public string op;//++、--  
            public IdentityNode identifierNode { get => (IdentityNode)children_group_0[0]; set => children_group_0[0] = value; }

            public IncDecNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
            }
        }

        [DataContract(IsReference = true)]
        public class NewObjectNode : SpecialExprNode
        {
            public IdentityNode className { get => (IdentityNode)children_group_0[0]; set => children_group_0[0] = value; }
            [DataMember]
            public TypeNode typeNode;

            public NewObjectNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
            }
        }

        [DataContract(IsReference = true)]
        public class NewArrayNode : SpecialExprNode
        {
            public TypeNode typeNode { get => (TypeNode)children_group_0[0]; set => children_group_0[0] = value; }
            public ExprNode lengthNode { get => (ExprNode)children_group_0[1]; set => children_group_0[1] = value; }

            public NewArrayNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
                children_group_0.Add(null);
            }
        }

        [DataContract(IsReference = true)]
        public class CastNode : ExprNode
        {
            public TypeNode typeNode { get => (TypeNode)children_group_0[0]; set => children_group_0[0] = value; }
            public ExprNode factorNode { get => (ExprNode)children_group_0[1]; set => children_group_0[1] = value; }

            public CastNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
                children_group_0.Add(null);
            }
        }

        [DataContract(IsReference = true)]
        public class ElementAccessNode : ExprNode
        {
            //id or memberaccesss  
            public ExprNode containerNode { get => (ExprNode)children_group_0[0]; set => children_group_0[0] = value; }
            public ExprNode indexNode { get => (ExprNode)children_group_0[1]; set => children_group_0[1] = value; }

            public ElementAccessNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
                children_group_0.Add(null);
            }
        }


        [DataContract(IsReference = true)]
        public class ObjectMemberAccessNode : ExprNode
        {
            //attributes: memberType func/var/property
            public ExprNode objectNode { get => (ExprNode)children_group_0[0]; set => children_group_0[0] = value; }
            public IdentityNode memberNode { get => (IdentityNode)children_group_0[1]; set => children_group_0[1] = value; }

            public ObjectMemberAccessNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
                children_group_0.Add(null);
            }
        }

        [DataContract(IsReference = true)]
        public class ThisNode : ExprNode
        {
            [DataMember]
            public Token token;
        }

        [DataContract(IsReference = true)]
        public class TypeOfNode : ExprNode
        {
            public TypeNode typeNode { get => (TypeNode)children_group_0[0]; set => children_group_0[0] = value; }

            public TypeOfNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
            }
        }
        [DataContract(IsReference = true)]
        public class SizeOfNode : ExprNode
        {
            public TypeNode typeNode { get => (TypeNode)children_group_0[0]; set => children_group_0[0] = value; }

            public SizeOfNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
            }
        }



        // ******************** TYPE NODES ******************************

        [DataContract(IsReference = true)]
        [KnownType(typeof(ArrayTypeNode))]
        [KnownType(typeof(ClassTypeNode))]
        [KnownType(typeof(PrimitiveTypeNode))]
        [KnownType(typeof(InferTypeNode))]
        public abstract class TypeNode : Node { public abstract string TypeExpression(); }

        [DataContract(IsReference = true)]
        public class ArrayTypeNode : TypeNode
        {
            public TypeNode elemtentType { get => (TypeNode)children_group_0[0]; set => children_group_0[0] = value; }

            public ArrayTypeNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
            }

            public override string TypeExpression()
            {
                return elemtentType.TypeExpression() + "[]";
            }
        }

        [DataContract(IsReference = true)]
        public class ClassTypeNode : TypeNode
        {
            public IdentityNode classname { get => (IdentityNode)children_group_0[0]; set => children_group_0[0] = value; }
            [DataMember]
            public readonly List<TypeNode> genericArguments = new();

            public ClassTypeNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
            }

            public override string TypeExpression()
            {
                if(genericArguments.Count == 0)
                    return classname.FullName;

                return Utils.MangleTemplateInstanceName(classname.FullName, genericArguments.Select(t => t.TypeExpression()));
            }
        }
        [DataContract(IsReference = true)]
        public class PrimitiveTypeNode : TypeNode
        {
            [DataMember]
            public Token token;

            public override string TypeExpression()
            {
                return token.name;
            }
        }

        [DataContract(IsReference = true)]
        public class InferTypeNode : TypeNode
        {
            public override string TypeExpression()
            {
                return "var";
            }
        }


        // ******************** OTHER NODES ******************************
        [DataContract(IsReference = true)]
        public class ArgumentListNode : Node
        {
            public readonly ChildList<ExprNode> arguments;

            public ArgumentListNode()
            {
                children_group_0 = new();
                arguments = new ChildList<ExprNode>(children_group_0);
            }
        }

        [DataContract(IsReference = true)]
        public class ParameterListNode : Node
        {
            public readonly ChildList<ParameterNode> parameterNodes;

            public ParameterListNode()
            {
                children_group_0 = new();
                parameterNodes = new ChildList<ParameterNode>(children_group_0);
            }
        }

        [DataContract(IsReference = true)]
        public class ParameterNode : Node
        {
            [DataMember]
            public VarModifiers flags;
            public TypeNode typeNode { get => (TypeNode)children_group_0[0]; set => children_group_0[0] = value; }
            public IdentityNode identifierNode { get => (IdentityNode)children_group_0[1]; set => children_group_0[1] = value; }

            public ParameterNode()
            {
                children_group_0 = new();
                children_group_0.Add(null);
                children_group_0.Add(null);
            }
        }


    }




    // ******************** Instance Members ******************************
    public partial class SyntaxTree
    {
        public ProgramNode rootNode;
        
        public List<SyntaxTree.Node> leafNodes = new List<SyntaxTree.Node>();
        public List<IdentityNode> identityNodes = new List<IdentityNode>();
        public List<LiteralNode> literalNodes = new List<LiteralNode>();


        public SyntaxTree(ProgramNode root)
        {
            if(root == null)
                throw new ArgumentNullException("root");

            this.rootNode = root;

            this.rootNode.depth = 0;

            //建立联系并计算深度      
            Traversal((n) => {

                //叶子节点收集  
                if(n.ChildCount == 0)
                {
                    leafNodes.Add(n);
                }
                foreach(var child in n.Children())
                {
                    //父子关系  
                    if(child == null)
                    {
                        continue;
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
            },
            overrideFirsrt: false //此时还没有override  
            );
        }

        public void Traversal(Action<Node> operation, bool overrideFirsrt)
        {
            TraversalNode(rootNode, operation, overrideFirsrt);
        }

        private void TraversalNode(Node node, Action<Node> operation, bool overrideFirst)
        {
            operation(node);

            foreach (var child in node.Children())
            {
                if(child == null)
                    continue;

                if(child.overrideNode != null && overrideFirst)
                    TraversalNode(child.overrideNode, operation, overrideFirst);
                else
                    TraversalNode(child, operation, overrideFirst);
            }
        }

        public void ApplyAllOverrides()
        {
            void ReplacementTraversal(Node node)
            {
                List<(Node, Node)> replaceTemp = null;

                foreach(var child in node.Children())
                {
                    if(child == null)
                        continue;

                    if(child.overrideNode != null)
                    {
                        if(replaceTemp == null)
                            replaceTemp = new();

                        replaceTemp.Add((child, child.overrideNode));
                    }
                }

                if(replaceTemp != null)
                {
                    foreach(var (oldNode, overrideNode) in replaceTemp)
                    {
                        node.ReplaceChild(oldNode, overrideNode);
                        overrideNode.rawNode = oldNode;
                        overrideNode.isOverrideReplaced = true;
                        oldNode.overrideNode = null;
                    }
                }


                foreach(var effectiveChild in node.Children())
                {
                    if(effectiveChild == null)
                        continue;
                    if(effectiveChild.overrideNode != null)
                        throw new Exception("error.");

                    if(effectiveChild.rawNode != null)
                        GixConsole.WriteLine(effectiveChild.GetType().Name + " raw: " + effectiveChild.rawNode.GetType().Name);

                    ReplacementTraversal(effectiveChild);
                }
            }

            ReplacementTraversal(rootNode);

            GixConsole.WriteLine(CompleteSerialize());
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
            }, 
            overrideFirsrt: true
            );

            return strb.ToString();
        }

        public string CompleteSerialize()
        {
            System.Text.StringBuilder strb = new System.Text.StringBuilder();


            void Visit(Node n, int trueDep)
            {
                string brace = "";
                string lineChar = "┖   ";
                if(n != null)
                {
                    for(int i = 0; i < trueDep; ++i)
                    {
                        brace += "    ";
                    }
                    strb.AppendLine(brace + lineChar + ((n is LiteralNode) ? ((n as LiteralNode).token.ToString()) : n.ToString()));

                    if(n.overrideNode != null)
                    {
                        for(int i = 0; i < trueDep; ++i)
                        {
                            brace += "    ";
                        }
                        strb.AppendLine(brace + lineChar + "(override)" + ((n.overrideNode is LiteralNode) ? ((n.overrideNode as LiteralNode).token.ToString()) : n.overrideNode.ToString()));
                    }

                    foreach(var child in n.Children())
                    {
                        Visit(child, trueDep + 1);
                    }
                }
            }

            Visit(rootNode, 0);

            return strb.ToString();
        }
    }
}