using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Gizbox;
using Gizbox.LRParse;
using Gizbox.LALRGenerator;
using Gizbox.SemanticRule;
using Gizbox.IL;
using System.Runtime.CompilerServices;
using static Gizbox.SyntaxTree;


namespace Gizbox.LRParse
{
    /// <summary>
    /// 分析栈  
    /// </summary>
    public class ParseStack
    {
        private List<ParseStackElement> data = new List<ParseStackElement>();

        public int Count => data.Count;

        public int Top => data.Count - 1;

        public ParseStackElement this[int idx]
        {
            get { return data[idx]; }
            set { data[idx] = value; }
        }

        public ParseStackElement Peek()
        {
            return data[Top];
        }

        public void Push(ParseStackElement ele)
        {
            data.Add(ele);
        }

        public ParseStackElement Pop()
        {
            var result = data[Top];
            data.RemoveAt(Top);
            return result;
        }

        public List<ParseStackElement> ToList()
        {
            return data;
        }
    }

    /// <summary>
    /// 分析栈元素（状态和其他信息）    
    /// </summary>
    public class ParseStackElement
    {
        public State state;

        public Dictionary<string, object> attributes;

        public ParseStackElement(State state)
        {
            this.state = state;

            this.attributes = new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// LR分析器    
    /// </summary>
    public class LRParser
    {
        //上下文  
        public Compiler compilerContext;


        //输出  
        public ParseTree parseTree;
        public SyntaxTree syntaxTree;

        //语法分析器信息（由生成器生成）    
        public ParserData data;


        //状态机  
        public Queue<Token> remainingInput;//剩余输入    
        public ParseStack stack;//分析栈  
        public ParseStackElement newElement;//待入栈的产生式头元素    


        /// <summary>
        /// 构造函数  
        /// </summary>
        public LRParser(ParserData data, Compiler context)
        {
            this.data = data;
            this.compilerContext = context;
        }

        /// <summary>
        /// 规约时记录起始终止Token  
        /// </summary>
        private void RecordStartEndToken(ParseStackElement newelement, int βLength)
        {
            if(βLength == 0) return;   

            //记录到分析栈元素attribute
            for(int i = stack.Count - βLength; i <= stack.Count - 1; ++i)
            {
                if(stack[i].attributes.ContainsKey("start") && stack[i].attributes["start"] != null)
                {
                    newelement.attributes["start"] = stack[i].attributes["start"];
                    break;
                }
            }
            for(int i = stack.Count - 1; i >= stack.Count - βLength; --i)
            {
                if(stack[i].attributes.ContainsKey("end") && stack[i].attributes["end"] != null)
                {
                    newelement.attributes["end"] = stack[i].attributes["end"];
                    break;
                }
            }

            //记录到AST节点attribute    
            if(newElement.attributes.ContainsKey("ast_node"))
            {
                var astNode = ((SyntaxTree.Node)newElement.attributes["ast_node"]);
                if(astNode.attributes == null) astNode.attributes = new Dictionary<string, object>();
                astNode.attributes["start"] = newelement.attributes["start"];
                astNode.attributes["end"] = newelement.attributes["end"];
            }
        }

        /// <summary>
        /// 语法分析  
        /// </summary>
        public void Parse(List<Token> input)
        {
            // *** 设置输入 ***  
            {
                //剩余输入队列
                this.remainingInput = new Queue<Token>();
                foreach (var token in input)
                {
                    this.remainingInput.Enqueue(token);
                }
                //添加$符号  
                if (input.LastOrDefault().name != "$")
                {
                    this.remainingInput.Enqueue(new Token("$", PatternType.Keyword, null, -99, 0, 0));
                }
            }




            //语义动作执行器  
            SematicActionExecutor sematicActionExecutor = new SematicActionExecutor(this);

            //初始状态入栈  
            stack = new ParseStack();
            var initState = new ParseStackElement(data.lalrStates[0]);
            stack.Push(initState);

            //自动机运行  
            while (remainingInput.Count > 0)
            {
                Log("剩余输入:" + string.Concat(remainingInput.ToArray().Select(t => t.name)));

                //当前此法单元  
                var currentToken = remainingInput.Peek();

                //查找ACTION表  
                Log("查询：ACTION[" + stack.Peek().state.idx + "," + currentToken.name + "]");
                var action = data.table.ACTION(stack.Peek().state.idx, currentToken.name);

                //ACTION语法分析动作    
                switch (action.type)
                {
                    //移入  
                    case ACTION_TYPE.Shift:
                        {
                            Log("移入状态" + action.num + "");

                            var token = remainingInput.Dequeue();

                            // *** 移入 ***  
                            var stateToPush = data.lalrStates[action.num];
                            var newEle = new ParseStackElement(stateToPush);


                            // *** 记录Token信息 ***  
                            newEle.attributes["token"] = token;
                            newEle.attributes["start"] = token;
                            newEle.attributes["end"] = token;

                            stack.Push(newEle);
                            // ************  

                            Log("当前栈状态：" + string.Concat(stack.ToList().Select(s => "\n" + s.state.idx + ": \"" + s.state.name + "\"")) + "\n");
                        }
                        break;
                    //规约    
                    case ACTION_TYPE.Reduce:
                        {
                            Log("按产生式：" + data.productions[action.num].ToExpression() + "规约");

                            // *** 确定产生式 ***  
                            var production = data.productions[action.num];
                            // ******************

                            // *** 确定要出栈的状态数量 ***  
                            var βLength = production.body.Length;
                            // ************************

                            // *** 确定产生式头对应状态 ***  
                            var goTo = data.table.GOTO(stack[stack.Top - βLength].state.idx, production.head.name);
                            this.newElement = new ParseStackElement(data.lalrStates[goTo.stateId]);
                            // ************************


                            // *** 执行语义动作 ***    
                            //语义动作执行在物理出栈和入栈之前  
                            sematicActionExecutor.ExecuteSemanticAction(production);
                            // ********************


                            // *** 记录新Element起始结束Token ***    
                            RecordStartEndToken(this.newElement, βLength);
                            // ********************


                            // *** 产生式体出栈 ***  
                            for(int i = 0; i < βLength; ++i)
                            {
                                stack.Pop();
                            }
                            Log(βLength + "个状态出栈");
                            // *********************


                            // *** 产生式头入栈 ***  
                            stack.Push(this.newElement);
                            this.newElement = null;
                            Log(goTo.stateId + "状态入栈");
                            // *********************

                            Log("当前栈状态：" + string.Concat(stack.ToList().Select(s => "\n" + s.state.idx + ": \"" + s.state.name + "\"")) + "\n");
                        }
                        break;
                    //接受  
                    case ACTION_TYPE.Accept:
                        {
                            Compiler.Pause("\n语法分析器已Accept");

                            var lastToken = remainingInput.Dequeue();

                            if (lastToken.name == "$" && remainingInput.Count == 0)
                            {
                                Log("语法分析成功完成！");

                                Log("当前栈状态：" + string.Concat(stack.ToList().Select(s => "\n" + s.state.idx + ": \"" + s.state.name + "\"")) + "\n");

                                this.parseTree = sematicActionExecutor.parseTreeBuilder.resultTree;
                                this.syntaxTree = new SyntaxTree(sematicActionExecutor.syntaxRootNode);
                                return;
                            }
                            else
                            {
                                throw new ParseException(ExceptioName.SyntaxAnalysisError, lastToken, "accept err");
                            }
                        }
                    //报错  
                    case ACTION_TYPE.Error:
                        throw new ParseException(ExceptioName.SyntaxAnalysisError, remainingInput.Peek(), "error action. line: " + remainingInput.Peek().line + "\ncurrent symbol :" + currentToken.name + "\ncurrent state:\n" + stack.Peek().state.set.ToExpression());

                }
            }


            Log("\n\n语法分析树：");
            Log(this.parseTree.Serialize());

            Log("\n\n抽象语法树：");
            Log(this.syntaxTree.Serialize());
            Compiler.Pause("抽象语法树生成完成");
        }

        private static void Log(object content)
        {
            if (!Compiler.enableLogParser) return;
            GixConsole.LogLine("Parser >>>" + content);
        }
    }
}


