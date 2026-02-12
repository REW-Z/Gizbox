using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text;

namespace Gizbox
{
    /// <summary>
    /// 模式  
    /// </summary>
    public class TokenPattern
    {
        public string tokenName;

        public string regularExpression;

        public int back;

        public Regex regex;

        public TokenPattern(string tokenName, string regularExpr, int back = 0)
        {
            this.tokenName = tokenName;
            this.regularExpression = regularExpr;
            this.back = back;

            this.regex = new Regex("^" + this.regularExpression + "$");
        }
    }


    public class Scanner
    {
        //Token Patterns  
        public List<TokenPattern> keywords;
        public List<TokenPattern> operators;
        public List<TokenPattern> literals;
        public TokenPattern identifierPattern;
        public TokenPattern whitespace;
        public TokenPattern comment;

        private readonly HashSet<string> typeNameSet = new HashSet<string>(StringComparer.Ordinal);


        public Scanner()
        {
            //词法分析器初始化    
            keywords = new List<TokenPattern>();

            keywords.Add(new TokenPattern("import", "import\\W", 1));
            keywords.Add(new TokenPattern("using", "using\\W", 1));

            keywords.Add(new TokenPattern("namespace", "namespace\\W", 1));
            keywords.Add(new TokenPattern("extern", "extern\\W", 1));
            keywords.Add(new TokenPattern("const", "const\\W", 1));
            keywords.Add(new TokenPattern("operator", "operator\\W", 1));

            keywords.Add(new TokenPattern("own", "own\\W", 1));
            keywords.Add(new TokenPattern("bor", "bor\\W", 1));
            keywords.Add(new TokenPattern("var", "var\\W", 1));
            keywords.Add(new TokenPattern("class", "class\\W", 1));
            keywords.Add(new TokenPattern("void", "void\\W", 1));
            keywords.Add(new TokenPattern("bool", "bool\\W", 1));
            keywords.Add(new TokenPattern("int", "int\\W", 1));
            keywords.Add(new TokenPattern("long", "long\\W", 1));
            keywords.Add(new TokenPattern("float", "float\\W", 1));
            keywords.Add(new TokenPattern("double", "double\\W", 1));
            keywords.Add(new TokenPattern("char", "char\\W", 1));
            keywords.Add(new TokenPattern("string", "string\\W", 1));
            keywords.Add(new TokenPattern("null", "null\\W", 1));

            keywords.Add(new TokenPattern("capture", "capture\\W", 1));
            keywords.Add(new TokenPattern("leak", "leak\\W", 1));
            keywords.Add(new TokenPattern("sizeof", "sizeof\\W", 1));
            keywords.Add(new TokenPattern("typeof", "typeof\\W", 1));
            keywords.Add(new TokenPattern("default", "default\\W", 1));

            keywords.Add(new TokenPattern(",", ",", 0));
            keywords.Add(new TokenPattern(";", ";", 0));
            keywords.Add(new TokenPattern("new", "new\\W", 1));
            keywords.Add(new TokenPattern("delete", "delete\\W", 1));
            keywords.Add(new TokenPattern("while", "while\\W", 1));
            keywords.Add(new TokenPattern("for", "for\\W", 1));
            keywords.Add(new TokenPattern("if", "if\\W", 1));
            keywords.Add(new TokenPattern("else", "else\\W", 1));
            keywords.Add(new TokenPattern("break", "break\\W", 1));
            keywords.Add(new TokenPattern("return", "return\\W", 1));
            keywords.Add(new TokenPattern("this", "this\\W", 1));

            operators = new List<TokenPattern>();

            operators.Add(new TokenPattern("(", "\\("));
            operators.Add(new TokenPattern(")", "\\)"));
            operators.Add(new TokenPattern("[", "\\["));
            operators.Add(new TokenPattern("]", "\\]"));
            operators.Add(new TokenPattern("{", "\\{"));
            operators.Add(new TokenPattern("}", "\\}"));

            operators.Add(new TokenPattern("=", "=[^=]", 1));
            operators.Add(new TokenPattern("+=", "\\+="));
            operators.Add(new TokenPattern("-=", "-="));
            operators.Add(new TokenPattern("*=", "\\*="));
            operators.Add(new TokenPattern("/=", "/="));
            operators.Add(new TokenPattern("%=", "%="));

            operators.Add(new TokenPattern("--", "--"));
            operators.Add(new TokenPattern("++", "\\+\\+"));


            operators.Add(new TokenPattern("==", "==[^=]", 1));
            operators.Add(new TokenPattern("!=", "!="));
            operators.Add(new TokenPattern("<=", "<="));
            operators.Add(new TokenPattern(">=", ">="));
            operators.Add(new TokenPattern(">", ">[^=>]", 1));
            operators.Add(new TokenPattern("<", "<[^=<]", 1));
            operators.Add(new TokenPattern("+", "\\+[^=\\+]", 1));
            operators.Add(new TokenPattern("-", "-[^=-]", 1));
            operators.Add(new TokenPattern("*", "\\*[^=\\*]", 1));
            operators.Add(new TokenPattern("/", "/[^=/]", 1));
            operators.Add(new TokenPattern("%", "%[^=\\%]", 1));

            operators.Add(new TokenPattern("||", "\\|\\|"));
            operators.Add(new TokenPattern("&&", "\\&\\&"));

            operators.Add(new TokenPattern("!", "![^=]", 1));

            operators.Add(new TokenPattern(":", "\\:[^\\:]", 1));

            operators.Add(new TokenPattern("?", "\\?[^=]", 1));

            operators.Add(new TokenPattern(".", "\\.[\\w]", 1));

            literals = new List<TokenPattern>();

            literals.Add(new TokenPattern("LITBOOL", "(true|false)[^a-zA-Z]", 1));
            literals.Add(new TokenPattern("LITINT", "[0-9]+[^\\d\\.Ll]", 1));
            literals.Add(new TokenPattern("LITLONG", "[0-9]+[L|l]\\D", 1));
            literals.Add(new TokenPattern("LITFLOAT", "[0-9]+\\.[0-9]+[F|f]\\D", 1));
            literals.Add(new TokenPattern("LITDOUBLE", "[0-9]+\\.[0-9]+[D|d]\\D", 1));
            literals.Add(new TokenPattern("LITCHAR", "\\\'[^\\\']\\\'[^\\\']", 1));
            literals.Add(new TokenPattern("LITSTRING", "\\\"[^\\\"]*\\\"[^\\\"]", 1));

            identifierPattern = new TokenPattern("ID", "[A-Za-z_][A-Za-z0-9_]*(\\:\\:[A-Za-z_][A-Za-z0-9_]*)*[^A-Za-z0-9_\\:]", 1);

            whitespace = new TokenPattern("space", "[\\n|\\s|\\t]+");

            comment = new TokenPattern("comment", "//.*\\n", 1);
        }

