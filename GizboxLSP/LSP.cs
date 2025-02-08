
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






        //入口  
        static async Task Main(string[] args)
        {

            //接收器  
            Console.OutputEncoding = Encoding.UTF8;
            var buffer = new byte[1024 * 64];
            var messageBuilder = new StringBuilder();

            //诊断计时开始  
            watch.Start();

            //Log Clean
            logPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop) + "//lsplog_" + System.DateTime.Now.Minute + "_" + System.DateTime.Now.Second + ".txt";
            if(System.IO.File.Exists(logPath))
            {
                System.IO.File.Delete(logPath);
            }

            //写入流启动    
            _ = Task.Run(StartWriteQueueToOutstream);

            //诊断计时系统启动  
            _ = Task.Run(StartDiagnosticSystem);


            while(true)
            {
                try
                {
                    var bytesRead = await inputStream.ReadAsync(buffer, 0, buffer.Length);
                    if(bytesRead == 0)
                    {
                        messageBuilder.Clear();
                        continue;
                    }

                    string messagePart = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    messageBuilder.Append(messagePart);

                    //Log("\n\n[Receive]\n\n\n" + messagePart);

                    var messageContents = SplitJson(messageBuilder.ToString());
                    if(messageContents.Count == 0)
                    {
                        messageBuilder.Clear();
                        continue;
                    }

                    foreach(var content in messageContents)
                    {
                        //Log("\n\n[Split]\n\n\n" + content);
                    }


                    foreach(var messageContent in messageContents)
                    {
                        var lspRequest = JObject.Parse(messageContent);

                        if(lspRequest != null)
                        {
                            string? methodName = lspRequest["method"]?.ToObject<string>();

                            if(methodName == "initialize")
                            {
                                //Log("\n\n[Handling Init...]\n\n\n");
                                await HandleInitialize(lspRequest);
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

                                //Log("\n\n[Handling didOpen...]\n\n\n");
                                await HandleDidOpen(lspRequest);

                                //第一次诊断  
                                if(string.IsNullOrEmpty(currentDocUri) == false)
                                {
                                    await Diagnostics(currentDocUri);
                                }
                            }
                            else if(methodName == "textDocument/didChange")
                            {
                                //Log("\n\n[Handling didChange...]\n\n\n");
                                await HandleDidChange(lspRequest);

                                //打字时候暂缓发送诊断  
                                if(currentDocUri != null)
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


                                //Log("\n\n[Handling completion....]\n\n\n");
                                await HandleCompletion(lspRequest);
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

                                //Log("\n\n[Handling highlighting....]\n\n\n");
                                await HandleHighlighting(lspRequest);
                            }
                        }
                    }


                    messageBuilder.Clear();
                }
                catch(Exception ex)
                {
                    //Log("\n\n[ERR]:\n\n" + ex.ToString());
                    messageBuilder.Clear();
                }
            }
        }

        public static void StartWriteQueueToOutstream()
        {
            while(true)
            {
                if(writeQueue.Count > 0)
                {
                    string responseMsg;
                    bool dequeued = writeQueue.TryDequeue(out responseMsg);
                    if(dequeued)
                    {
                        var responseBytes = Encoding.UTF8.GetBytes(responseMsg);
                        outputStream.Write(responseBytes, 0, responseBytes.Length);
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
            string logStr = "";
            //set folder  
            var folderInfos = (JArray?)request["params"]?["workspaceFolders"];
            if(folderInfos != null && folderInfos.Count > 0)
            {
                string? uri = folderInfos[0]["uri"]?.ToObject<string>();
                if(string.IsNullOrEmpty(uri) == false)
                {
                    Uri uriObj = new Uri(uri);
                    string finalPath = uriObj.LocalPath;
                    if(finalPath[0] == '/')
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
            writeQueue.Enqueue(responseMessage);


            await LogToStream("Server Inited... " + logStr);
        }

        private static async Task HandleDidOpen(JObject request)
        {
            string txt = (string)request["params"]["textDocument"]["text"];
            gizboxService.DidOpen(txt);

            //Log("\n[open text....current]\n" + gizboxService.Current());
        }
        private static async Task HandleDidChange(JObject request)
        {
            var changes = (JArray)request["params"]["contentChanges"];
            if(changes == null)
            {
                throw new Exception("没有contentChanges字段：" + request.ToString());
            }

            //Log("\n change count:" + changes.Count  + "\n");
            for(int i = 0; i < changes.Count; i++)
            {
                var change = (JObject)changes[i];
                var jRange = (JObject)(change["range"]);

                bool fullUpdate = false;
                if(jRange == null)
                {
                    fullUpdate = true;
                }

                //增量更新  
                if(fullUpdate == false)
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
            }

            await LogToStream("did changes....current : " + gizboxService.sourceB[gizboxService.sourceB.Length - 1]);
        }

        private static async Task HandleCompletion(JObject request)
        {
            var jposition = request["params"]?["position"]?.ToObject<JObject>();
            List<Completion> result;
            if(jposition != null)
            {
                int line = jposition["line"]?.ToObject<int>() ?? 0;
                int character = jposition["character"]?.ToObject<int>() ?? 0;

                try
                {
                    result = gizboxService.GetCompletion(line, character);
                }
                catch(Exception ex)
                {
                    result = new List<Completion>() { new Completion() { 
                        label = "DEBUG_ERR:" + ex.ToString(),
                        detail = ex.ToString(),
                        insertText = "Debug_Err"
                        } 
                    };
                }
            }
            else
            {
                result = new List<Completion>();

                if(result.Count == 0)
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


            var objArr = result.Select(c => new {
                label = c.label,
                kind = c.kind,
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

            //Log("\n[Completion Respons:]\n" + responseJson);

            var responseMessage = LSPUtils.HttpResponsePlainText(Encoding.UTF8.GetByteCount(responseJson), responseJson);// $"Content-Length: {Encoding.UTF8.GetByteCount(responseJson)}\r\n\r\n{responseJson}";
            //var responseBytes = Encoding.UTF8.GetBytes(responseMessage);
            //await outputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
            writeQueue.Enqueue(responseMessage);

            int takeCount = Math.Min(5, result.Count);
            await LogToStream("complete items sent:" + string.Concat(result.Take(takeCount).Select(c => c.label + ",")) );
        }

        private static async Task HandleHighlighting(JObject request)
        {
            var line = request["params"]["position"]["line"].ToObject<int>();
            var character = request["params"]["position"]["character"].ToObject<int>();

            List<HighLightToken> highlights = gizboxService.GetHighlights(line, character);
            var response = new
            {
                jsonrpc = "2.0",
                id = (int?)request["id"],
                result = highlights.Select(h => new {
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

            //Log("\n[Highlights MSG]\n" + responseMessage);


            //var responseBytes = Encoding.UTF8.GetBytes(responseMessage);
            //await outputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
            writeQueue.Enqueue(responseMessage);
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

            object response;

            if(gizboxService.tempDiagnosticInfo != null)
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
                            code = gizboxService.tempDiagnosticInfo.code,
                            source = "gizbox",
                            message = gizboxService.tempDiagnosticInfo.message,
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
            //var responseBytes = Encoding.UTF8.GetBytes(responseMessage);
            //await outputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
            writeQueue.Enqueue(responseMessage);

            //await LogToStream("StreamLog:\n" + responseJson);
        }

        private static async Task LogToStream(string text)
        {
            var response = new
            {
                jsonrpc = "2.0",
                method = "debug/log",
                @params = new
                {
                    text = text,
                }
            };

            var responseJson = JsonConvert.SerializeObject(response);

            var responseMessage = LSPUtils.HttpResponsePlainText(Encoding.UTF8.GetByteCount(responseJson), responseJson);// $"Content-Length: {Encoding.UTF8.GetByteCount(responseJson)}\r\n\r\n{responseJson}";
            //var responseBytes = Encoding.UTF8.GetBytes(responseMessage);
            //await outputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
            writeQueue.Enqueue(responseMessage);
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
