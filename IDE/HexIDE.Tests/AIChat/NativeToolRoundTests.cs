using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using HexIDE.Addins;
using HexIDE.AIChat;
using HexIDE.AIChat.Views;

namespace HexIDE.Tests.AIChat;

public class NativeToolRoundTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToolRoundFreezesSystemAndDefersCompaction(bool compact)
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "chat-settings.json"),
                new JsonObject { ["apiKey"] = "test-key", ["maxTurnsBeforeCompaction"] = compact ? 2 : 100 }.ToJsonString());
            var host = Substitute.For<IHexIdeHost>();
            var editor = Substitute.For<IEditorAccess>();
            host.Editor.Returns(editor);
            editor.GetActiveDocument().Returns(new AddinDocument("first", "first.bas", "first", AddinDocumentKind.Module));
            var handler = new RoundHandler(() => editor.GetActiveDocument().Returns(
                new AddinDocument("changed", "changed.bas", "changed", AddinDocumentKind.Module)));
            using var client = new ChatClient(new HttpClient(handler));
            var chat = new ChatViewModel(host, directory);
            typeof(ChatViewModel).GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(chat, client);
            chat.InputText = "first message";
            await chat.SendAsync();
            handler.Bodies.Should().HaveCount(2, "compaction must not run between a tool call and its continuation");
            handler.Bodies[0]["system"]!.ToJsonString().Should().Be(handler.Bodies[1]["system"]!.ToJsonString());
            handler.Bodies[1]["messages"]![1]!["content"]![0]!["signature"]!.GetValue<string>().Should().Be("signed");
            chat.InputText = "next message";
            await chat.SendAsync();
            var final = handler.Bodies.Last();
            final["system"]!.ToJsonString().Should().Contain("changed");
            final["messages"]!.ToJsonString().Should().NotContain("signed");
            handler.Bodies.Should().HaveCount(compact ? 4 : 3);
            chat.Messages.Should().NotContain(m => m.IsError);
        }
        finally { Directory.Delete(directory, true); }
    }

    [AvaloniaTheory]
    [InlineData("refusal", "details", "localized refusal details")]
    [InlineData("refusal", null, "localized refusal")]
    [InlineData("max_tokens", null, "localized token limit")]
    [InlineData("http", null, "Select a workspace.")]
    public async Task FailedRepliesAreVisibleAndFinishTheStreamingBubble(string reason, string? explanation, string expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        var resources = Application.Current!.Resources;
        var keys = new[] { "Str.AIChat.Refusal", "Str.AIChat.TokenLimit" };
        var previous = keys.Select(k => resources.ContainsKey(k) ? resources[k] : null).ToArray();
        resources[keys[0]] = "localized refusal";
        resources[keys[1]] = "localized token limit";
        try
        {
            File.WriteAllText(Path.Combine(directory, "chat-settings.json"), """{"apiKey":"test-key"}""");
            var chat = new ChatViewModel(Substitute.For<IHexIdeHost>(), directory) { InputText = "hello" };
            using var client = new ChatClient(new HttpClient(new StopHandler(reason, explanation)));
            typeof(ChatViewModel).GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(chat, client);
            await chat.SendAsync();
            chat.Messages.Should().ContainSingle(m => m.IsError && m.RawContent.Contains(expected));
            chat.Messages.Should().NotContain(m => m.IsStreaming);
            chat.IsResponding.Should().BeFalse();
        }
        finally
        {
            for (var i = 0; i < keys.Length; i++)
                if (previous[i] is { } value) resources[keys[i]] = value;
                else resources.Remove(keys[i]);
            Directory.Delete(directory, true);
        }
    }

    private sealed class StopHandler(string reason, string? explanation) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var delta = new JsonObject
            {
                ["type"] = "message_delta",
                ["delta"] = new JsonObject
                {
                    ["stop_reason"] = reason,
                    ["stop_details"] = new JsonObject { ["type"] = "refusal", ["explanation"] = explanation }
                }
            };
            return Task.FromResult(new HttpResponseMessage(reason == "http" ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
            {
                Content = new StringContent(reason == "http"
                    ? """{"type":"error","error":{"message":"Select a workspace."}}"""
                    : "data: " + delta.ToJsonString() + "\n\ndata: {\"type\":\"message_stop\"}\n\n")
            });
        }
    }

    private sealed class RoundHandler(Action changeDocument) : HttpMessageHandler
    {
        public List<JsonObject> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject());
            var first = Bodies.Count == 1;
            if (first) changeDocument();
            var stream = first ? """
                data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"reason","signature":"signed"}}

                data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"one","name":"get_active_file","input":{}}}

                data: {"type":"message_delta","delta":{"stop_reason":"tool_use"}}

                """ : "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"}}\n";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(stream + "\ndata: {\"type\":\"message_stop\"}\n\n", Encoding.UTF8, "text/event-stream")
            };
        }
    }
}
