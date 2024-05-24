
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


class Program
{
    //语言服务  
    public static Gizbox.LanguageServices.LanguageService gizboxService = new Gizbox.LanguageServices.LanguageService();

    public static string? currentDocUri = null;

    //日志 
    public static string? logPath;

    //同步器  
    public static ConcurrentQueue<string> writeQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();



    //入口  
    static async Task Main(string[] args)
    {

        //接收器  
        Console.OutputEncoding = Encoding.UTF8;
        var inputStream = Console.OpenStandardInput();
        var outputStream = Console.OpenStandardOutput();
        var buffer = new byte[1024 * 64];
        var messageBuilder = new StringBuilder();

        //Log Clean
        logPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop) + "//lsplog_" + System.DateTime.Now.Minute + "_" +  System.DateTime.Now.Second + ".txt";
        if (System.IO.File.Exists(logPath))
        {
            System.IO.File.Delete(logPath);
        }

        while (true)
        {
            try
            {
                var bytesRead = await inputStream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    messageBuilder.Clear();
                    continue;
                }

                string messagePart = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                messageBuilder.Append(messagePart);

                //Log("\n\n[Receive]\n\n\n" + messagePart);

                var messageContents = SplitJson(messageBuilder.ToString());
                if (messageContents.Count == 0)
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

                    if (lspRequest != null)
                    {
                        string? methodName = lspRequest["method"]?.ToObject<string>();

                        if (methodName == "initialize")
                        {
                            //Log("\n\n[Handling Init...]\n\n\n");
                            await HandleInitialize(lspRequest);
                        }
                        else if (methodName == "textDocument/didOpen")
                        {
                            //切换文档->重置    
                            string? uri = lspRequest["params"]?["textDocument"]?["uri"]?.ToObject<string>();
                            if (currentDocUri != uri)
                            {
                                gizboxService.Reset();
                                currentDocUri = uri;
                            }

                            //Log("\n\n[Handling didOpen...]\n\n\n");
                            await HandleDidOpen(lspRequest);
                        }
                        else if (methodName == "textDocument/didChange")
                        {
                            //Log("\n\n[Handling didChange...]\n\n\n");
                            await HandleDidChange(lspRequest);
                        }
                        else if (methodName == "textDocument/completion")
                        {
                            //切换文档->重置    
                            string? uri = lspRequest["params"]?["textDocument"]?["uri"]?.ToObject<string>();
                            if (currentDocUri != uri)
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
                            if (currentDocUri != uri)
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


            int trycount = 0;
            while(writeQueue.Count > 0)
            {
                trycount++;
                if (trycount > 99) break;

                string str;
                bool dequeued = writeQueue.TryDequeue(out str);
                if(dequeued)
                {
                    var responseBytes = Encoding.UTF8.GetBytes(str);
                    outputStream.Write(responseBytes, 0, responseBytes.Length);
                }
            }
        }
    }


    public static List<string> SplitJson(string input)
    {
        var jsonObjects = new List<string>();
        int braceDepth = 0;
        int start = 0;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '{')
            {
                if (braceDepth == 0)
                {
                    start = i;
                }
                braceDepth++;
            }
            else if (c == '}')
            {
                braceDepth--;
                if (braceDepth == 0)
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
        if (headerEndIndex >= 0)
        {
            return message.Substring(headerEndIndex + 4);
        }
        return null;
    }

    private static async Task HandleInitialize(JObject request)
    {
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
        
        var responseMessage = $"Content-Length: {Encoding.UTF8.GetByteCount(responseJson)}\r\n\r\n{responseJson}";
        //var responseBytes = Encoding.UTF8.GetBytes(responseMessage);
        //await outputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
        writeQueue.Enqueue(responseMessage);
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
        //Log("\n change count:" + changes.Count  + "\n");
        for(int i = 0; i < changes.Count; i++)
        {
            var change = (JObject)changes[i];
            var jRange = (JObject)(change["range"]);

            bool fullUpdate = false;
            if (jRange == null)
            {
                fullUpdate = true;
            }

            //全量更新  
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
            //增量更新  
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

        //Log("\n[did changes....current]\n" + gizboxService.Current());
    }

    private static async Task HandleCompletion(JObject request)
    {
        var response = new
        {
            jsonrpc = "2.0",
            id = (int?)request["id"],
            result = new
            {
                items = new[]
                {
                    new { label = "completion1", kind = 1 },
                    new { label = "completion2", kind = 1 },
                    new { label = "completion3", kind = 1 }
                }
            }
        };

        var responseJson = JsonConvert.SerializeObject(response);

        var responseMessage = $"Content-Length: {Encoding.UTF8.GetByteCount(responseJson)}\r\n\r\n{responseJson}";
        //var responseBytes = Encoding.UTF8.GetBytes(responseMessage);
        //await outputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
        writeQueue.Enqueue(responseMessage);
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

        var responseMessage = $"Content-Length: {Encoding.UTF8.GetByteCount(responseJson)}\r\n\r\n{responseJson}";

        //Log("\n[Highlights MSG]\n" + responseMessage);


        //var responseBytes = Encoding.UTF8.GetBytes(responseMessage);
        //await outputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
        writeQueue.Enqueue(responseMessage);
    }









    private static List<string> logList = new List<string>();
    private static object mutexList = new object();
    private static object mutexFile = new object();
    private static void Log(string txt)
    {
        lock (mutexList)
        {
            logList.Add(txt);
        }

        lock (mutexFile)
        {
            // 创建文件时立即关闭文件流
            if (!File.Exists(logPath))
            {
                using (File.Create(logPath)) { }
            }

            try
            {
                using (StreamWriter sw = new StreamWriter(logPath, true))
                {
                    lock (mutexList)
                    {
                        foreach (var log in logList)
                        {
                            sw.WriteLine(log); // 使用 log 而不是 txt
                        }
                        logList.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }
    }

}