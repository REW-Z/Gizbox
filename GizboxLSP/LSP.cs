
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

using System.Collections.Concurrent;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static System.Net.Mime.MediaTypeNames;
using Gizbox.LanguageServices;
using System.IO.Enumeration;
using System.Security.AccessControl;
using System.Runtime.CompilerServices;
using System.Net.Http.Headers;


namespace Gizbox.LSP
{
    class Program
    {
        //语言服务  
        public static Gizbox.LanguageServices.LanguageService gizboxService = new Gizbox.LanguageServices.LanguageService();

        public static Stream inputStream = Console.OpenStandardInput();
        public static Stream outputStream = Console.OpenStandardOutput();

        public static string? currentDocUri = null;


        //日志 
        public static string? logPath;

        //同步器  
        public static ConcurrentQueue<string> writeQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();


        //输出流同步锁  
        private static object mutexOutstream = new object();


        //诊断计时器目标  
        private static long needDiagnosticInMilisec = -1;

        //诊断计时器  
        private static System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();


        //常量参数  
        private static double heartbeatInterval = 4f;




        //入口  
        static async Task Main(string[] args)
        {
            //!关闭编译器的ConsoleLog(否则会当作LSP响应发送到Client)    
            Gizbox.GixConsole.enableSystemConsole = false;


            //接收器  
            Console.OutputEncoding = Encoding.UTF8;
            var buffer = new byte[1024 * 64];
            var messageBuilder = new StringBuilder();

            //诊断计时开始  
            watch.Start();

            //Log Clean
            CleanFileLog();

            //写入流启动    
            _ = Task.Run(StartWriteQueueToOutstream);

            //诊断计时系统启动  
            _ = Task.Run(StartDiagnosticSystem);


            //心跳相关  
             LogToClient("HEARTBEAT(START):" + System.DateTime.Now.ToString());
            DateTime lastLoop = DateTime.Now;
            TimeSpan timeTemp = default;

            while (true)
            {
                //心跳  
                Thread.Sleep(10);
                var deltaTime = DateTime.Now - lastLoop;
                lastLoop = DateTime.Now;
                timeTemp += deltaTime;
                if (timeTemp.TotalSeconds > Program.heartbeatInterval)
                {
                    timeTemp -= TimeSpan.FromSeconds(Program.heartbeatInterval);
                     LogToClient("HEARTBEAT:" + System.DateTime.Now.ToString());
                }

                //处理请求  
                try
                {
                    //读取输入流  
                    LogToFile("读取输入中...");
                    var bytesRead = await inputStream.ReadAsync(buffer, 0, buffer.Length);
                    if(bytesRead == 0)
                    {
                        messageBuilder.Clear();
                        continue;
                    }

                    string messagePart = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    messageBuilder.Append(messagePart);


                    var messageContents = SplitJson(messageBuilder.ToString());
                    if(messageContents.Count == 0)
                    {
                        messageBuilder.Clear();
                        continue;
                    }

                    foreach(var content in messageContents)
                    {
                    }


                    LogToFile("读取完毕...一共" + messageContents.Count + "条");

                    foreach (var messageContent in messageContents)
                    {
                        var lspRequest = JObject.Parse(messageContent);

                        if(lspRequest != null)
                        {
                            string? methodName = lspRequest["method"]?.ToObject<string>();

                            if(methodName == "initialize")
                            {
                                LogToFile("Start Handle Init...");
                                await HandleInitialize(lspRequest);
                                LogToFile("End Handle Init...");
                            }
                            else if(methodName == "textDocument/didOpen")
                            {
                                //切换文档->重置    
                                string? uri = lspRequest["params"]?["textDocument"]?["uri"]?.ToObject<string>();
                                if(currentDocUri != uri)
                                {
                                    gizboxService.Reset();
                                    currentDocUri = uri;
                                }

                                LogToFile("Start Handle DidOpen...");
                                await HandleDidOpen(lspRequest);
                                LogToFile("End Handle DidOpen...");

                                //第一次诊断  
                                if (string.IsNullOrEmpty(currentDocUri) == false)
                                {
                                    await Diagnostics(currentDocUri);
                                }
                            }
                            else if(methodName == "textDocument/didChange")
                            {
                                LogToFile("Start Handle DidChange...");
                                await HandleDidChange(lspRequest);
                                LogToFile("End Handle DidChange...");

                                //打字时候暂缓发送诊断  
                                if (currentDocUri != null)
                                {
                                    _ = DiagnosticsAfter(currentDocUri, 1000);
                                }
                            }
                            else if(methodName == "textDocument/completion")
                            {
                                //切换文档->重置    
                                string? uri = lspRequest["params"]?["textDocument"]?["uri"]?.ToObject<string>();
                                if(currentDocUri != uri)
                                {
                                    gizboxService.Reset();
                                    currentDocUri = uri;
                                }


                                LogToFile("Start Handle Completion...");
                                await HandleCompletion(lspRequest);
                                LogToFile("End Handle Completion...");
                            }
                            else if(methodName == "textDocument/documentHighlight")
                            {
                                //切换文档->重置    
                                string? uri = lspRequest["params"]?["textDocument"]?["uri"]?.ToObject<string>();
                                if(currentDocUri != uri)
                                {
                                    gizboxService.Reset();
                                    currentDocUri = uri;
                                }

                                LogToFile("Start Handle Highlighting...");
                                await HandleHighlighting(lspRequest);
                                LogToFile("End Handle Highlighting...");
                            }
                        }
                    }


                    messageBuilder.Clear();
                }
                catch(Exception ex)
                {
                    messageBuilder.Clear();
                    LogToFile("LSP Catch Err:" + ex.ToString());
                    LogToClient(" LSP Catch Err:" + ex.ToString());
                }
            }

        }


