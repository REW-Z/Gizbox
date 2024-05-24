
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

using Gizbox.LSP.Models;
using System.Text.RegularExpressions;

namespace Gizbox.LSP.Models
{
    public class DidChangeTextDocumentParams
    {
        [JsonProperty("textDocument")]
        public TextDocumentIdentifier TextDocument { get; set; }

        [JsonProperty("contentChanges")]
        public List<TextDocumentContentChangeEvent> ContentChanges { get; set; }
    }

    public class TextDocumentIdentifier
    {
        [JsonProperty("uri")]
        public string Uri { get; set; }
    }

    public class TextDocumentContentChangeEvent
    {
        [JsonProperty("text")]
        public string Text { get; set; }
    }

    public class LspRequest
    {
        [JsonProperty("jsonrpc")]
        public string Jsonrpc { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("params")]
        public object Params { get; set; }
    }

    public class LspResponse
    {
        [JsonProperty("jsonrpc")]
        public string Jsonrpc { get; set; }

        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("result")]
        public object Result { get; set; }

        [JsonProperty("error")]
        public object Error { get; set; }
    }
}


class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var inputStream = Console.OpenStandardInput();
        var outputStream = Console.OpenStandardOutput();
        var buffer = new byte[4096];
        var messageBuilder = new StringBuilder();

        while (true)
        {
            var bytesRead = await inputStream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead == 0) break;

            var messagePart = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            messageBuilder.Append(messagePart);

            var messageContent = GetJsonContent(messageBuilder.ToString());
            if (messageContent == null) continue;

            Console.WriteLine($"Received message: {messageContent}");

            var lspRequest = JsonConvert.DeserializeObject<LspRequest>(messageContent);
            if (lspRequest != null && lspRequest.Method == "textDocument/didChange")
            {
                var didChangeParams = JsonConvert.DeserializeObject<DidChangeTextDocumentParams>(lspRequest.Params.ToString());
                HandleDidChangeTextDocument(didChangeParams, outputStream);
            }

            var response = new LspResponse
            {
                Jsonrpc = "2.0",
                Id = lspRequest.Id,
                Result = null
            };

            var responseJson = JsonConvert.SerializeObject(response);
            var responseMessage = $"Content-Length: {Encoding.UTF8.GetByteCount(responseJson)}\r\n\r\n{responseJson}";
            var responseBytes = Encoding.UTF8.GetBytes(responseMessage);
            await outputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
            messageBuilder.Clear();
        }
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

    private static async void HandleDidChangeTextDocument(DidChangeTextDocumentParams parameters, Stream outputStream)
    {
        foreach (var change in parameters.ContentChanges)
        {
            if (change.Text.Trim() == "hello")
            {
                // Create completion response
                var newText = "hello world";
                var response = new
                {
                    jsonrpc = "2.0",
                    method = "textDocument/applyEdits",
                    @params = new
                    {
                        edits = new[]
                        {
                            new
                            {
                                range = new
                                {
                                    start = new { line = 0, character = 0 },
                                    end = new { line = 0, character = change.Text.Length }
                                },
                                newText
                            }
                        }
                    }
                };

                var responseJson = JsonConvert.SerializeObject(response);
                var responseMessage = $"Content-Length: {Encoding.UTF8.GetByteCount(responseJson)}\r\n\r\n{responseJson}";
                var responseBytes = Encoding.UTF8.GetBytes(responseMessage);
                await outputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
                Console.WriteLine($"Sent completion: {responseJson}");
            }
        }
    }
}