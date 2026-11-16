using Avalonia;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.ProjectElements;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Integration.Tests.Views;

public sealed class CarriedFileHoverFormattingTests : IDisposable
{
    private readonly ILspClient client = Substitute.For<ILspClient>();
    private readonly IWindowManager dialogs = Substitute.For<IWindowManager>();
    private readonly ILocalizationService localization = Substitute.For<ILocalizationService>();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hexide-hover-format-" + Guid.NewGuid().ToString("N"));
    private Window? window;
    private RelatedDocumentEditorViewModel? model;

    public CarriedFileHoverFormattingTests()
    {
        Directory.CreateDirectory(directory);
        client.IsRunning.Returns(true);
        client.SupportsCapability(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        client.SupportsRangeFormatting(Arg.Any<string>()).Returns(true);
        client.RequestHoverAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new HoverResult(new MarkupContent("markdown", "**server**\n\n```cpp\nint twice(int n)\n```")));
        TextEdit[] edits = [new(new(new(0, 0), new(0, 1)), "A"), new(new(new(1, 0), new(1, 1)), "B"),
            new(new(new(1, 1), new(1, 1)), "x"), new(new(new(1, 1), new(1, 1)), "y")];
        client.RequestFormattingAsync(Arg.Any<string>(), Arg.Any<FormattingOptions>(), Arg.Any<CancellationToken>()).Returns(edits);
        client.RequestRangeFormattingAsync(Arg.Any<string>(), Arg.Any<HexIDE.Lsp.Messages.Range>(), Arg.Any<FormattingOptions>(), Arg.Any<CancellationToken>()).Returns(edits);
        localization.GetString("Str.CodeEditor.Msg.TypingChangesReadOnlyText").Returns("Read-only text: only the IDE changes it.");
    }

    private TextEditor Show(ILspClient? actualClient = null)
    {
        var path = Path.Combine(directory, "carried.cpp");
        File.WriteAllText(path, "a\nb");
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(7);
        model = new RelatedDocumentEditorViewModel(actualClient ?? client, dialogs, localization, settings)
            .Initialize(new RelatedDocumentDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "carried.cpp", path));
        window = new Window { Content = new RelatedDocumentEditorView { DataContext = model }, Width = 900, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = ((Control)window.Content).FindControl<TextEditor>("TextEditor")!;
        editor.TextArea.Focus();
        client.ClearReceivedCalls();
        return editor;
    }

