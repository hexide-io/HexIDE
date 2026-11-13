using Avalonia.Input;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using HexIDE.Forms.Views;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.LspClient;

public class CompletionForeignTests
{
    public static bool HasCpp => ForeignServer.Cpp.Find() is not null;

    [AvaloniaFact(Skip = "No clangd available; RequiredServersTests enforces availability in CI.", SkipUnless = nameof(HasCpp))]
    public async Task ClangdSnippetInsertsArgumentsAndNavigatesStops()
    {
        var token = TestContext.Current.CancellationToken;
        using var logging = LoggerFactory.Create(_ => { });
        var server = ForeignServer.Cpp;
        var transport = new StdioProcessLspTransport(
            new LspServerInfo(server.Find()!, server.LaunchArguments, Path.GetTempPath()),
            logging.CreateLogger<StdioProcessLspTransport>());
        await using var client = new VBLspClient(transport, logging.CreateLogger<VBLspClient>(), server.LanguageId);
        const string source = "int twice(int n);\nint main() { tw }\n";
        var uri = LspDocumentUri.ForFile(Path.Combine(Path.GetTempPath(), "hexide-snippet.cpp"));
        var parsed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.DiagnosticsPublished += (_, p) => { if (p.Uri == uri) parsed.TrySetResult(); };
        await client.StartAsync(token);
        await client.OpenDocumentAsync(uri, source, token);
        await parsed.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        var items = await client.RequestCompletionAsync(uri, new Position(1, 15), token);
        var item = items.Should().ContainSingle(item => item.Label.Contains("twice", StringComparison.Ordinal)).Subject;
        item.InsertTextFormat.Should().Be(2);
        var snippetText = item.TextEdit?.NewText ?? item.InsertText!;
        snippetText.Should().Contain("${1:", "clangd must send an argument placeholder after snippet negotiation");
        var snippet = CompletionSnippet.Parse(snippetText);
        snippet.Should().NotBeNull();
        var stop = snippet!.Stops.Should().ContainSingle(stop => stop.Number == 1).Subject;
        var argument = snippet.Text.Substring(stop.Offset, stop.Length);
        argument.Should().Be("int n");
        var area = new TextArea { Document = new TextDocument(source) };
        area.Caret.Offset = source.IndexOf("tw }", StringComparison.Ordinal) + 2;
        new VbCompletionData(item, (_, _) => false).Complete(area,
            new TextSegment { StartOffset = area.Caret.Offset - 2, Length = 2 }, EventArgs.Empty);
        area.Document.Text.Should().Be("int twice(int n);\nint main() { twice(int n) }\n");
        area.Selection.GetText().Should().Be(argument);
        area.Selection.ReplaceSelectionWithText("21");
        var tab = new KeyEventArgs { Key = Key.Tab };
        area.StackedInputHandlers.First().OnPreviewKeyDown(tab);
        tab.Handled.Should().BeTrue();
        area.Document.Text.Should().Be("int twice(int n);\nint main() { twice(21) }\n");
        area.Caret.Offset.Should().Be(area.Document.Text.IndexOf(" }", StringComparison.Ordinal));
        area.StackedInputHandlers.Should().BeEmpty();
    }
}
