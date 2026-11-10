using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using HexIDE.AIChat;

namespace HexIDE.Tests.AIChat;

public class NativeMessagesTests
{
    private const string Done = """
        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn"}}

        event: message_stop
        data: {"type":"message_stop"}


        """;

    [Fact]
    public async Task NativeRequestUsesDocumentedHeadersAndTranslatesTheConversation()
    {
        var handler = new RecordingHandler(Done);
        using var client = new ChatClient(new HttpClient(handler));
        var messages = new List<JsonObject>
        {
            new() { ["role"] = "system", ["content"] = "system prompt" },
            new() { ["role"] = "user", ["content"] = "hello" },
            new ConversationMessage("assistant", "checking", [new ToolCall("one", "read", "{\"file\":\"Form1\"}"),
                new ToolCall("two", "read", "{}")]).ToApiJson(),
            new ConversationMessage("tool", "first result", toolCallId: "one").ToApiJson(),
            new ConversationMessage("tool", "second result", toolCallId: "two").ToApiJson()
        };
        var tools = JsonNode.Parse("""[{"type":"function","function":{"name":"read","description":"Read a file","parameters":{"type":"object"}}}]""")!.AsArray();
        await Collect(client, messages, tools, new ChatSettings
        {
            MaxTokens = 1234, Thinking = new JsonObject { ["type"] = "adaptive" }, Effort = "high"
        });

        handler.Url.Should().Be("https://api.anthropic.com/v1/messages");
        handler.Authorization.Should().Be("Bearer test-key");
        handler.Version.Should().Be("2026-10-21");
        handler.Accept.Should().Be("text/event-stream");
        handler.ContentType.Should().Be("application/json; charset=utf-8");
        JsonNode.DeepEquals(JsonNode.Parse(handler.Body), JsonNode.Parse("""
            {"model":"configured-model","stream":true,"max_tokens":1234,
             "system":[{"type":"text","text":"system prompt"}],
             "messages":[{"role":"user","content":[{"type":"text","text":"hello"}]},
               {"role":"assistant","content":[{"type":"text","text":"checking"},
                 {"type":"tool_use","id":"one","name":"read","input":{"file":"Form1"}},
                 {"type":"tool_use","id":"two","name":"read","input":{}}]},
               {"role":"user","content":[{"type":"tool_result","tool_use_id":"one","content":"first result"},
                 {"type":"tool_result","tool_use_id":"two","content":"second result"}]}],
             "tools":[{"name":"read","description":"Read a file","input_schema":{"type":"object"}}],
             "thinking":{"type":"adaptive"},"output_config":{"effort":"high"}}
            """)).Should().BeTrue();
    }

    [Fact]
    public async Task DefaultsLeaveThinkingAndEffortToTheConfiguredModel()
    {
        var handler = new RecordingHandler(Done);
        using var client = new ChatClient(new HttpClient(handler));
        await Collect(client);
        var body = JsonNode.Parse(handler.Body)!;
        body["max_tokens"]!.GetValue<int>().Should().Be(64000);
        body["thinking"].Should().BeNull();
        body["output_config"].Should().BeNull();
        body["tools"].Should().BeNull();
    }

