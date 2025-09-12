using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections;
using System.Collections.Generic;


namespace Gizbox
{

    /// <summary>
    /// 语法分析器( LL(1) )  
    /// </summary>
    public class LLParser
    {
        /// ****** 注意事项 ******  
        ///
        /// 非终结符的产生式的第一项是非终结符自己，会导致左递归。  
        /// 预测分析法要求FIRST(α)和FIRST(β)不相交。   
        /// 使用null来代表ε。    


        /// **** LL1文法验证 ****  
        ///对于任意A->α|β  
        ///FIRST(α)和FIRST(β)不相交  
        ///如果ε在FIRST(β)中，那么FIRST(α)和FOLLOW(A)是不相交的集合  


        /// *** 简单算术表达式文法 ***     
        /// expr -> expr + term  
        /// expr -> expr - term
        /// expr -> term  
        /// term -> term * factor
        /// term -> term / factor  
        /// term -> factor
        /// factor -> ( expr )
        /// factor -> id  




        //构造函数  
        public LLParser()
        {
            this.lookahead = 0;

            this.parseTree = new SimpleParseTree();
            this.currentNode = parseTree.root;


            //文法初始化  
            InitGrammar();
        }


        //输入和输出  
        public List<Token> input;
        public SimpleParseTree parseTree = null;



        //文法信息  
        private Nonterminal startSymbol = null;//开始符号
        private List<Symbol> symbols = new List<Symbol>();//符号列表   
        private List<Nonterminal> nonterminals = new List<Nonterminal>();//非终结符列表  
        private List<Production> productions = new List<Production>();//产生式列表  




        //状态  
        private int lookahead = 0;
        private Token lookaheadToken => input[lookahead];
        private SimpleParseTree.Node currentNode = null;


        //符号集缓存  
        private Dictionary<string, TerminalSet> cachedFIRSTOfSymbolStr = new Dictionary<string, TerminalSet>();



        /// <summary>
        /// 文法初始化  
        /// </summary>
        private void InitGrammar()
        {
            GixConsole.WriteLine("\n\n上下文无关文法初始化中....");

            ////生成文法符号  
            //startSymbol = NewNonterminal("stmt");
            //NewNonterminal("expr");
            //NewNonterminal("term");
            //NewNonterminal("rest");

            //NewTerminal("var");
            //NewTerminal("id");
            //NewTerminal("num");
            //NewTerminal("=");
            //NewTerminal("+");
            //NewTerminal("-");
            //NewTerminal(";");


            ////生成产生式  
            //NewProduction("stmt -> var id = expr ;");
            //NewProduction("stmt -> id = expr ;");

            //NewProduction("expr -> term rest");

            //NewProduction("term -> id");
            //NewProduction("term -> num");

            //NewProduction("rest -> + term");
            //NewProduction("rest -> - term");
            //NewProduction("rest ->"); //ε



            startSymbol = NewNonterminal("stmt");
            NewNonterminal("expr");
            NewNonterminal("term");
            NewNonterminal("factor");

            NewTerminal("var");
            NewTerminal("id");
            NewTerminal("num");
            NewTerminal("=");
            NewTerminal("+");
            NewTerminal("-");
            NewTerminal("/");
            NewTerminal("*");
            NewTerminal("(");
            NewTerminal(")");
            NewTerminal(";");

            NewProduction("stmt -> var id = expr ;");
            NewProduction("stmt -> id = expr ;");

            NewProduction("expr -> expr + term");
            NewProduction("expr -> expr - term");
            NewProduction("expr -> term");
            NewProduction("term -> term * factor");
            NewProduction("term -> term / factor");
            NewProduction("term -> factor");
            NewProduction("factor -> ( expr )");
            NewProduction("factor -> id");
            NewProduction("factor -> num");


            //检查左递归  
            CheckLeftRecursion();




            //FIRST集计算（从开始符号递归）  
            InitFIRSTCollections();

            //FOLLOW集计算  
            InitFOLLOWCollections();


            //DEBUG  
            foreach (var nont in nonterminals)
            {
                GixConsole.WriteLine("非终结符" + nont.name + "的FIRST集：" + string.Concat(FIRST(nont).ToArray().Select(symb => symb.name + ",")));

                GixConsole.WriteLine("非终结符" + nont.name + "的FOLLOW集：" + string.Concat(FOLLOW(nont).ToArray().Select(symb => symb.name + ",")));

                GixConsole.WriteLine("");

                foreach (var production in nont.productions)
                {
                    GixConsole.WriteLine("     产生式 " + production.ToExpression() + " 的FIRST集：" + string.Concat(FIRST(production.body).ToArray().Select(symb => symb.name + ",")));
                }
            }


            //验证文法    
            ValidLL1();
        }

