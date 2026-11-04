using System.Text.Json;
using HexIDE.Conversations;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.LspClient;

/// <summary>Measured close publications and teardown codes; see docs/foreign-language-servers.md (#491).</summary>
public class RealServerShutdownTests
{
    [ForeignServerFact("markdown")]
    public Task MarkdownClosesAndExits() => Check(ForeignServer.Markdown, "notes.md",
        "##Heading without a space\n\n\n\nsome text\n\n\n", 0);

    [ForeignServerFact("latex")]
    public Task LatexClosesAndExits() => Check(ForeignServer.Latex, "document.tex",
        "\\documentclass{article}\n\\begin{document}\n\\begin{itemize}\n\\end{document}\n", 1);

    [ForeignServerFact("json")]
    public Task JsonClosesAndExits() => Check(ForeignServer.Json, "settings.json", "{ \"a\": 1, }", null);

    [ForeignServerFact("cpp")]
    public Task CppClosesAndExits() => Check(ForeignServer.Cpp, "main.cpp", "int main() { return x; }\n", 0);

    [ForeignServerFact("python")]
    public Task PythonClosesAndExits() => Check(ForeignServer.Python, "main.py", "import os\n\n\ndef greet(name):\n    return name\n", 0);

    [BundledServerFact]
    public Task BundledClosesAndExits() => Check(null, "Module1.bas", "Sub Main()\n    Dim x As\nEnd Sub\n", 0);

    private static async Task Check(ForeignServer? server, string name, string text, int? diagnosticsAfterClose)
    {
        var token = TestContext.Current.CancellationToken;
        var id = server?.LanguageId ?? "vb6";
        var dir = Path.Combine(Path.GetTempPath(), "hexide-shutdown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        await using var capture = new ConversationLog();
        capture.Arm(id, true);
        using var logging = LoggerFactory.Create(b => { });
        var transport = new StdioProcessLspTransport(
            new LspServerInfo(server?.Find() ?? BundledServer.Find()!, server?.LaunchArguments ?? "", dir),
            logging.CreateLogger<StdioProcessLspTransport>());
        await using var client = new VBLspClient(transport, logging.CreateLogger<VBLspClient>(), id,
            capture: capture, connectionId: id);
        var file = Path.Combine(dir, name);
        await File.WriteAllTextAsync(file, text, token);
        var uri = LspDocumentUri.ForFile(file);
        try
        {
            await client.StartAsync(token);
            await client.OpenDocumentAsync(uri, text, token);
            await WaitUntil(() => HasFindings(), TimeSpan.FromSeconds(30));
            HasFindings().Should().BeTrue("the server must diagnose the opened document on the wire: {0}",
                string.Join(" | ", capture.Snapshot(id).Select(e => $"{e.Kind} {e.Method} {e.Detail}")));

            await capture.DrainAsync();
            var beforeClose = capture.Snapshot(id).Last().Sequence;
            await client.CloseDocumentAsync(uri, token);
            // Silence is a measurement too: observe the same bounded window for each server.
            await Task.Delay(TimeSpan.FromSeconds(3), token);
            await capture.DrainAsync();
            var closed = capture.Snapshot(id).Where(e => e.Sequence > beforeClose
                && e.Method == "textDocument/didClose" && e.Direction == ConversationDirection.Sent).Single();
            Frame(closed.Sequence).GetProperty("params").GetProperty("textDocument")
                .GetProperty("uri").GetString().Should().Be(uri);
            var publications = capture.Snapshot(id).Where(e => e.Sequence > closed.Sequence
                && e.Method == "textDocument/publishDiagnostics" && e.Direction == ConversationDirection.Received
                && e.Kind == ConversationEntryKind.Notification)
                .Select(e => Frame(e.Sequence).GetProperty("params").Clone()).ToArray();

            await client.StopAsync();
            await capture.DrainAsync();
            var lifecycle = capture.Snapshot(id).Where(e => e.Kind == ConversationEntryKind.Lifecycle)
                .Select(e => e.Detail).ToArray();
            TestContext.Current.TestOutputHelper!.WriteLine($"{id}: close publications: {string.Join(" | ", publications.Select(p => p.GetRawText()))}; lifecycle: {string.Join(" | ", lifecycle)}");
            if (diagnosticsAfterClose is null)
                publications.Should().BeEmpty("the JSON server published nothing in the measured three-second window");
            else
            {
                publications.Should().NotBeEmpty();
                publications.Should().OnlyContain(p =>
                    p.GetProperty("diagnostics").GetArrayLength() == diagnosticsAfterClose);
                publications.Should().OnlyContain(p => p.GetProperty("uri").GetString() == uri);
            }
            if (server == ForeignServer.Latex)
                foreach (var publication in publications)
                {
                    var diagnostic = publication.GetProperty("diagnostics")[0];
                    diagnostic.GetProperty("code").GetInt32().Should().Be(3);
                    diagnostic.GetProperty("message").GetString().Should().Be("Mismatched environment");
                    diagnostic.GetProperty("source").GetString().Should().Be("texlab");
                }
            var shutdown = capture.Snapshot(id).Single(e => e.Method == "shutdown"
                && e.Direction == ConversationDirection.Sent);
            shutdown.Outcome.Should().Be(ConversationOutcome.Answered);
            shutdown.AnswerSequence.Should().NotBeNull();
            Frame(shutdown.AnswerSequence!.Value).GetProperty("result").ValueKind.Should().Be(JsonValueKind.Null);
            capture.Snapshot(id).Should().Contain(e => e.Method == "exit"
                && e.Direction == ConversationDirection.Sent && e.Sequence > shutdown.Sequence);
            lifecycle.Should().Contain("process exited with code 0");
            lifecycle.Should().NotContain("process killed after waiting for exit");
        }
        finally
        {
            await client.StopAsync();
            Directory.Delete(dir, recursive: true);
        }

        JsonElement Frame(long sequence)
        {
            var body = capture.Body(id, sequence);
            body.Should().NotBeNull();
            body!.IsTruncated.Should().BeFalse();
            using var json = JsonDocument.Parse(body.Head);
            return json.RootElement.Clone();
        }

        bool HasFindings() => capture.Snapshot(id).Any(e =>
        {
            if (e.Method == "textDocument/publishDiagnostics"
                && e.Direction == ConversationDirection.Received && e.Kind == ConversationEntryKind.Notification)
                return Frame(e.Sequence).GetProperty("params").GetProperty("diagnostics").GetArrayLength() > 0;
            if (e.Method == "textDocument/diagnostic" && e.Direction == ConversationDirection.Sent
                && e.Outcome == ConversationOutcome.Answered && e.AnswerSequence is { } answer)
            {
                var result = Frame(answer).GetProperty("result");
                return result.TryGetProperty("items", out var items) && items.GetArrayLength() > 0;
            }
            return false;
        });

        async Task WaitUntil(Func<bool> ready, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            do
            {
                await capture.DrainAsync();
                if (ready()) return;
                await Task.Delay(50, token);
            } while (DateTime.UtcNow < deadline);
        }
    }
}
