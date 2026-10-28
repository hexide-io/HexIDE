using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using System.Text;
using System.Text.Json;
using HexIDE.Conversations;
using HexIDE.Forms.Views;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class SignatureHelpShapeTests
{
    [Theory]
    [InlineData("[10,11]")]
    [InlineData("\"x\"")]
    public async Task ParameterLabelsCrossTheWireAndReachThePopup(string label)
    {
        await WithAnswer(label, async (client, log) =>
        {
            var help = await client.RequestSignatureHelpAsync("file:///probe.bas", new Position(0, 0),
                TestContext.Current.CancellationToken);
            help.Should().NotBeNull();
            var provider = new VbSignatureProvider(help!);
            CheckHeader(provider, "F(x As T, x As T)", label.StartsWith('[') ? 10 : 2, "x");
            provider.CurrentContent.Should().Be("x: parameter docs");
            using var roundTrip = JsonDocument.Parse(JsonSerializer.Serialize(help, LspJsonContext.Default.SignatureHelp));
            roundTrip.RootElement.GetProperty("signatures")[0].GetProperty("parameters")[0]
                .GetProperty("label").GetRawText().Should().Be(label);
            await log.DrainAsync();
            var entry = log.Snapshot("probe").Single(e => e.Method == "textDocument/signatureHelp");
            entry.Outcome.Should().Be(ConversationOutcome.Answered);
            var body = log.Body("probe", entry.AnswerSequence!.Value)!;
            using var frame = JsonDocument.Parse(Encoding.UTF8.GetString(body.Head));
            frame.RootElement.GetProperty("result").GetProperty("signatures")[0]
                .GetProperty("parameters")[0].GetProperty("label").GetRawText().Should().Be(label);
        });
    }

    // Measured Quick Info presentation and examples: hexide-io/HexIDE#2632.
    [Theory]
    [InlineData("Mid(String, Start As Long, [Length] As Long) As String", "[Length] As Long", 2)]
    [InlineData("CallByName(Object, ProcName, CallType, Args...)", "Object", 0)]
    public void ActiveParameterIsBoldWithoutChangingTheLabel(string label, string parameter, int active)
    {
        var parameters = active == 0
            ? new[] { new ParameterInformation(parameter) }
            : [new ParameterInformation("String"), new ParameterInformation("Start As Long"), new ParameterInformation(parameter)];
        var provider = new VbSignatureProvider(new SignatureHelp(
            [new SignatureInformation(label, null, parameters)], 0, active));

        CheckHeader(provider, label, label.IndexOf(parameter, StringComparison.Ordinal), parameter);
    }

    private static void CheckHeader(VbSignatureProvider provider, string label, int start, string parameter)
    {
        var header = provider.CurrentHeader.Should().BeOfType<TextBlock>().Which;
        var runs = header.Inlines!.Cast<Run>().ToArray();
        string.Concat(runs.Select(r => r.Text)).Should().Be(label);
        runs.Should().HaveCount(3);
        runs[0].Text.Should().Be(label[..start]);
        runs[1].Text.Should().Be(parameter);
        runs[2].Text.Should().Be(label[(start + parameter.Length)..]);
        runs[1].FontWeight.Should().Be(FontWeight.Bold);
        runs.Where((_, i) => i != 1).Should().OnlyContain(r => r.FontWeight == FontWeight.Normal);
    }

    [Fact]
    public async Task OffsetsOutsideTheSignatureDoNotCrashThePopup()
    {
        await WithAnswer("[99,100]", async (client, _) =>
        {
            var help = await client.RequestSignatureHelpAsync("file:///probe.bas", new Position(0, 0),
                TestContext.Current.CancellationToken);
            help.Should().NotBeNull();
            var provider = new VbSignatureProvider(help!);
            provider.CurrentHeader.Should().Be("F(x As T, x As T)");
            provider.CurrentContent.Should().Be(": parameter docs");
        });
    }

    [Fact]
    public async Task AnUnreadableAnswerIsRecordedAsDroppedAndTheConnectionSurvives()
    {
        await WithAnswer("true", async (client, log) =>
        {
            (await client.RequestSignatureHelpAsync("file:///probe.bas", new Position(0, 0),
                TestContext.Current.CancellationToken)).Should().BeNull();
            await log.DrainAsync();
            var entry = log.Snapshot("probe").Single(e => e.Method == "textDocument/signatureHelp");
            entry.Outcome.Should().Be(ConversationOutcome.Failed);
            entry.Detail.Should().Contain("answer dropped");
            entry.Fault.Should().BeTrue();
            entry.AnswerSequence.Should().NotBeNull();
            client.IsRunning.Should().BeTrue();
            var next = await client.RequestSignatureHelpAsync("file:///probe.bas", new Position(0, 0),
                TestContext.Current.CancellationToken);
            next.Should().NotBeNull("a dropped result must not prevent the next request being answered");
        });
    }

    private static async Task WithAnswer(string label, Func<VBLspClient, ConversationLog, Task> check)
    {
        await using var log = new ConversationLog();
        log.Arm("probe", true);
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        using var server = new JsonRpc(new HeaderDelimitedMessageHandler(serverSide, serverSide,
            new SystemTextJsonFormatter()), new Server(label));
        server.StartListening();
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));
        await using var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(),
            "vb6", capture: log, connectionId: "probe");
        await client.StartAsync(TestContext.Current.CancellationToken);
        try { await check(client, log); }
        finally { server.Dispose(); }
    }

    private sealed class Server(string label)
    {
        private int _requests;
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            JsonDocument.Parse("""{"capabilities":{"signatureHelpProvider":{}}}""").RootElement.Clone();

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        [JsonRpcMethod("textDocument/signatureHelp", UseSingleObjectParameterDeserialization = true)]
        public JsonElement SignatureHelp(JsonElement _) => JsonDocument.Parse(
            "{\"signatures\":[{\"label\":\"F(x As T, x As T)\",\"parameters\":[{\"label\":" + (_requests++ == 0 ? label : "\"x\"")
            + ",\"documentation\":\"parameter docs\"}]}],\"activeParameter\":0}").RootElement.Clone();
    }
}
