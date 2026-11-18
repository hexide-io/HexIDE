using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Dock.Avalonia.Controls;
using HexIDE.Bookmarks;
using HexIDE.Desktop.Server;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Integration.Tests.Views;

public class ServerApplyEditModifiedTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerEditMarksTheTabAndEnablesEditorSave(bool carried)
    {
        await using var owned = new RealComposition();
        var dock = owned.Composition.DocumentDockService;
        var host = new DockControl { Layout = owned.Composition.Root.Layout };
        host.DataTemplates.Add(new FuncDataTemplate<CodeEditorViewModel>((model, _) => new CodeEditorView { DataContext = model }));
        host.DataTemplates.Add(new FuncDataTemplate<RelatedDocumentEditorViewModel>((model, _) => new RelatedDocumentEditorView { DataContext = model }));
        var window = new Window { Content = host, Width = 800, Height = 600 };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (local, remote) = FullDuplexStream.CreatePair();
        var server = new EditServer();
        using var rpc = new JsonRpc(new LspMessageHandler(remote, remote, new SystemTextJsonFormatter()), server);
        server.Rpc = rpc;
        rpc.StartListening();
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.LeaveServerRunning.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>()).Returns(call =>
            Task.FromResult<IJsonRpcMessageHandler?>(new LspMessageHandler(local, local, call.Arg<IJsonRpcMessageFormatter>())));
        EditorService editors = null!;
        await using var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6",
            applyEdit: (edit, documentFor, token) => Dispatcher.UIThread.InvokeAsync(
                () => editors.ApplyWorkspaceEdit(edit, documentFor), DispatcherPriority.Normal, token).GetTask());
        await client.StartAsync(timeout.Token);
        var manager = Substitute.For<IProjectManager>();
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        manager.LoadedProjects.Returns([project]);
        var localization = new LocalizationService();
        localization.Apply("en");
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        CodeEditorViewModel NewEditor() => new(Substitute.For<IWindowManager>(), editors,
            Substitute.For<IProjectService>(), new EventBus(), client, settings,
            Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(),
            Substitute.For<HexIDE.Debugging.IBreakpointService>(), Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<HexIDE.Debugging.IRunScope>(), localization);
        editors = new EditorService(dock, NewEditor,
            () => new RelatedDocumentEditorViewModel(client, Substitute.For<IWindowManager>(), localization, settings),
            () => throw new InvalidOperationException(), () => throw new InvalidOperationException(), () => manager, localization);
        var path = Path.Combine(Path.GetTempPath(), $"hexide-4043-{Guid.NewGuid():N}.txt");
        try
        {
            window.Show();
            BaseEditorWindowViewModel target;
            string uri;
            if (carried)
            {
                File.WriteAllText(path, "Greet\r\n");
                var definition = new RelatedDocumentDefinition(project, "Notes.txt", path);
                project.AddRelatedDocument(definition);
                editors.EditRelatedDocument(definition);
                var related = dock.OpenDocuments.OfType<RelatedDocumentEditorViewModel>().Single();
                await related.FlushDocumentAsync();
                target = related;
                uri = LspDocumentUri.ForFile(path);
            }
            else
            {
                var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
                module.UpdateCode("Public Sub Greet()\r\nEnd Sub\r\n");
                project.AddModule(module);
                editors.EditCode(module);
                var code = dock.OpenDocuments.OfType<CodeEditorViewModel>().Single();
                await code.FlushDocumentAsync();
                target = code;
                uri = code.GetDocumentUriPublic();
            }
            await Settle();
            var document = ((ISearchableDocument)target).Document;
            var line = document.GetLocation(document.Text.IndexOf("Greet", StringComparison.Ordinal));
            server.Edit = new(new() { [uri] = [new(new(new(line.Line - 1, line.Column - 1), new(line.Line - 1, line.Column + 4)), "Welcome")] });
            await client.ExecuteCommandAsync("test.edit", cancellationToken: timeout.Token);
            server.Answer.GetProperty("applied").GetBoolean().Should().BeTrue();
            server.Answer.TryGetProperty("failureReason", out _).Should().BeFalse();
            await Settle();
            target.IsModified.Should().BeTrue();
            host.GetVisualDescendants().OfType<TextBlock>().Should().Contain(block => block.Text == "*" && block.IsVisible,
                "the document tab renders its modified marker");
            var textEditor = host.GetVisualDescendants().OfType<TextEditor>().Single();
            textEditor.TextArea.Focus().Should().BeTrue();
            await Settle();
            ApplicationCommands.SaveCommand.CanExecute(null, textEditor.TextArea).Should().BeTrue();

            using var context = new IdeContext(manager, dock, client, null!, Substitute.For<IProjectRunnerService>(),
                null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
            var tools = new HexIdeTools(context);
            var tabs = JsonSerializer.SerializeToElement(await tools.GetDocumentTabsAsync(timeout.Token), ReplyJson.Options).GetProperty("tabs");
            tabs.EnumerateArray().Single(tab => tab.GetProperty("title").GetString() == target.Title)
                .GetProperty("isModified").GetBoolean().Should().BeTrue();
        }
        finally
        {
            dock.CloseAll();
            window.Close();
            File.Delete(path);
        }
    }

    private static async Task Settle()
    {
        for (var i = 0; i < 3; i++)
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private sealed class EditServer
    {
        public JsonRpc Rpc = null!;
        public WorkspaceEdit Edit = null!;
        public JsonElement Answer;
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p) => JsonDocument.Parse(
            """{"capabilities":{"textDocumentSync":1,"executeCommandProvider":{"commands":["test.edit"]}}}""").RootElement.Clone();
        [JsonRpcMethod("initialized", UseSingleObjectParameterDeserialization = true)]
        public void Initialized(JsonElement p) { }
        [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
        public void Open(JsonElement p) { }
        [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
        public void Change(JsonElement p) { }
        [JsonRpcMethod("workspace/executeCommand", UseSingleObjectParameterDeserialization = true)]
        public async Task<JsonElement?> Execute(JsonElement p)
        {
            Answer = await Rpc.InvokeWithParameterObjectAsync<JsonElement>("workspace/applyEdit", new ApplyWorkspaceEditParams(Edit), TestContext.Current.CancellationToken);
            return null;
        }
    }
}