        /// <summary>
        /// 检查左递归  
        /// </summary>
        private void CheckLeftRecursion()
        {
            bool isLeftRecursive = false;
            for (int i = 0; i < nonterminals.Count; ++i)
            {
                var nt_i = nonterminals[i];

                // (1)
                {
                    for (int j = 0; j < nonterminals.Count; ++j)
                    {
                        var nt_j = nonterminals[i];

                        //（未实现）  
                        //将每个形如Ai -> Ajγ的产生式替换为产生式组Ai -> δ1γ | δ2γ |...| δkγ  
                        //其中Aj -> δ1 | δ2 |...|δk 是所有的Aj产生式    
                    }
                }



                // (2)  (消除立即左递归)  
                {
                    bool isImmediateLRecursive = false;
                    //检测是否左递归  
                    for (int p = 0; p < nt_i.productions.Count; ++p)
                    {
                        var production = nt_i.productions[p];

                        //是立即左递归的  
                        if (production.body.Length > 0 && production.body[0] == nt_i)
                        {
                            isImmediateLRecursive = true;
                            isLeftRecursive = true;
                            break;
                        }
                    }
                    //重新生成  
                    if (isImmediateLRecursive)
                    {
                        var prevProductions = nt_i.productions;
                        nt_i.productions = new List<Production>();

                        //左递归的产生式列表  
                        var leftRecursiveProductions = prevProductions.Where(p => p.body.Length > 0 && p.body[0] == nt_i).ToList();

                        //非左递归的产生式列表
                        var normalProductions = prevProductions.Where(p => (p.body.Length > 0 && p.body[0] == nt_i) == false);

                        var A_quot = NewNonterminal(nt_i.name + "\'");

                        foreach (var p in normalProductions)
                        {
                            //A -> βA'
                            var β = p.body.ToList();
                            var βA_quat = β; β.Add(A_quot);

                            var newProduction = new Production() { head = nt_i, body = βA_quat.ToArray() };

                            nt_i.productions.Add(newProduction);
                            this.productions.Add(newProduction);
                        }

                        A_quot.productions = new List<Production>();
                        foreach (var p in leftRecursiveProductions)
                        {
                            var α = p.body.Skip(1).ToList();
                            var αA_quat = α; αA_quat.Add(A_quot);

                            var newProduction = new Production() { head = A_quot, body = αA_quat.ToArray() };

                            A_quot.productions.Add(newProduction);
                            this.productions.Add(newProduction);
                        }
                        //添加ε产生式  
                        var newproduction = new Production() { head = A_quot, body = new Symbol[] { null } };
                        A_quot.productions.Add(newproduction);
                        this.productions.Add(newproduction);
                    }
                }
            }


            if (isLeftRecursive)
            {
                GixConsole.WriteLine("文法中有左递归，已重新生成文法...");
                GixConsole.WriteLine("重新生成的产生式列表：");
                foreach (var nt in nonterminals)
                {
                    foreach (var p in nt.productions)
                    {
                        GixConsole.WriteLine(p.ToExpression());
                    }
                }
            }



        }

        private void InitFIRSTCollections()
        {
            FIRST(startSymbol);
            foreach (var nt in nonterminals)
            {
                FIRST(nt);
            }
        }