        public static void StartWriteQueueToOutstream()
        {
            while(true)
            {
                lock(writeQueue)
                {
                    if(writeQueue.Count > 0)
                    {
                        string responseMsg;
                        bool dequeued = writeQueue.TryDequeue(out responseMsg);
                        if(dequeued)
                        {
                            var responseBytes = Encoding.UTF8.GetBytes(responseMsg);
                            outputStream.Write(responseBytes, 0, responseBytes.Length);
                            outputStream.Flush();
                        }
                    }
                }
            }
        }

        public static async void StartDiagnosticSystem()
        {
            while(true)
            {
                if(needDiagnosticInMilisec > 0)
                {
                    if(watch.ElapsedMilliseconds > needDiagnosticInMilisec)
                    {
                        await Diagnostics(currentDocUri);
                        needDiagnosticInMilisec = -1;
                    }
                }
            }
        }


        public static List<string> SplitJson(string input)
        {
            var jsonObjects = new List<string>();
            int braceDepth = 0;
            int start = 0;

            for(int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if(c == '{')
                {
                    if(braceDepth == 0)
                    {
                        start = i;
                    }
                    braceDepth++;
                }
                else if(c == '}')
                {
                    braceDepth--;
                    if(braceDepth == 0)
                    {
                        int length = i - start + 1;
                        jsonObjects.Add(input.Substring(start, length));
                    }
                }
            }

            return jsonObjects;
        }

        private static string GetJsonContent(string message)
        {
            var headerEndIndex = message.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if(headerEndIndex >= 0)
            {
                return message.Substring(headerEndIndex + 4);
            }
            return null;
        }

        private static async Task HandleInitialize(JObject request)
        {
            try
            {
                string logStr = "";
                //set folder  
                var folderInfos = (JArray?)request["params"]?["workspaceFolders"];
                if (folderInfos != null && folderInfos.Count > 0)
                {
                    string? uri = folderInfos[0]["uri"]?.ToObject<string>();
                    if (string.IsNullOrEmpty(uri) == false)
                    {
                        Uri uriObj = new Uri(uri);
                        string finalPath = uriObj.LocalPath;
                        if (finalPath[0] == '/')
                            finalPath = finalPath.Substring(1);

                        try
                        {
                            gizboxService.SetWorkFolder(finalPath);

                            logStr = ("Set Work Folder:" + finalPath);
                        }
                        catch
                        {
                            logStr = ("Invalid Work Folder:" + finalPath);
                        }
                    }
                    else
                    {
                        logStr = ("Set Work Folder (uri null)");
                    }
                }
                else
                {
                    logStr = ("Set Work Folder (workspaceFolders null)");
                }


                //response  
                var response = new
                {
                    jsonrpc = "2.0",
                    id = (int?)request["id"],
                    result = new
                    {
                        capabilities = new
                        {
                            textDocumentSync = 2,//同步    0 、 1 、 2
                            completionProvider = new { resolveProvider = true },
                            hoverProvider = true
                        }
                    }
                };

                var responseJson = JsonConvert.SerializeObject(response);

                var responseMessage = LSPUtils.HttpResponsePlainText(Encoding.UTF8.GetByteCount(responseJson), responseJson);// $"Content-Length: {Encoding.UTF8.GetByteCount(responseJson)}\r\n\r\n{responseJson}";
                                                                                                                             //var responseBytes = Encoding.UTF8.GetBytes(responseMessage);
                                                                                                                             //await outputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                lock (writeQueue)
                {
                    writeQueue.Enqueue(responseMessage);
                }
            }
            catch(Exception e)
            {
                LogToClient("!!! Err Handle Init");
                LogToFile("!!! Err Handle Init");
                throw new Exception("Catch Err When Handle Init:" + e.ToString());
            }
        }