    private void Hover(TextEditor editor)
    {
        var view = editor.TextArea.TextView;
        var point = view.GetVisualPosition(new TextViewPosition(1, 1), VisualYPosition.TextMiddle) - view.ScrollOffset;
        window!.MouseMove(view.TranslatePoint(point, window!)!.Value);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.5)]
    public async Task HoverShowsServersMarkdown(double scaling)
    {
        var editor = Show();
        window!.SetRenderScaling(scaling);
        Dispatcher.UIThread.RunJobs();
        Hover(editor);
        await Task.Delay(650, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        ToolTip.GetIsOpen(editor).Should().BeTrue();
        var tip = ToolTip.GetTip(editor).Should().BeOfType<ToolTip>().Subject;
        ((Control)tip.Content!).GetVisualDescendants().OfType<TextEditor>().Should().Contain(e => e.Text == "int twice(int n)");
        await client.Received(1).RequestHoverAsync(Arg.Any<string>(), new Position(0, 0), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task NoHoverProviderSendsNoRequest()
    {
        client.SupportsCapability(Arg.Any<string>(), "hoverProvider").Returns(false);
        var editor = Show();
        Hover(editor);
        await Task.Delay(650, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        ToolTip.GetIsOpen(editor).Should().BeFalse();
        await client.DidNotReceive().RequestHoverAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task LeavingTextBeforeDwellSendsNoHoverRequest()
    {
        var editor = Show();
        Hover(editor);
        window!.MouseMove(new Point(895, 595));
        await Task.Delay(650, TestContext.Current.CancellationToken);
        ToolTip.GetIsOpen(editor).Should().BeFalse();
        await client.DidNotReceive().RequestHoverAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task ReattachedViewRequestsHoverOnlyOnce()
    {
        var editor = Show();
        var view = window!.Content;
        window.Content = null;
        window.Content = view;
        Dispatcher.UIThread.RunJobs();
        Hover(editor);
        await Task.Delay(650, TestContext.Current.CancellationToken);
        ToolTip.GetIsOpen(editor).Should().BeTrue();
        await client.Received(1).RequestHoverAsync(Arg.Any<string>(), new Position(0, 0), Arg.Any<CancellationToken>());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FormatCommandsApplyEditsAsOneUndoStep(bool selection)
    {
        var editor = Show();
        editor.Select(0, editor.Document.TextLength);
        var command = selection ? ApplicationCommands.FormatSelectionCommand : ApplicationCommands.FormatDocumentCommand;
        var menu = editor.ContextFlyout.Should().BeOfType<MenuFlyout>().Subject;
        menu.Items.OfType<MenuItem>().Should().Contain(item => ReferenceEquals(item.Command, command));
        command.CanExecute(null, editor.TextArea).Should().BeTrue();
        command.Execute(null, editor.TextArea);
        for (var i = 0; i < 20 && editor.Text.ReplaceLineEndings("\n") != "A\nBxy"; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
        }
        editor.Text.ReplaceLineEndings("\n").Should().Be("A\nBxy");
        if (selection)
            await client.Received(1).RequestRangeFormattingAsync(Arg.Any<string>(), new(new(0, 0), new(1, 1)), new FormattingOptions(7, true), Arg.Any<CancellationToken>());
        else
            await client.Received(1).RequestFormattingAsync(Arg.Any<string>(), new FormattingOptions(7, true), Arg.Any<CancellationToken>());
        editor.Undo();
        editor.Text.ReplaceLineEndings("\n").Should().Be("a\nb");
    }

    [AvaloniaFact]
    public void NoFormattingProvidersDisablesBothCommands()
    {
        client.SupportsCapability(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        client.SupportsRangeFormatting(Arg.Any<string>()).Returns(false);
        var editor = Show();
        editor.Select(0, editor.Document.TextLength);
        ApplicationCommands.FormatDocumentCommand.CanExecute(null, editor.TextArea).Should().BeFalse();
        ApplicationCommands.FormatSelectionCommand.CanExecute(null, editor.TextArea).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task ReadOnlyEditorRefusesFormattingAndExplainsWhy()
    {
        var editor = Show();
        editor.IsReadOnly = true;
        ApplicationCommands.FormatDocumentCommand.Execute(null, editor.TextArea);
        Dispatcher.UIThread.RunJobs();
        editor.Text.ReplaceLineEndings("\n").Should().Be("a\nb");
        await dialogs.Received().MessageBox("Read-only text: only the IDE changes it.", Arg.Any<string>(), MessageBoxButtons.Ok, MessageBoxIcon.Warning);
        await client.DidNotReceive().RequestFormattingAsync(Arg.Any<string>(), Arg.Any<FormattingOptions>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingFormattingCannotChangeAnEditedOrDetachedBuffer(bool detach)
    {
        var reply = new TaskCompletionSource<TextEdit[]>();
        client.RequestFormattingAsync(Arg.Any<string>(), Arg.Any<FormattingOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => reply.Task);
        var editor = Show();
        ApplicationCommands.FormatDocumentCommand.Execute(null, editor.TextArea);
        client.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ILspClient.RequestFormattingAsync)).Should().Be(1);
        if (detach) window!.Content = null;
        else editor.Document.Insert(0, "changed");
        var before = editor.Text;
        reply.SetResult([new(new(new(0, 0), new(0, 1)), "X")]);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        editor.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public async Task DetachedHoverAnswerCannotOpenAfterReattachment()
    {
        var reply = new TaskCompletionSource<HoverResult?>();
        client.RequestHoverAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>()).Returns(_ => reply.Task);
        var editor = Show();
        Hover(editor);
        await Task.Delay(650, TestContext.Current.CancellationToken);
        client.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ILspClient.RequestHoverAsync)).Should().Be(1);
        var view = window!.Content;
        window.Content = null;
        window.Content = view;
        reply.SetResult(new HoverResult(new MarkupContent("markdown", "**old reply**")));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        ToolTip.GetIsOpen(editor).Should().BeFalse();
        ToolTip.GetTip(editor).Should().BeNull();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScriptedServerSeesHoverAndFormattingOnlyWhenAdvertised(bool advertised)
    {
        var server = new ScriptedServer(advertised);
        var (local, remote) = FullDuplexStream.CreatePair();
        using var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(remote, remote, new SystemTextJsonFormatter()), server);
        rpc.StartListening();
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.LeaveServerRunning.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(c => Task.FromResult<IJsonRpcMessageHandler?>(new HeaderDelimitedMessageHandler(local, local, c.Arg<IJsonRpcMessageFormatter>())));
        await using var actualClient = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "cpp");
        await actualClient.StartAsync(TestContext.Current.CancellationToken);
        var editor = Show(actualClient);
        editor.Select(0, editor.Document.TextLength);
        ApplicationCommands.FormatDocumentCommand.CanExecute(null, editor.TextArea).Should().Be(advertised);
        ApplicationCommands.FormatSelectionCommand.CanExecute(null, editor.TextArea).Should().Be(advertised);
        Hover(editor);
        if (!advertised)
        {
            await Task.Delay(650, TestContext.Current.CancellationToken);
            server.HoverRequest.Should().BeNull();
            server.FormatRequests.Should().BeEmpty();
            return;
        }
        await Until(() => ToolTip.GetIsOpen(editor));
        server.HoverRequest.Should().NotBeNull();
        server.HoverRequest!.Value.GetProperty("textDocument").GetProperty("uri").GetString().Should().Be(model!.DocumentUri);
        var tip = ToolTip.GetTip(editor).Should().BeOfType<ToolTip>().Subject;
        ((Control)tip.Content!).GetVisualDescendants().OfType<TextEditor>().Should().Contain(e => e.Text == "int twice(int n)");
        ApplicationCommands.FormatDocumentCommand.Execute(null, editor.TextArea);
        await Until(() => editor.Text.ReplaceLineEndings("\n") == "A\nB");
        editor.Undo();
        editor.Text.ReplaceLineEndings("\n").Should().Be("a\nb");
        editor.Select(0, editor.Document.TextLength);
        ApplicationCommands.FormatSelectionCommand.Execute(null, editor.TextArea);
        await Until(() => editor.Text.ReplaceLineEndings("\n") == "A\nB");
        server.FormatRequests.Should().HaveCount(2);
        foreach (var request in server.FormatRequests)
        {
            request.GetProperty("options").GetProperty("tabSize").GetInt32().Should().Be(7);
            request.GetProperty("options").GetProperty("insertSpaces").GetBoolean().Should().BeTrue();
        }
        server.FormatRequests[1].GetProperty("range").GetProperty("end").GetProperty("line").GetInt32().Should().Be(1);
        editor.Undo();
        editor.Text.ReplaceLineEndings("\n").Should().Be("a\nb");
        window!.Close();
        model!.Dispose();
    }

    private static async Task Until(Func<bool> predicate)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!predicate() && deadline.Elapsed < TimeSpan.FromSeconds(30))
            await Task.Delay(10, TestContext.Current.CancellationToken);
        predicate().Should().BeTrue();
    }

    private sealed class ScriptedServer(bool advertised)
    {
        public JsonElement? HoverRequest;
        public System.Collections.Generic.List<JsonElement> FormatRequests { get; } = [];
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p) => JsonSerializer.SerializeToElement(new
        {
            capabilities = new { textDocumentSync = 1, hoverProvider = advertised,
                documentFormattingProvider = advertised, documentRangeFormattingProvider = advertised }
        });
        [JsonRpcMethod("initialized", UseSingleObjectParameterDeserialization = true)]
        public void Initialized(JsonElement p) { }
        [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
        public void Open(JsonElement p) { }
        [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
        public void Change(JsonElement p) { }
        [JsonRpcMethod("textDocument/hover", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Hover(JsonElement p)
        {
            HoverRequest = p.Clone();
            return JsonSerializer.SerializeToElement(new HoverResult(new MarkupContent("markdown", "```cpp\nint twice(int n)\n```")));
        }
        [JsonRpcMethod("textDocument/formatting", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Format(JsonElement p)
        {
            FormatRequests.Add(p.Clone());
            return JsonSerializer.SerializeToElement(new TextEdit[] { new(new(new(0, 0), new(0, 1)), "A"), new(new(new(1, 0), new(1, 1)), "B") });
        }
        [JsonRpcMethod("textDocument/rangeFormatting", UseSingleObjectParameterDeserialization = true)]
        public JsonElement FormatRange(JsonElement p) => Format(p);
    }

    public void Dispose()
    {
        window?.Close();
        model?.Dispose();
        Directory.Delete(directory, true);
    }
}
