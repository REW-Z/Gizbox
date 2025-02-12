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

    public enum ComletionKind
    {
         /**
		 * The `Text` completion item kind.
		 */
        Text = 0,
        /**
		 * The `Method` completion item kind.
		 */
        Method = 1,
        /**
		 * The `Function` completion item kind.
		 */
        Function = 2,
        /**
		 * The `Constructor` completion item kind.
		 */
        Constructor = 3,
        /**
		 * The `Field` completion item kind.
		 */
        Field = 4,
        /**
		 * The `Variable` completion item kind.
		 */
        Variable = 5,
        /**
		 * The `Class` completion item kind.
		 */
        Class = 6,
        /**
		 * The `Interface` completion item kind.
		 */
        Interface = 7,
        /**
		 * The `Module` completion item kind.
		 */
        Module = 8,
        /**
		 * The `Property` completion item kind.
		 */
        Property = 9,
        /**
		 * The `Unit` completion item kind.
		 */
        Unit = 10,
        /**
		 * The `Value` completion item kind.
		 */
        Value = 11,
        /**
		 * The `Enum` completion item kind.
		 */
        Enum = 12,
        /**
		 * The `Keyword` completion item kind.
		 */
        Keyword = 13,
        /**
		 * The `Snippet` completion item kind.
		 */
        Snippet = 14,
        /**
		 * The `Color` completion item kind.
		 */
        Color = 15,
        /**
		 * The `Reference` completion item kind.
		 */
        Reference = 17,
        /**
		 * The `File` completion item kind.
		 */
        File = 16,
        /**
		 * The `Folder` completion item kind.
		 */
        Folder = 18,
        /**
		 * The `EnumMember` completion item kind.
		 */
        EnumMember = 19,
        /**
		 * The `Constant` completion item kind.
		 */
        Constant = 20,
        /**
		 * The `Struct` completion item kind.
		 */
        Struct = 21,
        /**
		 * The `Event` completion item kind.
		 */
        Event = 22,
        /**
		 * The `Operator` completion item kind.
		 */
        Operator = 23,
        /**
		 * The `TypeParameter` completion item kind.
		 */
        TypeParameter = 24,
        /**
		 * The `User` completion item kind.
		 */
        User = 25,
        /**
		 * The `Issue` completion item kind.
		 */
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

            char wordedgeChar = ' ';
            int wordedgeIdx = curr - 1;
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


            //DEBUG  
            result.Add(new Completion()
            {
                label = "DEBUG_SHOW_WORD_EDGE:" + wordedgeChar,
                kind = ComletionKind.Class,
                detail = "",
                documentation = "",
                insertText = ""
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
                    insertText = ""
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
                    insertText = ""
                });
            }

            //类名/变量名自动提示  
            if(wordedgeChar == ';' || wordedgeChar == '\n')
            {
                if(persistentGlobalEnvs != null && persistentAST != null)
                {
                    char[] chararr = new char[curr - wordedgeIdx - 1];
                    sourceB.CopyTo(wordedgeIdx + 1, chararr, 0, curr - wordedgeIdx - 1);
                    string prefix = new string(chararr);

                    //从符号表链收集  
                    var curre = currEnv;
                    while(curre != null)
                    {
                        if(curre.tableCatagory == SymbolTable.TableCatagory.GlobalScope)
                            break;
                        CollectCompletionInEnv(curre, prefix, result);
                        curre = curre.parent;
                    }

                    //从全局作用域收集  
                    foreach(var env in persistentGlobalEnvs)
                    {
                        CollectCompletionInEnv(env, prefix, result);
                    }
                }
            }
            //成员自动提示  
            else if(wordedgeChar == '.' && persistentAST != null)
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



                    //result.Add(new Completion()
                    //{
                    //    label = "DEBUG_OBJNAME",
                    //    kind = ComletionKind.Text,
                    //    detail = "obj:(" + objName + ")len(" + objName.Length + ")",
                    //    documentation = "",
                    //    insertText = "DEBUG_OBJNAME"
                    //});

                    foreach(var idNode in persistentAST.identityNodes)
                    {
                        if(idNode.FullName.EndsWith(objName) && idNode.attributes.ContainsKey("def_at_env"))
                        {
                            var env = (idNode.attributes["def_at_env"] as SymbolTable);

                            SymbolTable identifierEnv = env;
                            SymbolTable globalEnv = null;
                            SymbolTable.Record rec = default;
                            int count = 0;
                            while(identifierEnv != null)
                            {
                                if(count++ > 10)
                                    throw new Exception("CURR ENV LOOP !");

                                if(identifierEnv.parent == null)
                                {
                                    globalEnv = identifierEnv;
                                }

                                if(rec == default && env.ContainRecordRawName(objName))
                                {
                                    rec = env.GetRecord(objName);
                                }
                                else
                                {
                                    identifierEnv = identifierEnv.parent;
                                }
                            }


                            if(rec != default && globalEnv != null && globalEnv.ContainRecordName(rec.typeExpression))
                            {
                                var classEnv = globalEnv.GetRecord(rec.typeExpression).envPtr;
                                if(classEnv != null)
                                {
                                    var members = classEnv.records.Values;
                                    foreach(var member in members)
                                    {
                                        if(member.category == SymbolTable.RecordCatagory.Variable || member.category == SymbolTable.RecordCatagory.Param)
                                        {
                                            result.Add(new Completion()
                                            {
                                                label = member.rawname,
                                                kind = ComletionKind.Field,
                                                detail = "obj:(" + objName + ")len(" + objName.Length + ")",
                                                documentation = "",
                                                insertText = member.rawname,
                                            });
                                        }
                                        else if(member.category == SymbolTable.RecordCatagory.Function)
                                        {
                                            result.Add(new Completion()
                                            {
                                                label = member.rawname,
                                                kind = ComletionKind.Method,
                                                detail = "obj:(" + objName + ")len(" + objName.Length + ")",
                                                documentation = "",
                                                insertText = member.rawname,
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }

                }
            }

            return result;
        }

        [System.Obsolete] public List<Completion> GetCompletionLegacy(int line, int character)
        {
            int curr = lineStartsList[line] + character;
            if (curr <= 1 || curr > sourceB.Length) return new List<Completion>();//注意：光标没有对应的字符（越界光标）  
            if (line > lineStartsList.Count - 1) return new List<Completion>();

            List<Completion> result = new List<Completion>();

            char wordedgeChar = ' ';
            int wordedgeIdx = curr - 1;
            for (int i = curr - 1; i > 0; --i)
            {
                char c = sourceB[i];
                if (Utils_IsWordedgeChar(c))
                {
                    wordedgeChar = c;
                    wordedgeIdx = i;
                    break;
                }
            }


            //DEBUG  
            result.Add(new Completion()
            {
                label = "DEBUG_SHOW_WORDEDGE:" + wordedgeChar,
                kind = ComletionKind.Class,
                detail = "",
                documentation = "",
                insertText = ""
            });

            //类名/变量名自动提示  
            if (wordedgeChar == ';' || wordedgeChar == '\n')
            {
                //result.Add(new Completion()
                //{
                //    label = "DEBUG_GLOBAL",
                //    kind = ComletionKind.Class,
                //    detail = "persistent env?" + (persistentGlobalEnvs != null),
                //    documentation = "",
                //    insertText = ""
                //});


                if (persistentGlobalEnvs != null && persistentAST != null)
                {
                    char[] chararr = new char[curr - wordedgeIdx - 1];
                    sourceB.CopyTo(wordedgeIdx + 1, chararr, 0, curr - wordedgeIdx - 1);
                    string prefix = new string(chararr);


                    foreach (var env in persistentGlobalEnvs)
                    {
                        foreach (var globalDef in env.records.Values)
                        {
                            //尝试名称空间    
                            bool valid = false;
                            int offsetNamespace = 0;
                            if (globalDef.rawname.StartsWith(prefix))
                            {
                                valid = true;
                                offsetNamespace = 0;
                            }
                            else
                            {
                                foreach (var nameUsingNode in persistentAST.rootNode.usingNamespaceNodes)
                                {
                                    string namespaceName = nameUsingNode.namespaceNameNode.token.attribute;
                                    if (Utils_CheckPrefixUseNamespace(namespaceName, prefix, globalDef.rawname))
                                    {
                                        valid = true;
                                        offsetNamespace = namespaceName.Length + 2;
                                        break;
                                    }
                                }
                            }
                            if (valid == false) continue;//所有命名空间都不合适  


                            int offsetPrefix = 0;
                            if(prefix.Contains(":")) offsetPrefix = prefix.LastIndexOf(':') + 1;
                            string completionStr = globalDef.rawname.Substring(offsetNamespace + offsetPrefix);

                            if (globalDef.category == SymbolTable.RecordCatagory.Class)
                            {
                                result.Add(new Completion()
                                {
                                    label = completionStr,
                                    kind = ComletionKind.Class,
                                    detail = "class",
                                    documentation = "",
                                    insertText = completionStr
                                });
                            }
                            else if (globalDef.category == SymbolTable.RecordCatagory.Function)
                            {
                                string paramStr = "";
                                foreach (var local in globalDef.envPtr.records.Values)
                                {
                                    if (local.category == SymbolTable.RecordCatagory.Param)
                                    {
                                        paramStr += local.typeExpression + " " + local.name + ", ";
                                    }
                                }
                                result.Add(new Completion()
                                {
                                    label = completionStr,
                                    kind = ComletionKind.Function,
                                    detail = globalDef.rawname + "(" + paramStr + ")",
                                    documentation = "",
                                    insertText = completionStr
                                });
                            }
                            else if (globalDef.category == SymbolTable.RecordCatagory.Variable)
                            {
                                result.Add(new Completion()
                                {
                                    label = completionStr,
                                    kind = ComletionKind.Variable,
                                    detail = globalDef.typeExpression + " " + globalDef.rawname,
                                    documentation = "",
                                    insertText = completionStr
                                });
                            }
                        }
                    }

                }

            }

            //成员自动提示  
            if (wordedgeChar == '.' && persistentAST != null)
            {
                int objNameEnd = wordedgeIdx;
                int objNameStart = wordedgeIdx;
                for (int i = wordedgeIdx - 1; i > 0; --i)
                {
                    if (Utils_IsPartOfIdentifier(sourceB[i]) == false)
                    {
                        objNameStart = i + 1;
                        break;
                    }
                }

                int length = objNameEnd - objNameStart;
                if (length > 0)
                {
                    char[] objNameBuffer = new char[length];
                    sourceB.CopyTo(objNameStart, objNameBuffer, 0, length);
                    string objName = new string(objNameBuffer);



                    //result.Add(new Completion()
                    //{
                    //    label = "DEBUG_OBJNAME",
                    //    kind = ComletionKind.Text,
                    //    detail = "obj:(" + objName + ")len(" + objName.Length + ")",
                    //    documentation = "",
                    //    insertText = "DEBUG_OBJNAME"
                    //});

                    foreach (var idNode in persistentAST.identityNodes)
                    {
                        if (idNode.FullName.EndsWith(objName) && idNode.attributes.ContainsKey("def_at_env"))
                        {
                            var env = (idNode.attributes["def_at_env"] as SymbolTable);

                            SymbolTable currEnv = env;
                            SymbolTable globalEnv = null;
                            SymbolTable.Record rec = default;
                            int count = 0;
                            while (currEnv != null)
                            {
                                if (count++ > 10) throw new Exception("CURR ENV LOOP !");

                                if (currEnv.parent == null)
                                {
                                    globalEnv = currEnv;
                                }

                                if (rec == default && env.ContainRecordRawName(objName))
                                {
                                    rec = env.GetRecord(objName);
                                }
                                else
                                {
                                    currEnv = currEnv.parent;
                                }
                            }


                            if (rec != default && globalEnv != null && globalEnv.ContainRecordName(rec.typeExpression))
                            {
                                var classEnv = globalEnv.GetRecord(rec.typeExpression).envPtr;
                                if (classEnv != null)
                                {
                                    var members = classEnv.records.Values;
                                    foreach (var member in members)
                                    {
                                        if (member.category == SymbolTable.RecordCatagory.Variable || member.category == SymbolTable.RecordCatagory.Param)
                                        {
                                            result.Add(new Completion()
                                            {
                                                label = member.rawname,
                                                kind = ComletionKind.Field,
                                                detail = "obj:(" + objName + ")len(" + objName.Length + ")",
                                                documentation = "",
                                                insertText = member.rawname,
                                            });
                                        }
                                        else if (member.category == SymbolTable.RecordCatagory.Function)
                                        {
                                            result.Add(new Completion()
                                            {
                                                label = member.rawname,
                                                kind = ComletionKind.Method,
                                                detail = "obj:(" + objName + ")len(" + objName.Length + ")",
                                                documentation = "",
                                                insertText = member.rawname,
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }

                }
            }

            return result;
        }

        private void CollectCompletionInEnv(SymbolTable env, string prefix, List<Completion> result)
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
                        detail = "class",
                        documentation = "",
                        insertText = completionStr
                    });
                }
                else if(rec.category == SymbolTable.RecordCatagory.Function)
                {
                    string paramStr = "";
                    foreach(var local in rec.envPtr.records.Values)
                    {
                        if(local.category == SymbolTable.RecordCatagory.Param)
                        {
                            paramStr += local.typeExpression + " " + local.name + ", ";
                        }
                    }
                    result.Add(new Completion()
                    {
                        label = completionStr,
                        kind = ComletionKind.Function,
                        detail = rec.rawname + "(" + paramStr + ")",
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
                        detail = rec.typeExpression + " " + rec.rawname,
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
                        detail = rec.typeExpression + " " + rec.rawname,
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
                        detail = rec.typeExpression + " " + rec.rawname,
                        documentation = "",
                        insertText = completionStr
                    });
                }
            }
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



            //寻找叶子节点
            SyntaxTree.Node tgtNode = null;
            {
                SyntaxTree.Node currNode = this.persistentAST.rootNode;
                while(currNode.Children.Length > 0)
                {
                    bool hit = false;

                    var startToken = currNode.StartToken();
                    var endToken = currNode.EndToken();
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
                                    hit = true;
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        break;
                    }



                    if(hit == false)
                    {
                        tgtNode = currNode;
                        break;
                    }
                }
            }

            //DEBUG
            if(tgtNode == null)
            {
                msg += "node null." ;
            }
            //获取符号表链  
            SymbolTable env = null;
            if(tgtNode != null)
            {
                string chain = "";
                var currNode = tgtNode;
                while(currNode != null)
                {
                    chain += ("-" + currNode.GetType().Name);
                    if(currNode.attributes.ContainsKey("env"))
                    {
                        env = (SymbolTable)currNode.attributes["env"];
                        break;
                    }

                    currNode = tgtNode.Parent;
                }

                //DEBUG
                if(env == null)
                {
                    msg += "env null. tgtNode:" + tgtNode.GetType().Name + "  parent chain:" + chain;
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

                                startLine = firstToken.line - 1,
                                startChar = firstToken.start,
                                endLine = lastToken.line - 1,
                                endChar = lastToken.start + lastToken.attribute.Length,
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
            bool leftInRange = line > startToken.line ? true : (line == startToken.line && character >= startToken.start);
            bool rightInRange = line < endToken.line ? true : (line == endToken.line && character <= (endToken.start + endToken.length));
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
        public static bool Utils_IsWordedgeChar(char c)
        {
            switch (c)
            {
                case ' '://
                case '\t'://
                case '\n'://
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
    }
}
