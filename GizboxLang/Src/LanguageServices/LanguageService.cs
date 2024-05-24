using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Gizbox;
using Gizbox.LRParse;
using Gizbox.IL;
using Gizbox.Utility;
using Gizbox.SemanticRule;


namespace Gizbox.LanguageServices
{
    public struct HighLightToken
    {
        public int startLine;
        public int startChar;
        public int endLine;
        public int endChar;
        public int kind;
    }
    public class LanguageService
    {
        //分析器数据  
        public Compiler compiler;
        public Scanner scanner;
        public LRParser parser;

        //代码文本数据  
        public StringBuilder sourceB;
        public List<int> lineStartsList;
        public int lineCount = 0;

        //临时数据
        public Gizbox.IL.ILUnit tempUnitCompiled;
        public List<Token> tempTokens;
        public SyntaxTree tempAST;

        //构造函数  
        public LanguageService()
        {
            compiler = new Compiler();
            compiler.AddLibPath(AppDomain.CurrentDomain.BaseDirectory);
            scanner = new Scanner();
            parser = new LRParser(ParserHardcoder.GenerateParser(), compiler);

            this.sourceB = new StringBuilder(); 

            this.lineStartsList = new List<int>();
            this.lineStartsList.Add(0);
        }

        public void Reset()
        {
            this.tempUnitCompiled = null;
            this.tempTokens = null;
            this.tempAST = null;

            sourceB.Clear();

            UpdateLineInfos();
        }

        public void DidOpen(string str)
        {
            this.tempUnitCompiled = null;
            this.tempTokens = null;
            this.tempAST = null;

            UpdateUnit();

            sourceB.Clear();
            sourceB.Append(str);
            UpdateLineInfos();
        }

        public void DidChange(int start_line, int start_char, int end_line, int end_char, string text)
        {
            this.tempUnitCompiled = null;
            this.tempTokens = null;
            this.tempAST = null;

            if (start_line == -1 && end_line == -1)
            {
                sourceB.Clear();
                sourceB.Append(text);
            }
            else
            {
                if (start_line > lineStartsList.Count - 1 || end_line > lineStartsList.Count - 1)
                {
                    throw new Exception("Line Idx Out of Index,当前行：" + end_line + "总行数：" + lineStartsList.Count);
                }

                int start = lineStartsList[start_line] + start_char;
                int end = lineStartsList[end_line] + end_char;

                if (start != end)
                {
                    sourceB.Remove(start, end - start);
                }

                sourceB.Insert(start, text);
            }

            UpdateUnit();

            UpdateLineInfos();
        }

        public string UpdateUnit()
        {
            return TryUpdate();
        }

        public List<HighLightToken> GetHighlights(int line, int character)
        {
            if(this.tempAST == null)
            {
                TryUpdate();
            }

            List<HighLightToken> result = new List<HighLightToken>();
            if (this.tempAST != null)
            {
                foreach (var idNode in this.tempAST.identityNodes)
                {
                    var token = idNode.token;
                    int kind  = 4;
                    if (idNode.identiferType == SyntaxTree.IdentityNode.IdType.Class)
                    {
                        kind = 1;
                    }
                    else if (
                        idNode.identiferType == SyntaxTree.IdentityNode.IdType.VariableOrField ||
                        idNode.identiferType == SyntaxTree.IdentityNode.IdType.FunctionOrMethod
                        )
                    {
                        kind = 2;
                    }


                    if(token.attribute.Contains("::") == false)
                    {
                        result.Add(new HighLightToken()
                        {
                            startLine = token.line,
                            startChar = token.start,
                            endLine = token.line,
                            endChar = token.start + token.attribute.Length,
                            kind = kind
                        });
                    }
                    else
                    {
                        var lastSplit = token.attribute.LastIndexOf(':');
                        result.Add(new HighLightToken()
                        {
                            startLine = token.line,
                            startChar = token.start,
                            endLine = token.line,
                            endChar = token.start + lastSplit + 1,
                            kind = 4
                        });
                        result.Add(new HighLightToken()
                        {
                            startLine = token.line,
                            startChar = token.start + lastSplit + 1,
                            endLine = token.line,
                            endChar = token.start + token.attribute.Length,
                            kind = kind
                        });
                    }
                }

                foreach (var litNode in this.tempAST.literalNodes)
                {
                    var token = litNode.token;
                    if(token.name != "LITSTRING" && token.name != "LITCHAR")
                    {
                        int kind = 3;
                        result.Add(new HighLightToken()
                        {
                            startLine = token.line,
                            startChar = token.start,
                            endLine = token.line,
                            endChar = token.start + token.length,
                            kind = kind
                        });
                    }
                }
            }
            return result;
        }
        public void GetCompletion(int line, int character)
        {
        }



        private string TryUpdate()
        {
            try
            {
                IL.ILUnit unit = new IL.ILUnit();

                //词法分析  
                Scanner scanner = new Scanner();
                List<Token> tokens = scanner.Scan(sourceB.ToString());


                //语法分析  
                LRParse.LRParser parser = new LRParse.LRParser(Gizbox.Utility.ParserHardcoder.GenerateParser(), compiler);
                parser.Parse(tokens);
                var syntaxTree = parser.syntaxTree;


                //语义分析  
                SemanticRule.SemanticAnalyzer semanticAnalyzer = new SemanticRule.SemanticAnalyzer(syntaxTree, unit, compiler);
                semanticAnalyzer.Analysis();

                this.tempUnitCompiled = unit;
                this.tempTokens = tokens;
                this.tempAST = semanticAnalyzer.ast;

                return "success";
            }
            catch (Exception ex) 
            {
                return ex.ToString();
            }
        }

        private void UpdateLineInfos()
        {
            int line = 0;
            lineStartsList[0] = 0;

            for(int i = 0; i  < sourceB.Length; i++) 
            {
                if (sourceB[i] == '\n')
                {
                    line++;

                    if(line > lineStartsList.Count - 10)
                    {
                        ResizeLineIdxArr();
                    }

                    lineStartsList[line] = (i + 1);
                }
            }

            this.lineCount = line + 1;
        }

        private void ResizeLineIdxArr()
        {
            int addcount = lineStartsList.Count;
            for (int i = 0;i < addcount; i++)
            {
                lineStartsList.Add(0);
            }
        }

        public string Current()
        {
            return sourceB.ToString();
        }
    }
}
