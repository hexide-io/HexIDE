using AvaloniaEdit;
using HexIDE.Conversations;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.ProjectElements;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.LspClient;

public class CarriedFileHoverFormattingForeignTests
{
    [ForeignServerFact("cpp")]
    public Task ClangdHoverAndFormattingReachCarriedFile() =>
        HeadlessDispatch.RunAsync(RunClangdHoverAndFormattingAsync, TestContext.Current.CancellationToken);

    private static async Task RunClangdHoverAndFormattingAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), "hexide-carried-clangd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var logging = LoggerFactory.Create(_ => { });
            var server = ForeignServer.Cpp;
            await using var capture = new ConversationLog();
            var transport = new StdioProcessLspTransport(new LspServerInfo(server.Find()!, server.LaunchArguments, directory),
                logging.CreateLogger<StdioProcessLspTransport>());
            await using var client = new VBLspClient(transport, logging.CreateLogger<VBLspClient>(), server.LanguageId, capture: capture, connectionId: "cpp");
            await client.StartAsync(token);
            await capture.DrainAsync();
            client.IsRunning.Should().BeTrue(string.Join(" | ", capture.Snapshot("cpp").Select(entry => $"{entry.Kind}: {entry.Detail}")));
            var path = Path.Combine(directory, "carried.cpp");
            const string source = "int twice(int n) {\nint result = n * 2;\nreturn result;\n}\nint main() { return missing; }\n";
            File.WriteAllText(path, source);
            var parsed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.DiagnosticsPublished += (_, p) => { if (p.Uri == LspDocumentUri.ForFile(path)) parsed.TrySetResult(true); };
            var settings = Substitute.For<ISettingsService>();
            settings.TabWidth.Returns(2);
            using var model = new RelatedDocumentEditorViewModel(client, Substitute.For<IWindowManager>(),
                Substitute.For<ILocalizationService>(), settings).Initialize(new RelatedDocumentDefinition(
                    new ProjectDefinition(VBProjectType.EXE, "P"), "carried.cpp", path));
            await ForeignServerWait.ForDiagnostics(parsed.Task, capture, "cpp", TimeSpan.FromSeconds(30));
            var hover = await model.RequestHoverAsync(new Position(0, 5), token);
            hover.Should().NotBeNull();
            hover!.Contents.Value.Should().Contain("twice").And.Contain("int n");
            var editor = new TextEditor { Document = model.Document };
            using var features = new EditorLanguageFeatures(editor.TextArea, model, new Rules());
            await features.FormatAsync(false, model.ApplyFormatting, () => false);
            model.Document.Text.ReplaceLineEndings("\n").Should().Be("int twice(int n) {\n  int result = n * 2;\n  return result;\n}\nint main() { return missing; }\n");
            model.Document.UndoStack.Undo();
            model.Document.Text.ReplaceLineEndings("\n").Should().Be(source);
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class Rules : IEditorCompletionRules
    {
        public bool MayOpenCompletion() => false;
        public bool LettersOpenCompletion => true;
        public bool LettersSelect => true;
        public bool RefusesTyping(int offset, int length) => false;
        public bool Commits(Avalonia.Input.Key key) => false;
        public bool HandlesReturn => true;
        public bool CompletionAtWordStart => false;
        public IReadOnlyList<string>? FixedSignatureTriggers => null;
    }
}
