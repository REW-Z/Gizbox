using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Linq;
using Gizbox;
using Gizbox.LRParse;
using Gizbox.IL;
using Gizbox.Utility;
using Gizbox.SemanticRule;
using System.Runtime.CompilerServices;


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

    /// <summary>
    /// From VSCode Documention
    /// </summary>
    public enum ComletionKind
    {
        Text = 0,
        Method = 1,
        Function = 2,
        Constructor = 3,
        Field = 4,
        Variable = 5,
        Class = 6,
        Interface = 7,
        Module = 8,
        Property = 9,
        Unit = 10,
        Value = 11,
        Enum = 12,
        Keyword = 13,
        Snippet = 14,
        Color = 15,
        Reference = 17,
        File = 16,
        Folder = 18,
        EnumMember = 19,
        Constant = 20,
        Struct = 21,
        Event = 22,
        Operator = 23,
        TypeParameter = 24,
        User = 25,
        Issue = 26,
    }
    public struct Completion
    {
        public string label;
        public ComletionKind kind;
        public string detail;
        public string documentation;
        public string insertText;
    }

    public class DiagnosticInfo
    {
        public string code;

        public int startLine;
        public int startChar;
        public int endLine;
        public int endChar;
        public string message;
        public int severity;
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

        public DiagnosticInfo tempDiagnosticInfo = null;

        public SyntaxTree persistentAST;
        public List<SymbolTable> persistentGlobalEnvs;


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

        //重置  
        public void Reset()
        {
            this.tempUnitCompiled = null;
            this.tempTokens = null;
            this.tempAST = null;

            this.tempDiagnosticInfo = null;

            this.persistentAST = null;
            this.persistentGlobalEnvs = null;

            sourceB.Clear();

            UpdateLineInfos();
        }

        //设置工作目录  
        public void SetWorkFolder(string dir)
        {
            compiler?.AddLibPath(dir);
        }


        public void DidOpen(string str)
        {
            this.tempUnitCompiled = null;
            this.tempTokens = null;
            this.tempAST = null;
            this.tempDiagnosticInfo = null;


            sourceB.Clear();
            sourceB.Append(str);

            UpdateUnit();

            UpdateLineInfos();
        }

        public void DidChange(int start_line, int start_char, int end_line, int end_char, string text)
        {
            this.tempUnitCompiled = null;
            this.tempTokens = null;
            this.tempAST = null;
            this.tempDiagnosticInfo = null;

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

        public List<HighLightToken> GetHighlights(int line, int character)//参数未使用  
        {
            if (this.tempAST == null)
            {
                TryUpdate();
            }

            List<HighLightToken> result = new List<HighLightToken>();
            if (this.tempAST != null)
            {
                foreach (var idNode in this.tempAST.identityNodes)
                {
                    var token = idNode.token;
                    int kind = 4;
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


                    if (token.attribute.Contains("::") == false)
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
                    if (token.name != "LITSTRING" && token.name != "LITCHAR")
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

        public List<Completion> GetCompletion(int line, int character)
        {
            if(line > lineStartsList.Count - 1)
            {
                return new List<Completion>{
                    new Completion(){
                        label = $"DEBUG_ERR_LINE_OUT_OF_INDEX:line{line}",
                        kind = ComletionKind.Class,
                        detail = "",
                        documentation = "",
                        insertText = ""
                    }};
            }

            int curr = lineStartsList[line] + character;
            if(curr <= 1 || curr > sourceB.Length)
            {
                //注意：光标没有对应的字符（越界光标）  
                return new List<Completion>{
                    new Completion(){
                        label = $"DEBUG_ERR_CURR_OUT_OF_INDEX:line{line} character:{character}",
                        kind = ComletionKind.Class,
                        detail = "",
                        documentation = "",
                        insertText = ""
                    }};
            }
                

            List<Completion> result = new List<Completion>();

            //标识符边界(包含空格)
            char wordedgeChar = ' ';
            int wordedgeIdx = curr - 1;
            //分割边界  
            char splitChar = ' ';
            int splitCharIdx = curr - 1;

            for(int i = curr - 1; i > 0; --i)
            {
                char c = sourceB[i];
                if(Utils_IsWordedgeChar(c))
                {
                    wordedgeChar = c;
                    wordedgeIdx = i;
                    break;
                }
            }
            for (int i = curr - 1; i > 0; --i)
            {
                char c = sourceB[i];
                if (Utils_IsSplitChar(c))
                {
                    splitChar = c;
                    splitCharIdx = i;
                    break;
                }
            }


            //DEBUG  
            result.Add(new Completion()
            {
                label = "DEBUG_SHOW_WORD_EDGE:" + Utils_CharToPrintFormat(wordedgeChar),
                kind = ComletionKind.Class,
                detail = "",
                documentation = "",
                insertText = "DEBUG_SHOW_WORD_EDGE:" + Utils_CharToPrintFormat(wordedgeChar),
            });
            result.Add(new Completion()
            {
                label = "DEBUG_SHOW_SPLIT:" + Utils_CharToPrintFormat(splitChar),
                kind = ComletionKind.Class,
                detail = "",
                documentation = "",
                insertText = "DEBUG_SHOW_SPLIT:" + Utils_CharToPrintFormat(splitChar),
            });

            string errMsg = "";
            var currEnv = GetCurrEnv(line, character, ref errMsg);
            if(currEnv != null)
            {
                result.Add(new Completion()
                {
                    label = "DEBUG_SHOW_ENV:" + currEnv.name,
                    kind = ComletionKind.Class,
                    detail = "",
                    documentation = "",
                    insertText = "DEBUG_SHOW_ENV:" + currEnv.name,
                });
            }
            else
            {
                result.Add(new Completion()
                {
                    label = "DEBUG_SHOW_ENV:" + "(no env!:"  + errMsg + ")",
                    kind = ComletionKind.Class,
                    detail = "",
                    documentation = "",
                    insertText = "DEBUG_SHOW_ENV:" + "(no env!:" + errMsg + ")"
                });
            }
            result.Add(new Completion()
            {
                label = "DEBUG_GLOABL_ENV_COUNT:" + "(" + ((persistentGlobalEnvs != null) ? persistentGlobalEnvs.Count : "null") + ") rec-counts:" + (string.Concat(persistentGlobalEnvs.Select(e => e.records.Values.Count + ", "))),
                kind = ComletionKind.Class,
                detail = "",
                documentation = "",
                insertText = "DEBUG_GLOABL_ENV_COUNT:" + "(" + ((persistentGlobalEnvs != null) ? persistentGlobalEnvs.Count : "null") + ") rec-counts:" + (string.Concat(persistentGlobalEnvs.Select(e => e.records.Values.Count + ", ")))
            });


            // *** 类名/变量名自动提示 ***   
            if(splitChar != '.')
            {
                if(persistentGlobalEnvs != null && persistentAST != null)
                {
                    char[] chararr = new char[curr - wordedgeIdx - 1];
                    sourceB.CopyTo(wordedgeIdx + 1, chararr, 0, curr - wordedgeIdx - 1);
                    string prefix = new string(chararr);

                    result.Add(new Completion()
                    {
                        label = "DEBUG_GLOABL_PREFIX:" + prefix,
                        kind = ComletionKind.Class,
                        detail = "",
                        documentation = "",
                        insertText = "DEBUG_GLOABL_PREFIX:" + prefix,
                    });

                    //从符号表链收集  
                    TraversalEnvChain(currEnv, curre => CollectCompletionInEnv(curre, prefix, result));
                }
            }
            // *** 成员自动提示 ***   
            else if(splitChar == '.' && persistentAST != null)
            {
                int objNameEnd = wordedgeIdx;
                int objNameStart = wordedgeIdx;
                for(int i = wordedgeIdx - 1; i > 0; --i)
                {
                    if(Utils_IsPartOfIdentifier(sourceB[i]) == false)
                    {
                        objNameStart = i + 1;
                        break;
                    }
                }

                int length = objNameEnd - objNameStart;
                if(length > 0)
                {
                    char[] objNameBuffer = new char[length];
                    sourceB.CopyTo(objNameStart, objNameBuffer, 0, length);
                    string objName = new string(objNameBuffer);



                    result.Add(new Completion()
                    {
                        label = $"DEBUG_SHOW_OBJNAME:{objName}",
                        kind = ComletionKind.Text,
                        detail = "",
                        documentation = "",
                        insertText = "DEBUG_OBJNAME"
                    });


                    //从符号表链对象名  
                    SymbolTable.Record idRec = null;
                    SymbolTable.Record typeRec = null;
                    
                    //查找对象Record  
                    TraversalEnvChain(currEnv, curre =>  FindIdentifierRecInEnv(curre, objName, out idRec));


                    //查找对象类型Record
                    if(idRec != null)
                    {
                        string classname = idRec.typeExpression;
                        TraversalEnvChain(currEnv, curre => FindTypeRecInEnv(curre, classname, out typeRec));
                    }
                    if(typeRec != null && typeRec.envPtr != null)
                    {
                        var members = typeRec.envPtr.records.Values;
                        foreach(var member in members)
                        {
                            if(member.category == SymbolTable.RecordCatagory.Variable || member.category == SymbolTable.RecordCatagory.Param)
                            {
                                result.Add(new Completion()
                                {
                                    label = member.rawname,
                                    kind = ComletionKind.Field,
                                    detail = $"{member.typeExpression} {member.rawname}",
                                    documentation = "",
                                    insertText = member.rawname,
                                });
                            }
                            else if(member.category == SymbolTable.RecordCatagory.Function)
                            {
                                string paramStr = Utils_ConcatWithComma(
                                    member.envPtr.records.Values
                                    .Where(r => r.category == SymbolTable.RecordCatagory.Param)
                                    .Select(p => p.typeExpression + " " + p.name)
                                    );

                                result.Add(new Completion()
                                {
                                    label = $"{member.rawname}({paramStr})",
                                    kind = ComletionKind.Method,
                                    detail = $"{member.rawname}({paramStr})",
                                    documentation = "",
                                    insertText = member.rawname,
                                });
                            }
                        }


                        result.Add(new Completion()
                        {
                            label = "DEBUG_OBJ_TYPE_MEMBRES:" + string.Concat(typeRec.envPtr.records.Values.Select(r => r.name + ", ")),
                            kind = ComletionKind.Method,
                            detail = "obj:(" + objName + ")len(" + objName.Length + ")",
                            documentation = "",
                            insertText = "DEBUG_OBJ_TYPE_MEMBRES???:" + string.Concat(typeRec.envPtr.records.Values.Select(r => r.name + ", ")),
                        });
                    }
                    else
                    {
                        if(idRec == null)
                        {
                            result.Add(new Completion()
                            {
                                label = "DEBUG_OBJ_IDENTIFIER_NOT_FIND:" + objName,
                                kind = ComletionKind.Method,
                                detail = "obj:(" + objName + ")len(" + objName.Length + ")",
                                documentation = "",
                                insertText = "DEBUG_OBJ_IDENTIFIER_NOT_FIND:" + objName,
                            });
                        }
                        else
                        {
                            if(typeRec == null)
                            {
                                result.Add(new Completion()
                                {
                                    label = "DEBUG_OBJ_TYPE_NOT_FIND:" + idRec.typeExpression,
                                    kind = ComletionKind.Method,
                                    detail = "obj:(" + objName + ")len(" + objName.Length + ")",
                                    documentation = "",
                                    insertText = "DEBUG_OBJ_TYPE_NOT_FIND:" + idRec.typeExpression,
                                });
                            }
                            else
                            {
                                if(typeRec.envPtr == null)
                                {
                                    result.Add(new Completion()
                                    {
                                        label = "DEBUG_OBJ_TYPE_ENVPTR_NULL:" + idRec.typeExpression,
                                        kind = ComletionKind.Method,
                                        detail = "obj:(" + objName + ")len(" + objName.Length + ")",
                                        documentation = "",
                                        insertText = "DEBUG_OBJ_TYPE_ENVPTR_NULL:" + idRec.typeExpression,
                                    });
                                }
                            }
                        }
                    }


                    //...
                }
            }

            return result;
        }

        private void TraversalEnvChain(SymbolTable fromEnv, Func<SymbolTable, bool> action)
        {
            //从局部符号表链收集  
            var curre = fromEnv;

            int loopCounter = 0;
            while(curre != null)
            {
                if (loopCounter++ > 99) throw new Exception("Infinite Loop !");

                if(curre.tableCatagory == SymbolTable.TableCatagory.GlobalScope)
                    break;

                bool end = action(curre);
                if(end) return;

                curre = curre.parent;
            }


            //从全局作用域收集  
            if(persistentGlobalEnvs != null)
            {
                foreach(var env in persistentGlobalEnvs)
                {
                    bool end = action(env);
                    if(end) return;
                }
            }
        }
        private bool FindIdentifierRecInEnv(SymbolTable env, string objName, out SymbolTable.Record rec)
        {
            if(env.ContainRecordRawName(objName))
            {
                rec = env.GetRecord(objName);
                return true;
            }

            rec = null;
            return false;
        }
        private bool FindTypeRecInEnv(SymbolTable env, string typeExpression, out SymbolTable.Record rec)
        {
            if(env.ContainRecordName(typeExpression))
            {
                rec = env.GetRecord(typeExpression);
                return true;
            }

            rec = null;
            return false;
        }
        private bool CollectCompletionInEnv(SymbolTable env, string prefix, List<Completion> result)
        {
            foreach(var rec in env.records.Values)
            {
                //尝试名称空间    
                bool valid = false;
                int offsetNamespace = 0;
                if(rec.rawname.StartsWith(prefix))
                {
                    valid = true;
                    offsetNamespace = 0;
                }
                else
                {
                    foreach(var nameUsingNode in persistentAST.rootNode.usingNamespaceNodes)
                    {
                        string namespaceName = nameUsingNode.namespaceNameNode.token.attribute;
                        if(Utils_CheckPrefixUseNamespace(namespaceName, prefix, rec.rawname))
                        {
                            valid = true;
                            offsetNamespace = namespaceName.Length + 2;
                            break;
                        }
                    }
                }
                if(valid == false)
                    continue;//所有命名空间都不合适  


                int offsetPrefix = 0;
                if(prefix.Contains(":"))
                    offsetPrefix = prefix.LastIndexOf(':') + 1;
                string completionStr = rec.rawname.Substring(offsetNamespace + offsetPrefix);

                if(rec.category == SymbolTable.RecordCatagory.Class)
                {
                    result.Add(new Completion()
                    {
                        label = completionStr,
                        kind = ComletionKind.Class,
                        detail = $"class {completionStr}",
                        documentation = "",
                        insertText = completionStr
                    });
                }
                else if(rec.category == SymbolTable.RecordCatagory.Function)
                {
                    string paramStr = Utils_ConcatWithComma(
                        rec.envPtr.records.Values
                        .Where(r => r.category == SymbolTable.RecordCatagory.Param)
                        .Select(p => p.typeExpression + " " + p.name)
                        );
                    result.Add(new Completion()
                    {
                        label = $"{completionStr}({paramStr})",
                        kind = ComletionKind.Function,
                        detail = $"{rec.rawname}({paramStr})",
                        documentation = "",
                        insertText = completionStr
                    });
                }
                else if(rec.category == SymbolTable.RecordCatagory.Variable)
                {
                    result.Add(new Completion()
                    {
                        label = completionStr,
                        kind = ComletionKind.Variable,
                        detail = $"{rec.typeExpression} {rec.rawname}",
                        documentation = "",
                        insertText = completionStr
                    });
                }
                else if(rec.category == SymbolTable.RecordCatagory.Param)
                {
                    result.Add(new Completion()
                    {
                        label = completionStr,
                        kind = ComletionKind.Variable,
                        detail = $"{rec.typeExpression} {rec.rawname}",
                        documentation = "",
                        insertText = completionStr
                    });
                }
                else if (rec.category == SymbolTable.RecordCatagory.Constant)
                {
                    result.Add(new Completion()
                    {
                        label = completionStr,
                        kind = ComletionKind.Constant,
                        detail = $"{rec.typeExpression} {rec.rawname}",
                        documentation = "",
                        insertText = completionStr
                    });
                }
                else
                {
                    result.Add(new Completion()
                    {
                        label = completionStr,
                        kind = ComletionKind.Text,
                        detail = $"{rec.typeExpression} {rec.rawname}",
                        documentation = "",
                        insertText = completionStr
                    });
                }
            }

            return false;
        }

        private SymbolTable GetCurrEnv(int line, int character, ref string msg)
        {
            if(line > lineStartsList.Count - 1)
            {
                msg = "Out of line";
                return null;
            }
                
            int curr = lineStartsList[line] + character;
            if(curr <= 1 || curr > sourceB.Length)
            {
                msg = "Out of index";
                return null;//注意：光标没有对应的字符（越界光标）  
            }
                

            if(this.persistentAST == null)
            {
                msg = "ast temp null";
                return null;
            }



            //寻找最深节点
            SyntaxTree.Node deepestNode = null;
            {
                SyntaxTree.Node currNode = this.persistentAST.rootNode;

                int loop = 0;
                while(currNode.Children.Length > 0)//非叶子节点  
                {
                    if (loop++ > 99) throw new Exception("Infinite Loop!"); 

                    bool anyChildHit = false;

                    var startToken = currNode.StartToken();
                    var endToken = currNode.EndToken();
                    //Is in currNode  
                    if(Utils_InRange(startToken, endToken, line, character))
                    {
                        foreach(var child in currNode.Children)
                        {
                            var childstartToken = child.StartToken();
                            var childendToken = child.EndToken();
                            if(childstartToken != null && childendToken != null)
                            {
                                if(Utils_InRange(childstartToken, childendToken, line, character))
                                {
                                    currNode = child;
                                    deepestNode = child;
                                    anyChildHit = true;
                                    break;//hit child  
                                }
                            }
                        }

                        if (anyChildHit)
                        {
                            // auto continue  
                        }
                        else
                        {
                            break;//end
                        }
                    }
                    //Not in currNode  
                    else
                    {
                        deepestNode = null;
                        break;//end
                    }
                }
            }

            //DEBUG
            if(deepestNode == null)
            {
                msg += "node null." ;
            }
            //获取符号表链  
            SymbolTable env = null;
            if(deepestNode != null)
            {
                string chain = "";
                var currNode = deepestNode;

                int loop = 0;
                while(currNode != null)
                {
                    if (loop++ > 99) throw new Exception("Infinite Loop!");

                    chain += ("-" + currNode.GetType().Name);
                    if(currNode.attributes.ContainsKey("env"))
                    {
                        env = (SymbolTable)currNode.attributes["env"];
                        break;
                    }

                    currNode = currNode.Parent;
                }

                //DEBUG
                if(env == null)
                {
                    msg += "env null. tgtNode:" + deepestNode.GetType().Name + "  parent chain:" + chain;
                }
            }

            if(env != null)
            {
                return env;
            }
            
            return default;
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


                // ------------------ 保存编译结果信息 ------------------   

                //暂时信息  
                this.tempUnitCompiled = unit;
                this.tempTokens = tokens;
                this.tempAST = semanticAnalyzer.ast;
                this.tempDiagnosticInfo = null;

                //持久信息 - 抽象语法树  
                this.persistentAST = semanticAnalyzer.ast;

                //全局作用域  
                this.persistentGlobalEnvs = unit.GetAllGlobalEnvs();


                return "success";
            }
            //编译失败 -> 填写诊断信息  
            catch (Exception ex)
            {
                switch (ex)
                {
                    case Gizbox.LexerException lexerExc:
                        {
                            this.tempDiagnosticInfo = new DiagnosticInfo()
                            {
                                code = "0001",

                                startLine = lexerExc.line - 1,
                                startChar = lexerExc.charinline,
                                endLine = GetEndLine(),
                                endChar = GetEndCharacter(),
                                message = lexerExc.Message,
                                severity = 1,
                            };
                        }
                        break;
                    case Gizbox.ParseException parseEx:
                        {
                            var errToken = parseEx.token;
                            this.tempDiagnosticInfo = new DiagnosticInfo()
                            {
                                code = "0002",

                                startLine = 0,
                                startChar = 0,
                                endLine = GetEndLine(),
                                endChar = GetEndCharacter(),
                                message = parseEx.Message,
                                severity = 2,
                            };
                        }
                        break;
                    case Gizbox.SemanticException semanticEx:
                        {
                            var firstToken = semanticEx.node.StartToken();
                            var lastToken = semanticEx.node.EndToken();
                            
                            this.tempDiagnosticInfo = new DiagnosticInfo()
                            {
                                code = "0003",

                                startLine = firstToken != null ? (firstToken.line - 1) : 0,
                                startChar = firstToken != null ? (firstToken.start) : 0,
                                endLine = lastToken != null ? (lastToken.line - 1) : GetEndLine(),
                                endChar = lastToken != null ? (lastToken.start + lastToken.length) : GetEndCharacter(),
                                message = semanticEx.Message,
                                severity = 2,
                            };
                        }
                        break;
                    default:
                        {
                            if (ex is Gizbox.GizboxException)
                            {
                                this.tempDiagnosticInfo = new DiagnosticInfo()
                                {
                                    code = ((int)((ex as GizboxException).exType)).ToString("d4"),

                                    startLine = 0,
                                    startChar = 0,
                                    endLine = GetEndLine(),
                                    endChar = GetEndCharacter(),
                                    message = ex.Message,
                                    severity = 3,
                                };
                            }
                            else
                            {
                                this.tempDiagnosticInfo = new DiagnosticInfo()
                                {
                                    code = "9999",

                                    startLine = 0,
                                    startChar = 0,
                                    endLine = GetEndLine(),
                                    endChar = GetEndCharacter(),
                                    message = ex.ToString(),
                                    severity = 3,
                                };
                            }
                        }
                        break;
                }
                return ex.ToString();
            }
        }

        private void UpdateLineInfos()
        {
            int line = 0;
            lineStartsList[0] = 0;

            for (int i = 0; i < sourceB.Length; i++)
            {
                if (sourceB[i] == '\n')
                {
                    line++;

                    if (line > lineStartsList.Count - 10)
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
            for (int i = 0; i < addcount; i++)
            {
                lineStartsList.Add(0);
            }
        }

        public string Current()
        {
            return sourceB.ToString();
        }

        public int GetEndLine()
        {
            return lineStartsList.Count - 1;
        }

        public int GetEndCharacter()
        {
            return sourceB.Length - lineStartsList[lineStartsList.Count - 1];
        }


        public static bool Utils_InRange(Token startToken, Token endToken, int line, int character)
        {
            bool leftInRange = line > startToken.line ? true : (line == startToken.line && character >= (startToken.start + startToken.length));
            bool rightInRange = line < endToken.line ? true : (line == endToken.line && character <= (endToken.start));
            return leftInRange && rightInRange;
        }
        public static bool Utils_CheckPrefixUseNamespace(string namesp, string prefix, string fullname)
        {
            if (fullname.StartsWith(namesp) == false) return false;
            if (fullname[namesp.Length] != ':') return false;
            if (fullname[namesp.Length + 1] != ':') return false;
            if (namesp.Length + 2 + prefix.Length > fullname.Length) return false;

            int prefixOffset = namesp.Length + 2;
            for (int i = 0; i < prefix.Length; ++i)
            {
                if (prefix[i] != fullname[prefixOffset + i]) return false;
            }

            return true;
        }
        public static bool Utils_IsPartOfIdentifier(char c)
        {
            if (Char.IsLetter(c)) return true;
            if (Char.IsDigit(c)) return true;
            if (c == ':') return true;//冒号视作标识符的一部分  
            if (c == '_') return true;//下划线视作标识符的一部分 

            return false;
        }

        public static string Utils_ConcatWithComma(IEnumerable<string> input)
        {
            StringBuilder sb = new StringBuilder();

            int index = 0;
            foreach(var str in input)
            {
                if(index != 0)
                    sb.Append(", ");

                sb.Append(str);

                index++;
            }

            return sb.ToString();
        }

        public static string Utils_CharToPrintFormat(char c)
        {
            switch(c)
            {
                case ' ': return "space";
                case '\t': return "tab";
                case '\n': return "\\ n";
                case '\r': return "\\ r";
                default: return c.ToString();
            }
        }

        public static bool Utils_IsSplitChar(char c)
        {
            switch (c)
            {
                case '.'://
                case ';'://
                case ','://

                case '{'://
                case '}'://
                case ']'://
                case '['://
                case ')'://
                case '('://
                case '<'://
                case '>'://

                case '\"'://
                case '\''://

                case '+'://
                case '-'://
                case '*'://
                case '/'://
                case '='://
                case '%'://
                case '?'://

                    return true;
                default:
                    return false;
            }
        }

        public static bool Utils_IsWordedgeChar(char c)
        {
            if(Utils_IsSplitChar(c))
            {
                return true;
            }

            switch (c)
            {
                case ' ':
                case '\t':
                case '\n':
                case '\r':
                    return true;
                default:
                    return false;
            }
        }
    }
}