        public List<string> GetTokenNames()
        {
            List<string> results = new List<string>();
            results.AddRange(keywords.Select(p => p.tokenName));
            results.AddRange(operators.Select(p => p.tokenName));
            results.AddRange(literals.Select(p => p.tokenName));
            results.Add(identifierPattern.tokenName);
            results.Add("TYPE_NAME");

            return results;
        }

        public void SetTypeNames(IEnumerable<string> typeNames)
        {
            typeNameSet.Clear();
            if (typeNames == null)
                return;

            foreach (var name in typeNames)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    typeNameSet.Add(name);
            }
        }

        public static bool PatternMatch(string input, TokenPattern pattern)
        {
            return pattern.regex.IsMatch(input);
        }

        public List<Token> Scan(string input)
        {
            StringBuilder strb = new StringBuilder(input);
            strb.Append('\n');
            string source = strb.ToString();

            List<Token> tokens = new List<Token>();

            Log("source:\n" + source);

            //Pointers  
            int lexemBegin = 0;
            int forward = 0;


            //line  
            int currLine = 1;//当前行数  
            int currLineStart = 0;


            Action<int> MovePointer = (offset) =>
            {
                lexemBegin += offset;
                forward = lexemBegin + 1;
            };

            //Scan  
            while (lexemBegin != source.Length)
            {
                if (lexemBegin + (forward - lexemBegin) > source.Length)
                {
                    if(Compiler.enableLogScanner) Log("发现越界：" + (lexemBegin + (forward - lexemBegin)));
                    if (Compiler.enableLogScanner) Log("总长度：" + source.Length);
                    string remainingText = source.Substring(lexemBegin);
                    throw new LexerException(ExceptioName.LexicalAnalysisError, currLine, (lexemBegin - currLineStart), remainingText.Substring(0, System.Math.Min(10, remainingText.Length)), "");
                }


                var seg = source.Substring(lexemBegin, forward - lexemBegin);

                if (Compiler.enableLogScanner) Log("[" + seg + "]" + "(" + lexemBegin + "," + forward + ")");

                //WHITE SPACE  
                if (seg.Length > 0 && PatternMatch(seg, whitespace))
                {
                    if (Compiler.enableLogScanner) Log("\n>>>>> white space. length:" + seg.Length + "\n\n");

                    //识别换行  
                    for (int i = 0; i < seg.Length - whitespace.back; ++i)
                    {
                        if (seg[i] == '\n')
                        {
                            currLine++;
                            currLineStart = lexemBegin + i + 1;
                        }
                    }

                    MovePointer(seg.Length - whitespace.back);
                    continue;
                }

                //COMMENT  
                if(seg.Length > 2 && PatternMatch(seg, comment))
                {
                    MovePointer(seg.Length - comment.back);
                    continue;
                }

                //KEYWORDS
                bool continueRead = false;
                foreach (var kw in keywords)
                {
                    if (PatternMatch(seg, kw))
                    {
                        string keyword = seg.Substring(0, seg.Length - kw.back);
                        if (Compiler.enableLogScanner) Log("\n>>>>> keyword:" + keyword + "\n\n");

                        Token token = new Token(keyword, PatternType.Keyword, null, currLine, lexemBegin - currLineStart, keyword.Length);
                        tokens.Add(token);


                        MovePointer(seg.Length - kw.back);

                        continueRead = true;
                        break;
                    }
                }
                if (continueRead)
                {
                    continue;
                }


                //OPERATORS
                continueRead = false;
                foreach (var op in operators)
                {
                    if (PatternMatch(seg, op))
                    {
                        string opStr = seg.Substring(0, seg.Length - op.back);
                        if (Compiler.enableLogScanner) Log("\n>>>>> operator:" + opStr + "  tokenname:" + op.tokenName + "\n\n");

                        Token token = new Token(opStr, PatternType.Operator, null, currLine, lexemBegin - currLineStart, opStr.Length);
                        tokens.Add(token);

                        MovePointer(seg.Length - op.back);

                        continueRead = true;
                        break;
                    }
                }
                if (continueRead)
                {
                    continue;
                }


                //LIT
                continueRead = false;
                foreach (var lit in literals)
                {
                    if (PatternMatch(seg, lit))
                    {
                        string litstr = seg.Substring(0, seg.Length - lit.back);
                        if (Compiler.enableLogScanner) Log("\n>>>>> literal value:" + lit.tokenName + ":" + litstr + "\n\n");

                        Token token = new Token(lit.tokenName, PatternType.Literal, litstr, currLine, lexemBegin - currLineStart, litstr.Length);
                        tokens.Add(token);

                        MovePointer(seg.Length - lit.back);

                        continueRead = true;
                        break;
                    }
                }
                if (continueRead)
                {
                    continue;
                }


                //ID  
                if (PatternMatch(seg, identifierPattern))
                {
                    string identifierName = seg.Substring(0, seg.Length - identifierPattern.back);
                    if (Compiler.enableLogScanner) Log("\n>>>>> identifier:" + identifierName + "\n\n");

                    //int idx = globalSymbolTable.AddIdentifier(identifierName);//词法分析阶段最好不创建符号表条目（编译原理p53）  
                    string tokenName = typeNameSet.Contains(identifierName) ? "TYPE_NAME" : identifierPattern.tokenName;
                    Token token = new Token(tokenName, PatternType.Id, identifierName, currLine, lexemBegin - currLineStart, identifierName.Length);
                    tokens.Add(token);

                    MovePointer(seg.Length - identifierPattern.back);
                    continue;
                }



                forward++;
            }


            //DEBUG  
            {
                if (Compiler.enableLogScanner) Log("Token列表：");
                foreach(var token in tokens)
                {
                    if (Compiler.enableLogScanner) Log(token.ToString());
                }

                Compiler.Pause("词法单元扫描完毕，共" + tokens.Count + "个...");
            }

            return tokens;
        }


        private static void Log(object content)
        {
            if(!Compiler.enableLogScanner) return;
            GixConsole.WriteLine("Scanner >>>" + content);
        }
    }
}
