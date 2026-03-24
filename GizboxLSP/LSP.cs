using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gizbox.LanguageServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gizbox.LSP
{
    class Program
    {
        //语言服务  
        public static LanguageService gizboxService = new LanguageService();

        public static Stream inputStream = Console.OpenStandardInput();
        public static Stream outputStream = Console.OpenStandardOutput();

        public static string? currentDocUri = null;

        //日志 
        public static string? logPath;

        //同步器  
        public static ConcurrentQueue<string> writeQueue = new ConcurrentQueue<string>();

        private static readonly SemaphoreSlim writeSignal = new SemaphoreSlim(0, int.MaxValue);
        private static readonly SemaphoreSlim serviceMutex = new SemaphoreSlim(1, 1);
        private static readonly object diagnosticMutex = new object();
        private static CancellationTokenSource? diagnosticCts;

        //入口  
        static async Task Main(string[] args)
        {
            //!关闭编译器的ConsoleLog(否则会当作LSP响应发送到Client)    
            Gizbox.GixConsole.enableSystemConsole = false;

            //协议输出使用独立流，普通Console输出全部吞掉，避免污染LSP通信
            Console.OutputEncoding = Encoding.UTF8;
            Console.SetOut(TextWriter.Null);

            CleanFileLog();

            _ = Task.Run(StartWriteQueueToOutstreamAsync);

            while (true)
            {
                try
                {
                    string? messageContent = await ReadMessageAsync(inputStream, CancellationToken.None);
                    if (messageContent == null)
                        break;

                    var lspRequest = JObject.Parse(messageContent);
                    await HandleRequestAsync(lspRequest);
                }
                catch (EndOfStreamException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogToFile("LSP Catch Err:" + ex);
                    LogToClient("LSP Catch Err:" + ex);
                }
            }
        }

        /// <summary>
        /// 按LSP标准的Content-Length协议读取一条完整消息。
        /// </summary>
        private static async Task<string?> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] headerTerminator = new byte[] { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };
            byte[] oneByteBuffer = new byte[1];
            using var headerStream = new MemoryStream();
            int matched = 0;

            while (true)
            {
                int bytesRead = await stream.ReadAsync(oneByteBuffer, 0, 1, cancellationToken);
                if (bytesRead == 0)
                {
                    if (headerStream.Length == 0)
                        return null;

                    throw new EndOfStreamException("LSP header is incomplete.");
                }

                byte current = oneByteBuffer[0];
                headerStream.WriteByte(current);

                if (current == headerTerminator[matched])
                {
                    matched++;
                    if (matched == headerTerminator.Length)
                        break;
                }
                else
                {
                    matched = current == headerTerminator[0] ? 1 : 0;
                }
            }

            string headerText = Encoding.ASCII.GetString(headerStream.ToArray());
            int contentLength = -1;
            string[] headerLines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in headerLines)
            {
                int separatorIndex = line.IndexOf(':');
                if (separatorIndex <= 0)
                    continue;

                string name = line.Substring(0, separatorIndex).Trim();
                string value = line.Substring(separatorIndex + 1).Trim();
                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int parsedLength))
                {
                    contentLength = parsedLength;
                    break;
                }
            }

            if (contentLength < 0)
                throw new InvalidDataException("Missing Content-Length header.");

            byte[] payload = await ReadExactlyAsync(stream, contentLength, cancellationToken);
            return Encoding.UTF8.GetString(payload);
        }

        /// <summary>
        /// 从流中读取指定长度的字节，直到全部读满为止。
        /// </summary>
        private static async Task<byte[]> ReadExactlyAsync(Stream stream, int length, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[length];
            int offset = 0;

            while (offset < length)
            {
                int bytesRead = await stream.ReadAsync(buffer, offset, length - offset, cancellationToken);
                if (bytesRead == 0)
                    throw new EndOfStreamException("LSP payload is incomplete.");

                offset += bytesRead;
            }

            return buffer;
        }

        /// <summary>
        /// 分发并处理收到的LSP请求。
        /// </summary>
        private static async Task HandleRequestAsync(JObject request)
        {
            string? methodName = request["method"]?.ToObject<string>();
            if (string.IsNullOrEmpty(methodName))
                return;

            switch (methodName)
            {
                case "initialize":
                    await HandleInitializeAsync(request);
                    break;
                case "textDocument/didOpen":
                    await HandleDidOpenAsync(request);
                    if (string.IsNullOrEmpty(currentDocUri) == false)
                        await PublishDiagnosticsAsync(currentDocUri);
                    break;
                case "textDocument/didChange":
                    await HandleDidChangeAsync(request);
                    if (string.IsNullOrEmpty(currentDocUri) == false)
                        ScheduleDiagnostics(currentDocUri, 1000);
                    break;
                case "textDocument/completion":
                    await HandleCompletionAsync(request);
                    break;
                case "textDocument/documentHighlight":
                    await HandleHighlightingAsync(request);
                    break;
            }
        }

        /// <summary>
        /// 在切换文档时重置语言服务状态，并取消旧文档的延迟诊断。
        /// </summary>
        private static async Task SwitchDocumentAsync(string? uri)
        {
            if (currentDocUri == uri)
                return;

            await serviceMutex.WaitAsync();
            try
            {
                if (currentDocUri == uri)
                    return;

                CancelPendingDiagnostics();
                gizboxService.Reset();
                currentDocUri = uri;
            }
            finally
            {
                serviceMutex.Release();
            }
        }

        /// <summary>
        /// 将响应消息放入统一写队列，由后台单线程写出。
        /// </summary>
        private static void EnqueueMessage(string responseMessage)
        {
            writeQueue.Enqueue(responseMessage);
            writeSignal.Release();
        }

        /// <summary>
        /// 将对象序列化为LSP响应并放入输出队列。
        /// </summary>
        private static void EnqueueJsonRpcMessage(object response)
        {
            var responseJson = JsonConvert.SerializeObject(response);
            var responseMessage = LSPUtils.HttpResponsePlainText(Encoding.UTF8.GetByteCount(responseJson), responseJson);
            EnqueueMessage(responseMessage);
        }

        /// <summary>
        /// 后台异步消费写队列，避免忙等和多线程并发写stdout。
        /// </summary>
        public static async Task StartWriteQueueToOutstreamAsync()
        {
            while (true)
            {
                await writeSignal.WaitAsync();

                while (writeQueue.TryDequeue(out string? responseMsg))
                {
                    byte[] responseBytes = Encoding.UTF8.GetBytes(responseMsg);
                    await outputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                    await outputStream.FlushAsync();
                }
            }
        }

        /// <summary>
        /// 取消尚未发送的延迟诊断任务。
        /// </summary>
        private static void CancelPendingDiagnostics()
        {
            CancellationTokenSource? cts = null;
            lock (diagnosticMutex)
            {
                cts = diagnosticCts;
                diagnosticCts = null;
            }

            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        /// <summary>
        /// 调度带防抖的诊断发布，只保留最后一次修改后的任务。
        /// </summary>
        private static void ScheduleDiagnostics(string uri, int milisecDelay)
        {
            CancellationTokenSource nextCts = new CancellationTokenSource();
            CancellationTokenSource? oldCts = null;

            lock (diagnosticMutex)
            {
                oldCts = diagnosticCts;
                diagnosticCts = nextCts;
            }

            if (oldCts != null)
            {
                oldCts.Cancel();
                oldCts.Dispose();
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(milisecDelay, nextCts.Token);
                    if (nextCts.Token.IsCancellationRequested)
                        return;

                    await PublishDiagnosticsAsync(uri);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    LogToFile("!!ERR Handle Diagnostics Delay:" + ex);
                    LogToClient("!!ERR Handle Diagnostics Delay:" + ex);
                }
                finally
                {
                    lock (diagnosticMutex)
                    {
                        if (ReferenceEquals(diagnosticCts, nextCts))
                            diagnosticCts = null;
                    }

                    nextCts.Dispose();
                }
            });
        }

        /// <summary>
        /// 处理初始化请求并设置工作目录。
        /// </summary>
        private static async Task HandleInitializeAsync(JObject request)
        {
            try
            {
                string logStr;
                var folderInfos = (JArray?)request["params"]?["workspaceFolders"];
                if (folderInfos != null && folderInfos.Count > 0)
                {
                    string? uri = folderInfos[0]["uri"]?.ToObject<string>();
                    if (string.IsNullOrEmpty(uri) == false)
                    {
                        Uri uriObj = new Uri(uri);
                        string finalPath = uriObj.LocalPath;
                        if (finalPath.Length > 0 && finalPath[0] == '/')
                            finalPath = finalPath.Substring(1);

                        await serviceMutex.WaitAsync();
                        try
                        {
                            gizboxService.SetWorkFolder(finalPath);
                            logStr = "Set Work Folder:" + finalPath;
                        }
                        catch
                        {
                            logStr = "Invalid Work Folder:" + finalPath;
                        }
                        finally
                        {
                            serviceMutex.Release();
                        }
                    }
                    else
                    {
                        logStr = "Set Work Folder (uri null)";
                    }
                }
                else
                {
                    logStr = "Set Work Folder (workspaceFolders null)";
                }

                LogToFile(logStr);

                var response = new
                {
                    jsonrpc = "2.0",
                    id = (int?)request["id"],
                    result = new
                    {
                        capabilities = new
                        {
                            textDocumentSync = 2,
                            completionProvider = new { resolveProvider = true },
                            hoverProvider = true
                        }
                    }
                };

                EnqueueJsonRpcMessage(response);
            }
            catch (Exception e)
            {
                LogToClient("!!! Err Handle Init");
                LogToFile("!!! Err Handle Init");
                throw new Exception("Catch Err When Handle Init:" + e);
            }
        }

        /// <summary>
        /// 处理文档打开请求并立即刷新语言服务状态。
        /// </summary>
        private static async Task HandleDidOpenAsync(JObject request)
        {
            string? uri = request["params"]?["textDocument"]?["uri"]?.ToObject<string>();
            await SwitchDocumentAsync(uri);

            string txt = request["params"]?["textDocument"]?["text"]?.ToObject<string>() ?? string.Empty;

            await serviceMutex.WaitAsync();
            try
            {
                gizboxService.DidOpen(txt);
            }
            finally
            {
                serviceMutex.Release();
            }
        }

        /// <summary>
        /// 处理文档变更请求，并使用防抖方式延迟发布诊断。
        /// </summary>
        private static async Task HandleDidChangeAsync(JObject request)
        {
            try
            {
                string? uri = request["params"]?["textDocument"]?["uri"]?.ToObject<string>();
                await SwitchDocumentAsync(uri);

                var changes = (JArray?)request["params"]?["contentChanges"];
                if (changes == null)
                    throw new Exception("没有contentChanges字段：" + request);

                await serviceMutex.WaitAsync();
                try
                {
                    for (int i = 0; i < changes.Count; i++)
                    {
                        var change = (JObject)changes[i]!;
                        var jRange = change["range"]?.ToObject<JObject>();
                        bool fullUpdate = jRange == null;
                        string text = change["text"]?.ToObject<string>() ?? string.Empty;

                        if (fullUpdate == false)
                        {
                            int start_line = jRange!["start"]!["line"]!.ToObject<int>();
                            int start_char = jRange["start"]!["character"]!.ToObject<int>();
                            int end_line = jRange["end"]!["line"]!.ToObject<int>();
                            int end_char = jRange["end"]!["character"]!.ToObject<int>();

                            gizboxService.DidChange(start_line, start_char, end_line, end_char, text);
                        }
                        else
                        {
                            gizboxService.DidChange(-1, -1, -1, -1, text);
                        }
                    }
                }
                finally
                {
                    serviceMutex.Release();
                }
            }
            catch (Exception e)
            {
                LogToClient("!!ERR Handle did changes");
                LogToFile("!!ERR Handle did changes");
                throw new Exception("Catch Err When Handle DidChange:" + e);
            }
        }

        /// <summary>
        /// 处理自动补全请求并返回当前光标位置的补全项。
        /// </summary>
        private static async Task HandleCompletionAsync(JObject request)
        {
            try
            {
                string? uri = request["params"]?["textDocument"]?["uri"]?.ToObject<string>();
                await SwitchDocumentAsync(uri);

                var jposition = request["params"]?["position"]?.ToObject<JObject>();
                List<Completion> result;

                await serviceMutex.WaitAsync();
                try
                {
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
                            result = new List<Completion>()
                            {
                                new Completion()
                                {
                                    label = "DEBUG_ERR:" + ex,
                                    detail = ex.ToString(),
                                    insertText = "DEBUG_ERR:" + ex,
                                }
                            };
                            LogToFile("!!GetCompletionErr:" + ex);
                        }
                    }
                    else
                    {
                        result = new List<Completion>()
                        {
                            new Completion()
                            {
                                label = "DEBUG_NO_COMPLETION",
                                kind = ComletionKind.Field,
                                detail = "no completion",
                                documentation = "",
                                insertText = "",
                            }
                        };
                    }
                }
                finally
                {
                    serviceMutex.Release();
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

                EnqueueJsonRpcMessage(response);
                LogToClient($"Handle Completion:{result.Count}");
            }
            catch (Exception e)
            {
                LogToClient("!!ERR Handle Completion");
                LogToFile("!!ERR Handle Completion");
                throw new Exception("Catch Err When Handle Completion:" + e);
            }
        }

        /// <summary>
        /// 处理高亮请求并返回当前文档中的高亮范围。
        /// </summary>
        private static async Task HandleHighlightingAsync(JObject request)
        {
            try
            {
                string? uri = request["params"]?["textDocument"]?["uri"]?.ToObject<string>();
                await SwitchDocumentAsync(uri);

                int line = request["params"]?["position"]?["line"]?.ToObject<int>() ?? 0;
                int character = request["params"]?["position"]?["character"]?.ToObject<int>() ?? 0;
                List<HighLightToken> highlights;

                await serviceMutex.WaitAsync();
                try
                {
                    highlights = gizboxService.GetHighlights(line, character);
                }
                finally
                {
                    serviceMutex.Release();
                }

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

                EnqueueJsonRpcMessage(response);
            }
            catch (Exception e)
            {
                LogToClient("!!ERR Handle Highlight");
                LogToFile("!!ERR Handle Highlight");
                throw new Exception("Catch Err When Handle Highlight:" + e);
            }
        }

        /// <summary>
        /// 发布当前文档的诊断信息。
        /// </summary>
        private static async Task PublishDiagnosticsAsync(string uri)
        {
            if (string.IsNullOrEmpty(uri))
                return;

            try
            {
                DiagnosticInfo? diagnostic = null;

                await serviceMutex.WaitAsync();
                try
                {
                    if (gizboxService.tempDiagnosticInfo != null)
                    {
                        diagnostic = new DiagnosticInfo()
                        {
                            code = gizboxService.tempDiagnosticInfo.code,
                            startLine = gizboxService.tempDiagnosticInfo.startLine,
                            startChar = gizboxService.tempDiagnosticInfo.startChar,
                            endLine = gizboxService.tempDiagnosticInfo.endLine,
                            endChar = gizboxService.tempDiagnosticInfo.endChar,
                            message = gizboxService.tempDiagnosticInfo.message,
                            severity = gizboxService.tempDiagnosticInfo.severity,
                        };
                    }
                }
                finally
                {
                    serviceMutex.Release();
                }

                object response;
                if (diagnostic != null)
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
                                new
                                {
                                    range = new
                                    {
                                        start = new
                                        {
                                            line = diagnostic.startLine,
                                            character = diagnostic.startChar,
                                        },
                                        end = new
                                        {
                                            line = diagnostic.endLine,
                                            character = diagnostic.endChar,
                                        }
                                    },
                                    severity = diagnostic.severity,
                                    code = Escape(diagnostic.code),
                                    source = "gizbox",
                                    message = Escape(diagnostic.message),
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
                            diagnostics = Array.Empty<object>()
                        }
                    };
                }

                EnqueueJsonRpcMessage(response);
            }
            catch (Exception e)
            {
                LogToClient("!!ERR Handle Diagnostics");
                LogToFile("!!ERR Handle Diagnostics");
                throw new Exception("Catch Err When Handle Diagnostics:" + e);
            }
        }

        /// <summary>
        /// 转义日志文本中的引号，避免调试消息难以阅读。
        /// </summary>
        private static string Escape(string? input)
        {
            string result = input ?? string.Empty;
            result = result.Replace("\"", "*");
            result = result.Replace("'", "*");
            return result;
        }

        /// <summary>
        /// 发送调试日志到客户端的自定义通知通道。
        /// </summary>
        private static void LogToClient(string text)
        {
            var response = new
            {
                jsonrpc = "2.0",
                method = "debug/log",
                @params = new
                {
                    text = $"{Escape(text)} ({System.DateTime.Now})",
                }
            };

            EnqueueJsonRpcMessage(response);
        }

        private static void CleanFileLog()
        {
            return;

            string path = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop) + "\\lsp_log.txt";
            if (System.IO.File.Exists(path))
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