        private static async Task HandleDidOpen(JObject request)
        {
            string txt = (string)request["params"]["textDocument"]["text"];
            gizboxService.DidOpen(txt);

            LogToClient($"Handle DidOpen...textLen:{txt.Length}  debug:AnyDiagnostic:{(gizboxService.tempDiagnosticInfo != null)}");
        }
        private static async Task HandleDidChange(JObject request)
        {
            try
            {
                var changes = (JArray)request["params"]["contentChanges"];
                if (changes == null)
                {
                    throw new Exception("没有contentChanges字段：" + request.ToString());
                }

                for (int i = 0; i < changes.Count; i++)
                {
                    var change = (JObject)changes[i];
                    var jRange = (JObject)(change["range"]);

                    bool fullUpdate = false;
                    if (jRange == null)
                    {
                        fullUpdate = true;
                    }

                    //增量更新  
                    if (fullUpdate == false)
                    {
                        int start_line = (int)jRange["start"]["line"];
                        int start_char = (int)jRange["start"]["character"];
                        int end_line = (int)jRange["end"]["line"];
                        int end_char = (int)jRange["end"]["character"];
                        string text = (string)change["text"];

                        gizboxService.DidChange(
                            start_line,
                            start_char,
                            end_line,
                            end_char,
                            text);
                    }
                    //全量更新  
                    else
                    {
                        string text = (string)change["text"];
                        gizboxService.DidChange(
                            -1,
                            -1,
                            -1,
                            -1,
                            text);
                    }

                    LogToClient($"Handle did changes[{i}]....isfullupdate:{fullUpdate}    textLength:{((string)change["text"]).Length}");
                }
            }
            catch(Exception e)
            {
                LogToClient("!!ERR Handle did changes");
                LogToFile("!!ERR Handle did changes");
                throw new Exception("Catch Err When Handle DidChange:" + e.ToString());
            }
        }

        private static async Task HandleCompletion(JObject request)
        {
            try
            {
                var jposition = request["params"]?["position"]?.ToObject<JObject>();

                List<Completion> result;

                if (jposition != null)
                {
                    int line = jposition["line"]?.ToObject<int>() ?? 0;
                    int character = jposition["character"]?.ToObject<int>() ?? 0;

                    try
                    {
                        result = gizboxService.GetCompletion(line, character);
                    }
                    catch (Exception ex)
                    {
                        result = new List<Completion>() {
                            new Completion() {
                                label = "DEBUG_ERR:" + ex.ToString(),
                                detail = ex.ToString(),
                                insertText = "DEBUG_ERR:" + ex.ToString(),
                            }
                        };
                        LogToFile("!!GetCompletionErr:" + ex.ToString());
                    }
                }
                else
                {
                    result = new List<Completion>();

                    if (result.Count == 0)
                    {
                        result.Add(new Completion()
                        {
                            label = "DEBUG_NO_COMPLETION",
                            kind = ComletionKind.Field,
                            detail = "no completion",
                            documentation = "",
                            insertText = "",
                        });
                    }
                }


                var objArr = result.Select(c => new
                {
                    label = c.label,
                    kind = (int)c.kind,
                    detail = c.detail,
                    documentation = c.documentation,
                    insertText = c.insertText,
                });

                var response = new
                {
                    jsonrpc = "2.0",
                    id = (int?)request["id"],
                    result = new
                    {
                        isIncomplete = false,
                        items = objArr
                    }
                };

                var responseJson = JsonConvert.SerializeObject(response);

                var responseMessage = LSPUtils.HttpResponsePlainText(Encoding.UTF8.GetByteCount(responseJson), responseJson);// $"Content-Length: {Encoding.UTF8.GetByteCount(responseJson)}\r\n\r\n{responseJson}";

                lock (writeQueue)
                {
                    writeQueue.Enqueue(responseMessage);
                }

                LogToClient($"Handle Completion:{result?.Count ?? 0}");
            }
            catch(Exception e)
            {
                LogToClient("!!ERR Handle Completion");
                LogToFile("!!ERR Handle Completion");
                throw new Exception("Catch Err When Handle Completion:" + e.ToString());
            }
        }

