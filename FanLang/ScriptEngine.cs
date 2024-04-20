using System;
using System.Collections.Generic;
using System.Text;
using FanLang;


namespace FanLang.ScriptEngine
{
    public enum ExecuteState
    {
        Finish,
        Break,
        Error,
    }
    public struct ExecuteResult
    {
        public object returnValue;
        public ExecuteState state;


        public static implicit operator ExecuteResult(int ret) => Return(ret);
        public static implicit operator ExecuteResult(float ret) => Return(ret);
        public static implicit operator ExecuteResult(string ret) => Return(ret);
        public static implicit operator ExecuteResult(bool ret) => Return(ret);
        public ExecuteResult(object ret, ExecuteState state)
        {
            this.state = state;
            this.returnValue = ret;
        }
        public static ExecuteResult Return(object ret) => new ExecuteResult() { state = ExecuteState.Finish, returnValue = ret };
    }


    public class VirtualMemory
    {
        private List<object> data = new List<object>();
        public int MemAlloc(int length)
        {
            int addr = data.Count - 1 + 1;
            for(int i = 0; i < length; ++i)
            {
                data.Add(null);
            }
            return addr;
        }
        public object this[int addr]
        {
            get { return data[addr]; }
            set { data[addr] = value; }
        }
    }


    public class Env
    {
        public struct Record
        {
            public string name;
            public int addr;
        }
        private VirtualMemory memory;

        private Dictionary<string, Record> data;

        public Env(VirtualMemory mem)
        {
            this.memory = mem;
        }

        public Record this[string name]
        {
            get
            {
                return data[name];
            }
        }

        public Record NewRecord(string name)
        {
            int newAddr = memory.MemAlloc(1);
            var newrec = new Record() { 
                name = name,
                addr = newAddr
            };
            data[name] = newrec;

            return newrec;
        }
    }


    public class ScriptEngine
    {
        public SyntaxTree tree;

        public Env globalEnv;

        private Stack<Env> envStack;

        private VirtualMemory mem;

        // ********************** EXECUTE ***************************

        public void Execute()
        {
            if (tree == null) throw new Exception("AST Null");

            //mem  
            this.mem = new VirtualMemory();

            //table stack  
            this.globalEnv = new Env(mem);
            this.envStack = new Stack<Env>();
            this.envStack.Push(this.globalEnv);

            //start  
            ExecuteNode(tree.rootNode);
        }