    [Fact]
    public async Task TextAndSplitToolInputAreReadAndSignedThinkingSurvivesHistoryAndNextRequest()
    {
        // Wire shapes from the streaming reference; synthetic IDs, signature and payload, no live API.
        var stream = """
            event: message_start
            data: {"type":"message_start","message":{"content":[]}}

            event: ping
            data: {"type":"ping"}

            event: content_block_start
            data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","signature":""}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"Consider the file"}}

            data: {"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"signed"}}

            data: {"type":"content_block_stop","index":0}

            data: {"type":"content_block_start","index":1,"content_block":{"type":"text","text":""}}

            data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"Reading"}}

            data: {"type":"content_block_stop","index":1}

            data: {"type":"content_block_start","index":2,"content_block":{"type":"tool_use","id":"call_1","name":"read","input":{}}}

            data: {"type":"content_block_delta","index":2,"delta":{"type":"input_json_delta","partial_json":"{\"file\":"}}

            data: {"type":"content_block_delta","index":2,"delta":{"type":"input_json_delta","partial_json":"\"Form1\"}"}}

            data: {"type":"content_block_stop","index":2}

            data: {"type":"future_event"}

            data: {"type":"message_delta","delta":{"stop_reason":"tool_use"}}

            data: {"type":"message_stop"}


            """;
        var handler = new RecordingHandler(stream);
        using var client = new ChatClient(new HttpClient(handler));
        var chunks = await Collect(client);
        chunks.Where(c => c.ContentDelta is not null).Select(c => c.ContentDelta).Should().Equal("Reading");
        var call = chunks.Single(c => c.FinishedToolCalls is not null).FinishedToolCalls!.Single();
        call.Id.Should().Be("call_1");
        call.Name.Should().Be("read");
        call.ArgumentsJson.Should().Be("{\"file\":\"Form1\"}");
        chunks.Last().IsDone.Should().BeTrue();
        var native = chunks.Last().NativeContent!;
        native[0]!["signature"]!.GetValue<string>().Should().Be("signed");
        native[0]!["thinking"]!.GetValue<string>().Should().Be("Consider the file");
        var session = new ChatSession();
        session.Add(new ConversationMessage("assistant", "Reading", [call]) { NativeContent = native });
        session.Add(new ConversationMessage("tool", "file content", toolCallId: call.Id));
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "history.json");
            session.SaveTo(path);
            var restored = ChatSession.LoadFrom(path);
            handler.Stream = Done;
            await Collect(client, restored.BuildApiMessages("system"));
            var body = JsonNode.Parse(handler.Body)!;
            JsonNode.DeepEquals(body["messages"]![0]!["content"], native).Should().BeTrue();
            body["messages"]![1]!["content"]![0]!["tool_use_id"]!.GetValue<string>().Should().Be("call_1");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("data: {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}\n\n", true)]
    [InlineData("", false)]
    public async Task FailureAfterTextDoesNotCompleteOrPublishTools(string ending, bool providerError)
    {
        var handler = new RecordingHandler("""
            data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":"partial"}}

            data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"one","name":"read","input":{}}}


            """ + ending);
        using var client = new ChatClient(new HttpClient(handler));
        var chunks = new List<StreamChunk>();
        Func<Task> read = async () =>
        {
            await foreach (var chunk in client.StreamAsync("https://api.anthropic.com/v1", "test-key", "model", [], [], TestContext.Current.CancellationToken))
                chunks.Add(chunk);
        };
        if (providerError) (await read.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Contain("overloaded_error");
        else await read.Should().ThrowAsync<EndOfStreamException>();
        chunks.Select(c => c.ContentDelta).Should().Equal("partial");
        chunks.Should().NotContain(c => c.IsDone || c.FinishedToolCalls != null);
    }

    [Theory]
    [InlineData("https://api.openai.com/v1")]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://127.0.0.1:1234/v1")]
    [InlineData("https://api.anthropic.com.example/v1")]
    public async Task OtherHostsKeepTheOpenAiWireFormat(string endpoint)
    {
        var handler = new RecordingHandler("data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n\ndata: [DONE]\n\n");
        using var client = new ChatClient(new HttpClient(handler));
        var message = new ConversationMessage("assistant", "previous")
        {
            NativeContent = new JsonArray(new JsonObject { ["type"] = "thinking", ["thinking"] = "private protocol metadata" })
        };
        var chunks = new List<StreamChunk>();
        await foreach (var chunk in client.StreamAsync(endpoint, "test-key", "model", [message.ToApiJson()], [], TestContext.Current.CancellationToken))
            chunks.Add(chunk);
        handler.Url.Should().Be(endpoint + "/chat/completions");
        handler.Version.Should().BeNull();
        handler.Authorization.Should().Be("Bearer test-key");
        handler.Body.Should().Be("""{"model":"model","stream":true,"messages":[{"role":"assistant","content":"previous"}]}""");
        chunks[0].ContentDelta.Should().Be("hello");
        chunks.Last().IsDone.Should().BeTrue();
    }

    [Fact]
    public async Task EmptyHistoricalToolArgumentsBecomeAnEmptyObject()
    {
        var handler = new RecordingHandler(Done);
        using var client = new ChatClient(new HttpClient(handler));
        await Collect(client, [new ConversationMessage("assistant", "", [new ToolCall("one", "read", "")]).ToApiJson()]);
        handler.Body.Should().Contain("\"input\":{}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrkspc_test")]
    public async Task WorkspaceHeaderIsOptionalAndNativeOnly(string? workspace)
    {
        var handler = new RecordingHandler(Done);
        using var client = new ChatClient(new HttpClient(handler));
        var settings = System.Text.Json.JsonSerializer.Deserialize<ChatSettings>(
            new JsonObject { ["workspaceId"] = workspace }.ToJsonString())!;
        await Collect(client, settings: settings);
        handler.Workspace.Should().Be(workspace);
        handler.Stream = "data: [DONE]\n\n";
        await foreach (var _ in client.StreamAsync("https://api.openai.com/v1", "test-key", "model", [], [],
                           TestContext.Current.CancellationToken, settings)) { }
        handler.Workspace.Should().BeNull();
    }

    [Theory]
    [InlineData("refusal", "This request was declined.")]
    [InlineData("refusal", null)]
    [InlineData("max_tokens", null)]
    public async Task UnsuccessfulStopsUseTheStreamFailureRoute(string reason, string? explanation)
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
        var handler = new RecordingHandler("data: " + delta.ToJsonString() + "\n\ndata: {\"type\":\"message_stop\"}\n\n");
        using var client = new ChatClient(new HttpClient(handler));
        Func<Task> read = async () => await Collect(client);
        var error = (await read.Should().ThrowAsync<Exception>()).Which;
        error.Message.Should().Contain(explanation ?? (reason == "refusal" ? "Str.AIChat.Refusal" : "Str.AIChat.TokenLimit"));
    }

    [Fact]
    public async Task HttpFailureExposesTheApiMessage()
    {
        var handler = new RecordingHandler("""{"type":"error","error":{"type":"invalid_request_error","message":"Select a workspace."}}""")
        { Status = HttpStatusCode.BadRequest };
        using var client = new ChatClient(new HttpClient(handler));
        Func<Task> read = async () => await Collect(client);
        (await read.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Be("Select a workspace.");
    }

    private static async Task<List<StreamChunk>> Collect(ChatClient client, IReadOnlyList<JsonObject>? messages = null,
        JsonArray? tools = null, ChatSettings? settings = null)
    {
        var chunks = new List<StreamChunk>();
        await foreach (var chunk in client.StreamAsync("https://api.anthropic.com/v1", "test-key", "configured-model",
            messages ?? [new JsonObject { ["role"] = "user", ["content"] = "hello" }], tools ?? [],
            TestContext.Current.CancellationToken, settings)) chunks.Add(chunk);
        return chunks;
    }

    private sealed class RecordingHandler(string stream) : HttpMessageHandler
    {
        public string Stream { get; set; } = stream;
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string? Workspace { get; private set; }
        public string? Url { get; private set; }
        public string? Authorization { get; private set; }
        public string? Version { get; private set; }
        public string? Accept { get; private set; }
        public string? ContentType { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.AbsoluteUri;
            Workspace = request.Headers.TryGetValues("anthropic-workspace-id", out var workspace) ? workspace.Single() : null;
            Authorization = request.Headers.Authorization?.ToString();
            Version = request.Headers.TryGetValues("anthropic-version", out var versions) ? versions.Single() : null;
            Accept = request.Headers.Accept.ToString();
            ContentType = request.Content!.Headers.ContentType!.ToString();
            Body = await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status)
            {
                Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(Stream)))
            };
        }
    }
}