        private static async Task HandleHighlighting(JObject request)
        {
            try
            {
                var line = request["params"]["position"]["line"].ToObject<int>();
                var character = request["params"]["position"]["character"].ToObject<int>();

                List<HighLightToken> highlights = gizboxService.GetHighlights(line, character);
                var response = new
                {
                    jsonrpc = "2.0",
                    id = (int?)request["id"],
                    result = highlights.Select(h => new
                    {
                        range = new
                        {
                            start = new
                            {
                                line = h.startLine,
                                character = h.startChar,
                            },
                            end = new
                            {
                                line = h.endLine,
                                character = h.endChar,
                            }
                        },
                        kind = h.kind

                    }).ToArray()
                };

                var responseJson = JsonConvert.SerializeObject(response);

                var responseMessage = LSPUtils.HttpResponsePlainText(Encoding.UTF8.GetByteCount(responseJson), responseJson);// $"Content-Length: {Encoding.UTF8.GetByteCount(responseJson)}\r\n\r\n{responseJson}";


                lock (writeQueue)
                {
                    writeQueue.Enqueue(responseMessage);
                }

                LogToClient($"Handle Highlight:{highlights.Count}");
            }
            catch (Exception e)
            {
                LogToClient("!!ERR Handle Highlight");
                LogToFile("!!ERR Handle Highlight");
                throw new Exception("Catch Err When Handle Highlight:" + e.ToString());
            }
        }

        private static async Task DiagnosticsAfter(string uri, long milisecDelay)
        {
            needDiagnosticInMilisec = milisecDelay;
            watch.Restart();
        }

        private static async Task Diagnostics(string uri)
        {
            if(string.IsNullOrEmpty(uri))
                return;

            try
            {
                object response;

                if (gizboxService.tempDiagnosticInfo != null)
                {
                    response = new
                    {
                        jsonrpc = "2.0",
                        method = "textDocument/publishDiagnostics",
                        @params = new
                        {
                            uri = uri,
                            diagnostics = new[]
                            {
                        new {
                            range = new{
                                start = new {
                                    line = gizboxService.tempDiagnosticInfo.startLine,
                                    character = gizboxService.tempDiagnosticInfo.startChar,
                                },
                                end = new {
                                    line = gizboxService.tempDiagnosticInfo.endLine,
                                    character = gizboxService.tempDiagnosticInfo.endChar,
                                }
                            },
                            severity = gizboxService.tempDiagnosticInfo.severity,
                            code = Escape(gizboxService.tempDiagnosticInfo.code),
                            source = "gizbox",
                            message = Escape(gizboxService.tempDiagnosticInfo.message),
                        }
                    }
                        }
                    };
                }
                else
                {
                    response = new
                    {
                        jsonrpc = "2.0",
                        method = "textDocument/publishDiagnostics",
                        @params = new
                        {
                            uri = uri,
                            diagnostics = new object[0]
                        }
                    };
                }


                var responseJson = JsonConvert.SerializeObject(response);

                var responseMessage = LSPUtils.HttpResponsePlainText(Encoding.UTF8.GetByteCount(responseJson), responseJson);// $"Content-Length: {Encoding.UTF8.GetByteCount(responseJson)}\r\n\r\n{responseJson}";

                lock (writeQueue)
                {
                    writeQueue.Enqueue(responseMessage);
                }

                LogToClient($"Handle Diagnostics severity: {(gizboxService.tempDiagnosticInfo != null ? gizboxService.tempDiagnosticInfo.severity.ToString() : "none")}");
            }
            catch(Exception e)
            {
                LogToClient("!!ERR Handle Diagnostics");
                LogToFile("!!ERR Handle Diagnostics");
                throw new Exception("Catch Err When Handle Diagnostics:" + e.ToString());
            }
        }

        private static string Escape(string input)
        {
            string result = input.Replace("\"", "*");
            result =  result.Replace("\'", "*");

            return result;
        }

        private static void LogToClient(string text)
        {
            var response = new
            {
                jsonrpc = "2.0",
                method = "debug/log",
                @params = new
                {
                    text = $"{Escape(text)} ({System.DateTime.Now.ToString()})",
                }
            };

            var responseJson = JsonConvert.SerializeObject(response);

            var responseMessage = LSPUtils.HttpResponsePlainText(Encoding.UTF8.GetByteCount(responseJson), responseJson);// $"Content-Length: {Encoding.UTF8.GetByteCount(responseJson)}\r\n\r\n{responseJson}";

            lock(writeQueue)
            {
                writeQueue.Enqueue(responseMessage);
            }
        }

        private static void CleanFileLog()
        {
            return;

            string path = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop) + "\\lsp_log.txt";
            if(System.IO.File.Exists(path))
            {
                System.IO.File.WriteAllText(path, string.Empty);
            }
        }
        private static void LogToFile(string text)
        {
            return;

            string path = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop) + "\\lsp_log.txt";
            string content = "[" + System.DateTime.Now + "]" + text + Environment.NewLine;
            if (System.IO.File.Exists(path))
            {
                System.IO.File.AppendAllText(path, content);
            }
            else
            {
                System.IO.File.WriteAllText(path, content);
            }
            
        }
    }


    public class LSPUtils
    {
        public static string HttpResponsePlainText(int length, string content)
        {
            var responseMessage = $"Content-Type: text/plain\r\nContent-Length: {length}\r\n\r\n{content}";
            return responseMessage;
        }
    }
}