        public ExecuteResult ExecuteNode(SyntaxTree.Node node)
        {
            var type = node.GetType();
            switch(type.Name)
            {
                case "ProgramNode":
                    {
                        ExecuteNode((node as SyntaxTree.ProgramNode).statements);
                        return 0;
                    }
                case "StatementsNode":
                    {
                        foreach(var stmtNode in (node as SyntaxTree.StatementsNode).statements)
                        {
                            ExecuteNode(stmtNode);
                        }
                        return 0;
                    }
                case "StatementBlockNode":
                    {
                        foreach (var stmtNode in (node as SyntaxTree.StatementBlockNode).statements)
                        {
                            ExecuteNode(stmtNode);
                        }
                        return 0;
                    }
                case "VarDeclareNode":
                    {
                        string name = (node as SyntaxTree.VarDeclareNode).identifierNode.token.attribute;
                        object val = ExecuteNode((node as SyntaxTree.VarDeclareNode).initializerNode);
                        DeclareVariable_CurrentEnv(name, val);
                        return 0;
                    }
                case "FuncDeclareNode":
                    {
                        DeclareFunction_CurrentEnv(node as SyntaxTree.FuncDeclareNode);
                        return 0;
                    }
                case "ClassDeclareNode":
                    {
                        DeclareClass_CurrentEnv(
                            (node as SyntaxTree.ClassDeclareNode).classNameNode.token.attribute,
                            (List<SyntaxTree.DeclareNode>)((node as SyntaxTree.ClassDeclareNode).memberDelareNodes)
                            );
                        return 0;
                    }
                case "SingleExprStmtNode":
                    {
                        ExecuteNode((node as SyntaxTree.SingleExprStmtNode).exprNode);
                        return 0;
                    }
                case "BreakStmtNode":
                    {
                        return new ExecuteResult(null, ExecuteState.Break);
                    }
                case "ReturnStmtNode":
                    {
                        return 0;
                    }
                case "WhileStmtNode":
                    {
                        var conditionNode = (node as SyntaxTree.WhileStmtNode).conditionNode;
                        var stmtNode = (node as SyntaxTree.WhileStmtNode).stmtNode;

                        List<SyntaxTree.StmtNode> stmtList;
                        if(stmtNode is SyntaxTree.StatementBlockNode)
                        {
                            stmtList = (stmtNode as SyntaxTree.StatementBlockNode).statements;
                        }
                        else
                        {
                            stmtList = new List<SyntaxTree.StmtNode>() { stmtNode };
                        }

                        while ((bool)(ExecuteNode(conditionNode).returnValue))
                        {
                            bool breakWhile = false;
                            foreach (var stmt in stmtList)
                            {
                                var result = ExecuteNode(stmt);
                                if (result.state == ExecuteState.Break)
                                {
                                    breakWhile = true;
                                    break;
                                }
                            }
                            if (breakWhile)
                            {
                                break;
                            }
                        }

                        return 0;
                    }
                case "ForStmtNode":
                    {
                        var initializer = (node as SyntaxTree.ForStmtNode).initializerNode;
                        var contition = (node as SyntaxTree.ForStmtNode).conditionNode;
                        var iter = (node as SyntaxTree.ForStmtNode).iteratorNode;
                        var stmt = (node as SyntaxTree.ForStmtNode).stmtNode;

                        ExecuteNode(initializer);


                        List<SyntaxTree.StmtNode> stmtList;
                        if (stmt is SyntaxTree.StatementBlockNode)
                        {
                            stmtList = (stmt as SyntaxTree.StatementBlockNode).statements;
                        }
                        else
                        {
                            stmtList = new List<SyntaxTree.StmtNode>() { stmt };
                        }

                        while ((bool)ExecuteNode(contition).returnValue)
                        {
                            bool breakWhile = false;
                            foreach (var s in stmtList)
                            {
                                var result = ExecuteNode(s);
                                if (result.state == ExecuteState.Break)
                                {
                                    breakWhile = true;
                                    break;
                                }
                            }
                            if (breakWhile)
                            {
                                break;
                            }

                            ExecuteNode(iter); 
                        }

                        return 0;
                    }
                case "IfStmtNode":
                    {
                        var conditionClauseList = ((SyntaxTree.IfStmtNode)node).conditionClauseList;
                        var elseClause = ((SyntaxTree.IfStmtNode)node).elseClause;

                        bool anyConidtionValid = false;
                        foreach(var clause in conditionClauseList)
                        {
                            if ((bool)ExecuteNode(clause.conditionNode).returnValue)
                            {
                                anyConidtionValid = true;
                                ExecuteNode(clause.thenNode);
                                break;
                            }
                        }
                        if (anyConidtionValid == false)
                        {
                            ExecuteNode(elseClause.stmt);
                        }

                        return 0;
                    }
                //case "ConditionClauseNode":
                //    {
                //        return 0;
                //    }
                //case "ElseClauseNode":
                //    {
                //        return 0;
                //    }
                case "IdentityNode":
                    {
                        string name = ((SyntaxTree.IdentityNode)node).token.name;
                        return new ExecuteResult(GetIdentidierValue(name), ExecuteState.Finish);
                    }
                case "LiteralNode":
                    {
                        string name = ((SyntaxTree.LiteralNode)node).token.name;
                        return new ExecuteResult(GetLiteralValue(name), ExecuteState.Finish);
                    }
                case "BinaryOpNode":
                    {
                        var vl = ExecuteNode(((SyntaxTree.BinaryOpNode)node).leftNode).returnValue;
                        var vr = ExecuteNode(((SyntaxTree.BinaryOpNode)node).rightNode).returnValue ;
                        var opname = ((SyntaxTree.BinaryOpNode)node).op;
                        return ExecuteResult.Return(BinaryOp(opname, vl, vr));
                    }
                case "UnaryOpNode":
                    {
                        var v = ExecuteNode(((SyntaxTree.UnaryOpNode)node).exprNode).returnValue;
                        string op = ((SyntaxTree.UnaryOpNode)node).op;
                        return ExecuteResult.Return(UnaryOp(op, v));
                    }
                case "AssignNode":
                    {
                        var lNode = ((SyntaxTree.AssignNode)node).lvalueNode;
                        var rNode = ((SyntaxTree.AssignNode)node).rvalueNode;
                        string op = ((SyntaxTree.AssignNode)node).op;
                        return ExecuteResult.Return(Assign(op, lNode, rNode));
                    }
                case "CallNode":
                    {
                        var ret = Call((SyntaxTree.CallNode)node);
                        return ExecuteResult.Return(ret);
                    }
                case "IncDecNode":
                    {
                        return ExecuteResult.Return(IncDec((SyntaxTree.IncDecNode)node));
                    }
                case "NewObjectNode":
                    {
                        return ExecuteResult.Return(NewObject((SyntaxTree.NewObjectNode)node));
                    }

                case "CastNode":
                    {
                        return ExecuteResult.Return(Cast((SyntaxTree.CastNode)node));
                    }
                case "ObjectMemberAccessNode":
                    {
                        return ExecuteResult.Return(MemberAccess((SyntaxTree.ObjectMemberAccessNode)node));
                    }
                case "ThisMemberAccessNode":
                    {
                        return ExecuteResult.Return(MemberAccess((SyntaxTree.ThisMemberAccessNode)node));
                    }
                //case "TypeNode":
                //    {
                        
                //    }
                case "PrimitiveNode":
                    {
                        return ExecuteResult.Return(ConvertCSharpType((SyntaxTree.PrimitiveNode)node));
                    }
                case "ClassTypeNode":
                    {
                        return ExecuteResult.Return(ConvertCSharpType((SyntaxTree.ClassTypeNode)node));
                    }
                //case "ArgumentListNode":
                //    {
                //        return 0;
                //    }
                //case "ParameterListNode":
                //    {
                //        return 0;
                //    }
                //case "ParameterNode":
                //    {
                //        return 0;
                //    }
                default:
                    throw new Exception("UnknownNode！");
            }
        }


