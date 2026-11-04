using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Folding;
using HexIDE.Controls;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Integration.Tests.Views;

public class EditorRedockTests
{
    [AvaloniaTheory]
    [InlineData("textDocument/codeLens", false)]
    [InlineData("textDocument/codeLens", true)]
    [InlineData("textDocument/semanticTokens/full", false)]
    [InlineData("textDocument/semanticTokens/full", true)]
    [InlineData("textDocument/inlayHint", false)]
    [InlineData("textDocument/inlayHint", true)]
    [InlineData("textDocument/foldingRange", false)]
    [InlineData("textDocument/foldingRange", true)]
    public async Task Opening_a_tool_reuses_answers_and_an_edit_still_refreshes_them(string method, bool answersPending)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var (clientStream, serverStream) = FullDuplexStream.CreatePair();
        using var serverSide = serverStream;
        var server = new Server();
        if (!answersPending) server.ReleaseAnswers();
        using var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(serverStream, serverStream,
            new SystemTextJsonFormatter()), server);
        rpc.StartListening();
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IJsonRpcMessageHandler?>(new HeaderDelimitedMessageHandler(
                clientStream, clientStream, call.Arg<IJsonRpcMessageFormatter>())));
        await using var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), DocumentLanguage.Vb6);
        await client.StartAsync(cancellation);
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        typeof(ISettingsService).GetProperty("ShowCodeLenses")?.SetValue(settings, true);
        settings.ShowInlayHints.Returns(true);
        var localization = new LocalizationService();
        localization.Apply("en");
        using var vm = new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), new EventBus(), client, settings,
            Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(),
            Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<HexIDE.Debugging.IRunScope>(), localization);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Sub Greet()\r\nEnd Sub\r\n");
        vm.Initialize(module);
        var factory = new Factory();
        var documents = new DocumentDock
        {
            VisibleDockables = factory.CreateList<IDockable>(vm), ActiveDockable = vm,
        };
        var middle = new ProportionalDock
        {
            Orientation = Dock.Model.Core.Orientation.Vertical,
            VisibleDockables = factory.CreateList<IDockable>(documents), ActiveDockable = documents,
        };
        var root = new RootDock
        {
            VisibleDockables = factory.CreateList<IDockable>(middle), ActiveDockable = middle,
        };
        factory.InitLayout(root);
        var host = new DockControl { Layout = root };
        var attached = 0;
        var detached = 0;
        host.DataTemplates.Add(new FuncDataTemplate<CodeEditorViewModel>((model, _) =>
        {
            var view = new CodeEditorView { DataContext = model };
            view.AttachedToVisualTree += (_, _) => attached++;
            view.DetachedFromVisualTree += (_, _) => detached++;
            return view;
        }));
        var window = new Window { Content = host, Width = 800, Height = 600 };
        try
        {
            window.Show();
            await Until(() => new[] { "textDocument/codeLens", "textDocument/semanticTokens/full",
                "textDocument/inlayHint", "textDocument/foldingRange" }.All(m => server.Count(m) > 0));
            await Settle();
            if (!answersPending) CheckAnswers();
            var before = server.Count(method);
            var tool = new Tool { Id = "References", Title = "References" };
            var tools = new ToolDock
            {
                VisibleDockables = factory.CreateList<IDockable>(tool), ActiveDockable = tool,
                Proportion = .3,
            };
            // The same alternating-list insertion as DockFactory.InsertToolDock for the first bottom pane.
            middle.VisibleDockables!.Add(new ProportionalDockSplitter());
            middle.VisibleDockables.Add(tools);
            factory.InitDockable(tools, middle);
            // DockFactory.RefreshLayout replaces this list to clear Dock's cached presenter sizing.
            middle.VisibleDockables = factory.CreateList(middle.VisibleDockables.ToArray());
            await Settle();
            detached.Should().BeGreaterThan(0, "opening the tool must exercise the reported detach path");
            attached.Should().BeGreaterThan(1);
            server.Count(method).Should().Be(before, "a re-dock alone must not ask the server again");

            server.ReleaseAnswers();
            await Settle();
            CheckAnswers();
            var changes = server.Count("textDocument/didChange");
            var diagnostics = server.Count("textDocument/diagnostic");
            var lensesBeforeEdit = server.Count("textDocument/codeLens");
            for (var i = 0; i < 3; i++)
            {
                vm.Document.Insert(vm.Document.TextLength, "' edited\r\n");
                await Task.Delay(50, cancellation);
            }
            await Until(() => server.Count("textDocument/diagnostic") > diagnostics);
            // Lenses follow the same sync as diagnostics, rather than the view's later folding pause.
            await Until(() => server.Count("textDocument/codeLens") > lensesBeforeEdit, 120);
            server.Count("textDocument/codeLens").Should().Be(lensesBeforeEdit + 1);
            await Until(() => server.Count("textDocument/didChange") > changes && server.Count(method) > before);
            server.LastChange.Should().Contain("' edited");
            server.Count("textDocument/codeLens").Should().Be(lensesBeforeEdit + 1,
                "the later folding pause must not rediscover lenses a second time");
            var lensRequests = server.Count("textDocument/codeLens");
            await rpc.InvokeWithParameterObjectAsync<object?>("workspace/codeLens/refresh", null, cancellation);
            await Until(() => server.Count("textDocument/codeLens") > lensRequests);
            host.GetVisualDescendants().OfType<CodeEditorView>().Should().ContainSingle();

            window.Content = null;
            lensRequests = server.Count("textDocument/codeLens");
            await rpc.InvokeWithParameterObjectAsync<object?>("workspace/codeLens/refresh", null, cancellation);
            server.Count("textDocument/codeLens").Should().Be(lensRequests,
                "a suspended document must not request lenses through a detached view");
            window.Content = host;
            await Until(() => server.Count("textDocument/codeLens") > lensRequests);
        }
        finally { server.ReleaseAnswers(); window.Close(); }

        void CheckAnswers()
        {
            var view = host.GetVisualDescendants().OfType<CodeEditorView>().Single();
            var editor = view.FindControl<TextEditor>("TextEditor")!;
            var textView = editor.TextArea.TextView;
            view.GetVisualDescendants().OfType<Button>().Should().Contain(b => Equals(b.Content, "1 reference"));
            textView.LineTransformers.OfType<SemanticTokensColorizer>().Single().Tokens.Should().ContainSingle();
            textView.BackgroundRenderers.OfType<InlayHintRenderer>().Single().Placements(textView)
                .Should().Contain(p => p.Text == "hint");
            ((FoldingManager)textView.GetService(typeof(FoldingManager))!).AllFoldings.Should().ContainSingle();
        }

        async Task Until(Func<bool> ready, int milliseconds = 10000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (!ready() && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                await Task.Delay(20, cancellation);
            }
            ready().Should().BeTrue("the expected wire frame must arrive");
        }

        async Task Settle()
        {
            // Longer than the editor's 500ms debounce, to expose attach-triggered requests too.
            for (var i = 0; i < 40; i++)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                await Task.Delay(20, cancellation);
            }
        }
    }

    private sealed class Server
    {
        private readonly ConcurrentDictionary<string, int> counts = new();
        private readonly TaskCompletionSource answers = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseAnswers() => answers.TrySetResult();
        public string? LastChange { get; private set; }
        public int Count(string method) => counts.GetValueOrDefault(method);
        private void Seen(string method) => counts.AddOrUpdate(method, 1, (_, count) => count + 1);

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) => JsonSerializer.SerializeToElement(new
        {
            capabilities = new
            {
                diagnosticProvider = new { interFileDependencies = false, workspaceDiagnostics = false },
                textDocumentSync = 1, codeLensProvider = new { resolveProvider = false },
                semanticTokensProvider = new { legend = new { tokenTypes = new[] { "keyword" }, tokenModifiers = Array.Empty<string>() }, full = true },
                inlayHintProvider = true, foldingRangeProvider = true,
            },
        });
        [JsonRpcMethod("initialized", UseSingleObjectParameterDeserialization = true)]
        public void Initialized(JsonElement _) { }
        [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
        public void Open(JsonElement _) => Seen("textDocument/didOpen");
        [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
        public void Change(JsonElement p)
        {
            LastChange = p.GetProperty("contentChanges")[0].GetProperty("text").GetString();
            Seen("textDocument/didChange");
        }
        [JsonRpcMethod("textDocument/didClose", UseSingleObjectParameterDeserialization = true)]
        public void Close(JsonElement _) { }
        [JsonRpcMethod("textDocument/diagnostic", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Diagnostics(JsonElement _)
        {
            Seen("textDocument/diagnostic");
            return JsonSerializer.SerializeToElement(new { kind = "full", items = Array.Empty<object>() });
        }
        [JsonRpcMethod("textDocument/codeLens", UseSingleObjectParameterDeserialization = true)]
        public async Task<JsonElement> Lenses(JsonElement _)
        {
            Seen("textDocument/codeLens");
            await answers.Task;
            return JsonSerializer.SerializeToElement(new[] { new
            {
                range = new { start = new { line = 1, character = 0 }, end = new { line = 1, character = 3 } },
                command = new { title = "1 reference", command = "test.run" },
            } });
        }
        [JsonRpcMethod("textDocument/semanticTokens/full", UseSingleObjectParameterDeserialization = true)]
        public async Task<JsonElement> Tokens(JsonElement _)
        {
            Seen("textDocument/semanticTokens/full");
            await answers.Task;
            return JsonSerializer.SerializeToElement(new { data = new[] { 1, 0, 3, 0, 0 } });
        }
        [JsonRpcMethod("textDocument/inlayHint", UseSingleObjectParameterDeserialization = true)]
        public async Task<JsonElement> Hints(JsonElement _)
        {
            Seen("textDocument/inlayHint");
            await answers.Task;
            return JsonSerializer.SerializeToElement(new[] { new { position = new { line = 1, character = 0 }, label = "hint" } });
        }
        [JsonRpcMethod("textDocument/foldingRange", UseSingleObjectParameterDeserialization = true)]
        public async Task<JsonElement> Folds(JsonElement _)
        {
            Seen("textDocument/foldingRange");
            await answers.Task;
            return JsonSerializer.SerializeToElement(new[] { new { startLine = 1, endLine = 2 } });
        }
        [JsonRpcMethod("shutdown")]
        public object? Shutdown() => null;
        [JsonRpcMethod("exit")]
        public void Exit() { }
    }
}