        private void InitFOLLOWCollections()
        {
            //开始符号的FOLLOW先添加结束符号
            FOLLOW(startSymbol).AddDistinct(new Terminal() { name = "$" });

            //遍历产生式
            foreach (var production in productions)
            {
                if (production.IsεProduction())
                {
                    continue;
                }


                for (int i = 0; i < production.body.Length; ++i)
                {
                    //如果【存在A->αBβ】，那么FIRST(β)中除了ε外所有符号都在FOLLOW(B)中  
                    {
                        var α = production.body.Take(i);
                        var B = production.body[i];
                        var β = production.body.Skip(i + 1);

                        if (β.Count() > 0)
                        {
                            FOLLOW(B).UnionWith(FIRST(β), exceptedTerminals: new List<Terminal> { null });//排除ε  
                        }
                    }


                    //如果【存在A->αB】 或者 【A->αBβ(FIRST(B)包含ε)】，那么FOLLOW(A)中所有的符号都在FOLLOW(B)中    
                    {
                        var A = production.head;
                        var α = production.body.Take(i);
                        var B = production.body[i];
                        var β = production.body.Skip(i + 1);

                        if (β.Count() == 0 || FIRST(B).Contains(null))
                        {
                            FOLLOW(B).UnionWith(FOLLOW(A));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 检查LL1文法
        /// </summary>
        /// <returns></returns>
        private void ValidLL1()
        {
            foreach (var nt in this.nonterminals)
            {
                if (nt.productions.Count < 2) continue;

                //对于任意A -> α|β  
                for (int i = 0; i < nt.productions.Count; ++i)
                {
                    for (int j = i + 1; j < nt.productions.Count; ++j)
                    {
                        var A = nt;
                        var α = nt.productions[i].body;
                        var β = nt.productions[j].body;

                        //FIRST(α)和FIRST(β)不相交  
                        if (FIRST(α).Intersect(FIRST(β)))
                        {
                            throw new Exception("文法错误，非LL1文法。(FIRST(" + FormatSymbolStr(α) + ")包含FIRST(" + FormatSymbolStr(β) + "))");
                        }


                        //如果ε在FIRST(β)中，那么FIRST(α)和FOLLOW(A)是不相交的集合  
                        if (FIRST(β).Contains(null))
                        {
                            if (FIRST(α).Intersect(FOLLOW(A)))
                            {
                                throw new Exception("文法错误，非LL1文法。(FIRST(" + FormatSymbolStr(α) + ")包含FOLLOW(" + A.name + "))");
                            }
                        }
                        if (FIRST(α).Contains(null))
                        {
                            if (FIRST(β).Intersect(FOLLOW(A)))
                            {
                                throw new Exception("文法错误，非LL1文法。(FIRST(" + FormatSymbolStr(β) + ")包含FOLLOW(" + A.name + "))");
                            }
                        }
                    }
                }
            }

            GixConsole.WriteLine("\n\nLL1文法验证通过。\n\n");
        }



        private Terminal NewTerminal(string name)
        {
            Terminal terminal = new Terminal() { name = name };

            symbols.Add(terminal);


            return terminal;
        }

        private Nonterminal NewNonterminal(string name)
        {
            Nonterminal nonterminal = new Nonterminal() { name = name, productions = new List<Production>() };

            symbols.Add(nonterminal);
            nonterminals.Add(nonterminal);

            return nonterminal;
        }

        private void NewProduction(string expression)
        {
            string[] segments = expression.Split(' ');
            if (segments.Length < 2) return;
            if (segments[1] != "->") return;

            int bodyLength = segments.Length - 2;

            Nonterminal head = nonterminals.FirstOrDefault(s => s.name == segments[0]);

            if (head.productions == null) head.productions = new List<Production>();


            //是ε产生式  
            if (segments.Length == 2)
            {
                Production newProduction = new Production();
                newProduction.head = head;
                newProduction.body = new Symbol[1];
                head.productions.Add(newProduction);
                productions.Add(newProduction);

                newProduction.body[0] = null;//ε  

            }
            //不是ε产生式  
            else
            {
                Production newProduction = new Production();
                newProduction.head = head;
                newProduction.body = new Symbol[bodyLength];
                head.productions.Add(newProduction);
                productions.Add(newProduction);

                for (int i = 2; i < segments.Length; ++i)
                {
                    Symbol symbol = symbols.FirstOrDefault(s => s.name == segments[i]);
                    newProduction.body[i - 2] = symbol;
                }
            }
        }

        private TerminalSet FIRST(Symbol symbol)
        {
            //无缓存 -> 计算  
            if (symbol.cachedFIRST == null)
            {
                TerminalSet firstCollection = new TerminalSet();

                //终结符
                if (symbol is Terminal)
                {
                    firstCollection.AddDistinct(symbol as Terminal);
                }
                //非终结符
                else
                {
                    Nonterminal nt = symbol as Nonterminal;
                    foreach (var production in nt.productions)
                    {
                        firstCollection.UnionWith(FIRST(production.body));
                    }
                }

                symbol.cachedFIRST = (firstCollection);

                return symbol.cachedFIRST;
            }
            //有缓存 -> 读取缓存
            else
            {
                return symbol.cachedFIRST;
            }
        }

        private TerminalSet FIRST(IEnumerable<Symbol> sstr)
        {
            string key = string.Concat(sstr.Select(s => (s != null ? s.name : "ε") + " "));

            //有缓存 -> 返回缓存  
            if (cachedFIRSTOfSymbolStr.ContainsKey(key))
            {
                return cachedFIRSTOfSymbolStr[key];
            }
            //无缓存 -> 计算并缓存  
            else
            {
                TerminalSet firstCollection = new TerminalSet();
                foreach (var s in sstr)
                {
                    if (s is Nonterminal)
                    {
                        //该产生式中的该非终结符 可以 推导ε
                        if ((s as Nonterminal).HasεProduction() == true)
                        {
                            firstCollection.UnionWith(FIRST(s));

                            continue;//跳到产生式下一个符号  
                        }
                        //该产生式中的该非终结符 不可以 推导ε
                        else
                        {
                            firstCollection.UnionWith(FIRST(s));

                            break;//跳出该产生式  
                        }
                    }
                    else if (s is Terminal)
                    {
                        firstCollection.AddDistinct(s as Terminal);
                        break;//跳出该产生式  
                    }
                }

                //缓存  
                cachedFIRSTOfSymbolStr[key] = firstCollection;


                return firstCollection;
            }


        }

        private TerminalSet FOLLOW(Symbol symbol)
        {
            //无缓存 -> 初始化缓存  （CRE:FOLLOW集无法一次计算完毕）    
            if (symbol.cachedFOLLOW == null)
            {
                symbol.cachedFOLLOW = new TerminalSet();

                return symbol.cachedFOLLOW;
            }
            else
            {
                return symbol.cachedFOLLOW;
            }
        }


        private void ExecuteNonterminal(Nonterminal nonterminal)
        {
            if (nonterminal == null) throw new Exception("要执行的非终结符为空");

            GixConsole.WriteLine("执行非终结符:" + nonterminal.name);

            var stmtNode = new SimpleParseTree.Node() { isLeaf = false, name = nonterminal.name };
            parseTree.AppendNode(currentNode, stmtNode);
            this.currentNode = stmtNode;

            //选择非ε的产生式  
            Production targetProduction = null;
            foreach (var production in nonterminal.productions)
            {
                if (FIRST(production.body).ContainsTerminal(lookaheadToken.name))
                {
                    targetProduction = production;
                    GixConsole.WriteLine("选择产生式：" + targetProduction.ToExpression());
                    break;
                }
            }
            //未找到合适产生式 - 最后考虑ε产生式  
            if (targetProduction == null)
            {
                GixConsole.WriteLine("未找到非ε产生式...考虑ε产生式...");
                if (FOLLOW(nonterminal).ContainsTerminal(lookaheadToken.name))
                {
                    //考虑epsilon产生式  
                    var εProduction = nonterminal.productions.FirstOrDefault(p => p.body.Length == 1 && p.body[0] == null);
                    if (εProduction != null)
                    {
                        GixConsole.WriteLine("存在ε产生式，已选择ε产生式");
                        targetProduction = εProduction;
                    }

                    //更加一般化的条件：能推导出ε产生式也可以(A =*=> ε)  
                    if (targetProduction == null)
                    {
                        foreach (var p in nonterminal.productions)
                        {
                            if (p.CanDeriveε())
                            {
                                targetProduction = p;
                                GixConsole.WriteLine("产生式" + p.ToExpression() + "可以推导出ε，已选择该产生式");
                                break;
                            }
                        }
                    }
                }
                else
                {
                    throw new Exception("syntax error. 找不到对应的非ε产生式，且当前非终结符的FOLLOW集不包含lookahead符号:" + lookaheadToken.name);
                }

            }

            //错误：未找到产生式  
            if (targetProduction == null)
            {
                throw new Exception("syntax error: production of [" + nonterminal.name + "] not found!    lookahead:[" + lookaheadToken.name + "]");
            }

            //执行产生式  
            for (int i = 0; i < targetProduction.body.Length; ++i)
            {
                var symbol = targetProduction.body[i];

                if (symbol == null)//ε  
                {
                    //把ε也加到分析树里?  
                    {
                        var terminalNode = new SimpleParseTree.Node() { isLeaf = true, name = "ε" };
                        parseTree.AppendNode(currentNode, terminalNode);
                    }


                    break;
                }
                else if (symbol is Terminal)
                {
                    Match(symbol.name);
                }
                else
                {
                    ExecuteNonterminal(symbol as Nonterminal);
                }
            }

            this.currentNode = stmtNode.parent;
        }

        public void Match(string terminal)
        {
            if (lookaheadToken.name == terminal)
            {
                var terminalNode = new SimpleParseTree.Node() { isLeaf = true, name = terminal };
                parseTree.AppendNode(currentNode, terminalNode);

                GixConsole.WriteLine("成功匹配:" + terminal);

                lookahead++;
            }
            else
            {
                throw new Exception("syntax error! try match terminal:" + terminal + "  lookahead:" + lookaheadToken.name);
            }
        }

        public void Parse(List<Token> input)
        {
            //输入  
            this.input = input;

            //开始递归向下  
            int stmtCounter = 0;
            while (lookahead <= (input.Count - 1))
            {
                if (++stmtCounter > 99) break;

                //循环执行开始符号  
                ExecuteNonterminal(startSymbol);
            }
        }






        // UTILS  
        private string FormatSymbolStr(IEnumerable<Symbol> symbolStr)
        {
            return string.Concat(symbolStr.Select(s => (s != null ? s.name : "ε")));
        }
    }



}