        // ********************** METHODS ***************************

        public void DeclareVariable_CurrentEnv(string name, object initValue)
        {
            var rec = envStack.Peek().NewRecord(name);
            mem[rec.addr] = initValue;
        }

        public void DeclareFunction_CurrentEnv(SyntaxTree.FuncDeclareNode node)
        {
            Type type = (Type)(ExecuteNode(node.returnTypeNode).returnValue);
            string name = node.identifierNode.token.attribute;

            var rec = envStack.Peek().NewRecord(name);
            
            //函数作用域环境  
            mem[rec.addr] = new Env(mem);
        }

        public void DeclareClass_CurrentEnv(string name, List<SyntaxTree.DeclareNode> memberDeclares)
        {
            //Env classEnv = new Env(this.mem);
            //envStack.Peek().NewRecord(name, classEnv);
        }

        public object GetIdentidierValue(string varName)
        {
            return null;
        }
        public object GetLiteralValue(string lexeme)
        {
            return null;
        }

        public object BinaryOp(string op, object vl, object vr)
        {
            return null;
        }
        public object UnaryOp(string op, object val)
        {
            return null;
        }

        public object Assign(string op, SyntaxTree.ExprNode lNode, SyntaxTree.ExprNode rNode)
        {
            return null;
        }

        public object Call(SyntaxTree.CallNode callNode)
        {
            return null;
        }
        public object IncDec(SyntaxTree.IncDecNode node)
        {
            return null;
        }

        public object NewObject(SyntaxTree.NewObjectNode node)
        {
            return null;
        }

        public object Cast(SyntaxTree.CastNode node)
        {
            return null;
        }

        public object MemberAccess(SyntaxTree.MemberAccessNode accessNode)
        {
            return null;
        }

        public Type ConvertCSharpType(SyntaxTree.TypeNode typeNode)
        {
            return default;
        }
    }
}
