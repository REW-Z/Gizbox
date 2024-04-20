using System;
using System.Collections.Generic;
using System.Linq;
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


    public class FanObject
    {
        public string name;
        public Dictionary<string, object> fields = new Dictionary<string, object>();
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
            get 
            {
                return data[addr];
            }
            set 
            {
                if (value.GetType() == typeof(ExecuteResult)) throw new Exception("不应把ExecuteResult存入虚拟内存");
                data[addr] = value; 
            }
        }
    }


    public class Env
    {
        public struct Record
        {
            public string name;
            public int addr;
        }

        public string name;

        private Dictionary<string, Record> data = new Dictionary<string, Record>();

        private VirtualMemory targetMem;


        public Env(string name, VirtualMemory mem)
        {
            this.name = name;
            this.targetMem = mem;
        }

        public bool ContainsKey(string name)
        {
            return data.ContainsKey(name);
        }
        public Record this[string name]
        {
            get
            {
                if (data.ContainsKey(name) == false) throw new Exception("Env中没有" + name + "条目");
                return data[name];
            }
        }


        public Record GetOrNewRecord(string name)
        {
            if(data.ContainsKey("name"))
            {
                return data["name"];
            }
            else
            {
                int newAddr = targetMem.MemAlloc(1);
                var newrec = new Record()
                {
                    name = name,
                    addr = newAddr
                };
                data[name] = newrec;

                return newrec;
            }
        }
        public Record NewRecord(string name)
        {
            if (data.ContainsKey(name)) throw new Exception("重复添加同名条目！:" + name);

            int newAddr = targetMem.MemAlloc(1);
            var newrec = new Record() { 
                name = name,
                addr = newAddr
            };
            data[name] = newrec;

            Console.WriteLine("添加条目:" + name + "指向虚拟内存：" + newAddr);

            return newrec;
        }

        public void Print()
        {
            Console.WriteLine("---------------Table:" + this.name + "---------------");
            Console.WriteLine("NAME" + "\t\t|" + "ADDR" + "\t\t|");
            Console.WriteLine("------------------------------------");
            foreach (var key in data.Keys)
            {
                Console.WriteLine(key + "\t\t|" + data[key].addr + "\t\t|"); 
            }
            Console.WriteLine("------------------------------------");
        }
    }


    public class ScriptEngine
    {
        public SyntaxTree tree;

        public Env globalEnv;

        private FanLang.Stack<Env> envStack;

        private VirtualMemory mem;

        // ********************** EXECUTE ***************************

        public void Execute()
        {
            if (tree == null) throw new Exception("AST Null");

            //mem  
            this.mem = new VirtualMemory();

            //table stack  
            this.globalEnv = new Env("global", mem);
            this.envStack = new FanLang.Stack<Env>();
            this.envStack.Push(this.globalEnv);

            //start  
            ExecuteNode(tree.rootNode);
        }

        public ExecuteResult ExecuteNode(SyntaxTree.Node node)
        {
            Console.WriteLine(" ****** 执行节点：" + node.ToString() + " ****** ");

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
                        object val = ExecuteNode((node as SyntaxTree.VarDeclareNode).initializerNode).returnValue;
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
                        var ret = ExecuteNode((node as SyntaxTree.ReturnStmtNode).returnExprNode).returnValue;
                        return ExecuteResult.Return(ret);
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
                        string name = ((SyntaxTree.IdentityNode)node).token.attribute;
                        return new ExecuteResult(GetIdentidierValue(name), ExecuteState.Finish);
                    }
                case "LiteralNode":
                    {
                        var token = ((SyntaxTree.LiteralNode)node).token;
                        return new ExecuteResult(GetLiteralValue(token), ExecuteState.Finish);
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
            
            //函数环境  
            var newEnv = new Env("function:" + name, mem);
            mem[rec.addr] = newEnv;

            //函数符号表第一条存储函数定义节点  
            int nodeAddr = newEnv.NewRecord("Node").addr;
            mem[nodeAddr] = node;
        }

        public void DeclareClass_CurrentEnv(string name, List<SyntaxTree.DeclareNode> memberDeclares)
        {
            var rec = envStack.Peek().NewRecord(name);
            var newEnv = new Env("class " + name,this.mem);
            mem[rec.addr] = newEnv;

            //类成员作用域  
            envStack.Push(newEnv);

            foreach(var decl in memberDeclares)
            {
                ExecuteNode(decl);
            }

            envStack.Pop();
        }

        public object GetIdentidierAddr(string varName)
        {
            Console.WriteLine("查找名称地址:" + varName);
            for (int i = envStack.Top; i > -1; --i)
            {
                var env = envStack[i];
                if (env.ContainsKey(varName))
                {
                    return env[varName].addr;
                }
            }


            {
                Console.WriteLine("找不到" + varName);
                PrintAllTables();
            }

            return -1;
        }
        public object GetIdentidierValue(string varName)
        {
            int addr = (int)GetIdentidierAddr(varName);
            if (addr == -1) throw new Exception(varName + "地址错误！");

            Console.WriteLine("变量" + varName + "的类型：" + mem[addr].GetType().Name + "  值：" + mem[addr]);
            return mem[addr];
        }
        public object GetLiteralValue(Token token)
        {
            switch(token.name)
            {
                case "LITBOOL":
                    return bool.Parse(token.attribute);
                case "LITINT":
                    return int.Parse(token.attribute);
                case "LITFLOAT":
                    return float.Parse(token.attribute);
                case "LITSTRING":
                    return token.attribute.Substring(1, token.attribute.Length - 2);
                default:
                    return null;
            }
        }

        public object BinaryOp(string op, object vl, object vr)
        {
            return Calculator.CalBinary(op, vl, vr);
        }
        public object UnaryOp(string op, object val)
        {
            switch(op)
            {
                case "!":
                    {
                        if (val is bool)
                        {
                            return !((bool)val);
                        }
                    }
                    break;
                case "-":
                    {
                        return Calculator.CalNegtive(val);
                    }
                    break;
            }
            return null;
        }

        public object Assign(string op, SyntaxTree.ExprNode lNode, SyntaxTree.ExprNode rNode)
        {
            if(lNode is SyntaxTree.IdentityNode)
            {
                var lexeme = (lNode as SyntaxTree.IdentityNode).token.attribute;
                int addr = (int)GetIdentidierAddr(lexeme);

                AssignAddr(op, addr, ExecuteNode(rNode).returnValue);
            }
            else if(lNode is SyntaxTree.MemberAccessNode)
            {
                if(lNode is SyntaxTree.ObjectMemberAccessNode)
                {

                }
                else if(lNode is SyntaxTree.ThisMemberAccessNode)
                {
                }
                throw new Exception("未实现");
            }
            else
            {
                throw new Exception("只能给左值赋值！");
            }

            return null;
        }
        public object AssignAddr(string op, int addr, object rval)
        {
            switch (op)
            {
                case "=":
                    {
                        mem[addr] = rval;
                        return mem[addr];
                    }
                case "+=":
                    {
                        mem[addr] = Calculator.CalBinary("+", mem[addr], rval);
                        return mem[addr];
                    }
                case "-=":
                    {
                        mem[addr] = Calculator.CalBinary("-", mem[addr], rval);
                        return mem[addr];
                    }
                case "*=":
                    {
                        mem[addr] = Calculator.CalBinary("*", mem[addr], rval);
                        return mem[addr];
                    }
                case "/=":
                    {
                        mem[addr] = Calculator.CalBinary("/", mem[addr], rval);
                        return mem[addr];
                    }
                case "%=":
                    {
                        mem[addr] = Calculator.CalBinary("%", mem[addr], rval);
                        return mem[addr];
                    }
                default:
                    {
                        return mem[addr];
                    }
            }
        }

        public object IncDec(SyntaxTree.IncDecNode node)
        {
            var token = node.identifierNode.token;
            int addr = (int)GetIdentidierAddr(token.attribute);

            if(node.isFront)
            {
                switch (node.op)
                {
                    case "++":
                        {
                            var newvalue = Calculator.CalBinary("+", mem[addr], 1);
                            mem[addr] = newvalue;
                            return mem[addr];
                        }
                    case "--":
                        {
                            var newvalue = Calculator.CalBinary("-", mem[addr], 1);
                            mem[addr] = newvalue;
                            return mem[addr];
                        }
                }
            }
            else
            {
                switch (node.op)
                {
                    case "++":
                        {
                            var prevValue = mem[addr];
                            var newvalue = Calculator.CalBinary("+", mem[addr], 1);
                            mem[addr] = newvalue;
                            return prevValue;
                        }
                    case "--":
                        {
                            var prevValue = mem[addr];
                            var newvalue = Calculator.CalBinary("-", mem[addr], 1);
                            mem[addr] = newvalue;
                            return prevValue;
                        }
                }
            }

            throw new Exception("op：" + node.op + " err");
        }

        public object NewObject(SyntaxTree.NewObjectNode node)
        {
            string className = node.className.token.attribute;
            FanObject obj = new FanObject() { name = className };

            Console.WriteLine("创建了类型为" + className + "的新对象");

            return obj;
        }

        public object Cast(SyntaxTree.CastNode node)
        {
            Type t = (Type)ExecuteNode(node.typeNode).returnValue;

            return System.Convert.ChangeType(ExecuteNode(node.factorNode).returnValue, t);
        }

        public object MemberAccess(SyntaxTree.MemberAccessNode accessNode)
        {
            if(accessNode is SyntaxTree.ObjectMemberAccessNode)
            {
            }
            else if(accessNode is SyntaxTree.ThisMemberAccessNode)
            {
            }

            throw new Exception("未实现");
        }

        public Type ConvertCSharpType(SyntaxTree.TypeNode typeNode)
        {
            if(typeNode is SyntaxTree.PrimitiveNode)
            {
                var primitive = (typeNode as SyntaxTree.PrimitiveNode).token.name;
                switch (primitive)
                {
                    case "void": return typeof(void);
                    case "bool": return typeof(bool);
                    case "int": return typeof(int);
                    case "float": return typeof(float);
                    case "string": return typeof(string);
                    default: throw new Exception("类型错误");
                }
            }
            else if(typeNode is SyntaxTree.ClassTypeNode)
            {
                return typeof(FanObject);
            }
            return default;
        }

        public object Call(SyntaxTree.CallNode callNode)
        {
            if(callNode.isMemberAccessFunction == false)
            {
                string funcName = (callNode.funcNode as SyntaxTree.IdentityNode).token.attribute;

                //实参列表    
                List<SyntaxTree.ExprNode> argNodes = callNode.argumantsNode.arguments;
                List<object> args = argNodes.Select(n => ExecuteNode(n).returnValue).ToList();


                //是外部调用  
                if ((int)GetIdentidierAddr(funcName) == -1)
                {
                    Console.WriteLine("外部调用：" + funcName);
                    PrintAllTables();
                    return ExternCall(funcName, args);
                }

                //进入函数作用域  
                int addr = (int)GetIdentidierAddr(funcName);
                Env funEnv = (Env)mem[addr];
                envStack.Push(funEnv);

                //返回值  
                object ret = null;


                //DEBUG  
                if (funEnv.ContainsKey("Node") == false)
                {
                    Console.WriteLine("未找到node");
                    PrintAllTables();
                }

                //传参（形参初始化）  
                var declNode = (SyntaxTree.FuncDeclareNode)(mem[funEnv["Node"].addr]);
                if (args.Count != declNode.parametersNode.parameterNodes.Count) throw new Exception("参数个数不匹配！");
                for (int i = 0; i < declNode.parametersNode.parameterNodes.Count; ++i) 
                {
                    var paramNode = declNode.parametersNode.parameterNodes[i];
                    string paramName = paramNode.identifierNode.token.attribute;
                    var rec = envStack.Peek().GetOrNewRecord(paramName);
                    mem[rec.addr] = args[i];
                }

                //执行语句  
                foreach(var stmt in declNode.statementsNode.statements)
                {
                    var result = ExecuteNode(stmt);
                    if(stmt is SyntaxTree.ReturnStmtNode)
                    {
                        ret = result.returnValue;
                        Console.WriteLine("返回值：" + ret);
                        break;
                    }
                }



                //离开函数作用域  
                envStack.Pop();

                //返回  
                return ret;
            }
            else
            {
            }

            throw new Exception("调用错误！");
        }

        public object ExternCall(string name, List<object> args)
        {
            switch(name)
            {
                case "print":
                    {
                        Console.WriteLine("fan:" + args[0]);
                    }
                    break;
            }
            return null;
        }



        // ------------- DEBUG -----------------
        private void PrintAllTables()
        {
            for (int i = envStack.Top; i > -1; --i)
            {
                var env = envStack[i];
                env.Print();
            }
        }

    }

    public class Calculator
    {
        private static bool IsNumberType(Type type)
        {
            if (type == typeof(int) || type == typeof(float))
                return true;

            return false;
        }
        public static object CalNegtive(object v)
        {
            Type t = v.GetType();
            if (IsNumberType(t))
            {
                if (t == typeof(float))
                {
                    return -(float)v;
                }
                else if (t == typeof(int))
                {
                    return -(int)v;
                }
            }
            return null;
        }
        public static object CalBinary(string op, object v1, object v2)
        {
            Type t1 = v1.GetType();
            Type t2 = v2.GetType();

            if (IsNumberType(t1) && IsNumberType(t2))
            {
                if (t1 == typeof(float))
                {
                    if(t2 == typeof(float))
                    {
                        return CalBinary(op, (float)v1 , (float)v2);
                    }
                    else if(t2 == typeof(int))
                    {
                        return CalBinary(op, (float)v1 , (int)v2);
                    }
                }
                else if(t1 == typeof(int))
                {
                    if (t2 == typeof(float))
                    {
                        return CalBinary(op, (int)v1, (float)v2);
                    }
                    else if (t2 == typeof(int))
                    {
                        return CalBinary(op, (int)v1, (int)v2);
                    }
                }
            }
            else if ((t1 == typeof(string) || t2 == typeof(string)) && op == "+")
            {
                return v1.ToString() + v2.ToString();
            }
            return null;
        }
        public static object CalBinary(string op, int v1, int v2)
        {
            switch(op)
            {
                case "+": return v1 + v2;
                case "-": return v1 - v2;
                case "*": return v1 * v2;
                case "/": return v1 / v2;
                case "%": return v1 % v2;
                default: return null;
            }
        }
        public static object CalBinary(string op, float v1, int v2)
        {
            switch (op)
            {
                case "+": return v1 + v2;
                case "-": return v1 - v2;
                case "*": return v1 * v2;
                case "/": return v1 / v2;
                case "%": return v1 % v2;
                default: return null;
            }
        }
        public static object CalBinary(string op, int v1, float v2)
        {
            switch (op)
            {
                case "+": return v1 + v2;
                case "-": return v1 - v2;
                case "*": return v1 * v2;
                case "/": return v1 / v2;
                case "%": return v1 % v2;
                default: return null;
            }
        }
        public static object CalBinary(string op, float v1, float v2)
        {
            switch (op)
            {
                case "+": return v1 + v2;
                case "-": return v1 - v2;
                case "*": return v1 * v2;
                case "/": return v1 / v2;
                case "%": return v1 % v2;
                default: return null;
            }
        }
    }
}
