using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using HexIDE.Addins;
using HexIDE.AIChat;
using HexIDE.AIChat.Views;

namespace HexIDE.Tests.AIChat;

public class ChatCredentialTests
{
    [Theory]
    [InlineData(null, "", null, false)]
    [InlineData(null, "   ", null, false)]
    [InlineData("https://remote.example/localhost", "", null, false)]
    [InlineData("http://localhost:11434/v1", "", null, true)]
    [InlineData("http://127.0.0.1:1234/v1", "", null, true)]
    [InlineData("http://[::1]:1234/v1", "", null, true)]
    [InlineData("http://127.0.0.2:1234/v1", "", null, true)]
    [InlineData("http://localhost.example.com/v1", "", null, false)]
    [InlineData(null, "", "HEXIDE_AI_API_KEY", true)]
    [InlineData(null, "", "ANTHROPIC_API_KEY", true)]
    [InlineData(null, "", "OPENAI_API_KEY", true)]
    [InlineData(null, "file-key", null, true)]
    public async Task SendRequiresACredentialBeforeReadingContext(string? endpoint, string key, string? environmentKey, bool allowed)
    {
        string[] names = ["HEXIDE_AI_API_KEY", "ANTHROPIC_API_KEY", "OPENAI_API_KEY"];
        var previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var name in names) Environment.SetEnvironmentVariable(name, null);
            if (environmentKey is not null) Environment.SetEnvironmentVariable(environmentKey, "environment-key");
            var settings = new ChatSettings { ApiKey = key, MaxTurnsBeforeCompaction = 2 };
            if (endpoint is not null) settings.BaseUrl = endpoint;
            File.WriteAllText(Path.Combine(directory, "chat-settings.json"), JsonSerializer.Serialize(settings));
            var session = new ChatSession();
            for (var i = 0; i < 6; i++) session.Add(new ConversationMessage("user", "earlier"));
            session.SaveTo(Path.Combine(directory, "chat-history.json"));
            using var handler = new RecordingHandler();
            using var client = new ChatClient(new HttpClient(handler));
            var host = Substitute.For<IHexIdeHost>();
            var chat = new ChatViewModel(host, directory, client) { InputText = "private message" };

            await chat.SendAsync();

            handler.Requests.Should().Be(allowed ? 2 : 0, "compaction and conversation both require authorization");
            if (!allowed)
            {
                host.Editor.DidNotReceive().GetActiveDocument();
                chat.Messages.Last().IsError.Should().BeTrue();
                chat.Messages.Last().RawContent.Should().Be("Str.AIChat.ApiKeyRequired");
                chat.InputText.Should().Be("private message");
                chat.SessionLog.Should().HaveCount(6);
            }
            else host.Editor.Received(1).GetActiveDocument();
        }
        finally
        {
            for (var i = 0; i < names.Length; i++) Environment.SetEnvironmentVariable(names[i], previous[i]);
            Directory.Delete(directory, true);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.Host == "api.anthropic.com"
                    ? "data: {\"type\":\"message_stop\"}\n\n" : "data: [DONE]\n\n")
            });
        }
    }
}
