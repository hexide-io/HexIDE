using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

public sealed class DocumentLinkTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hexide-document-links-" + Guid.NewGuid().ToString("N"));
    private readonly ILspClient client = Substitute.For<ILspClient>();
    private readonly IStatusBarService status = Substitute.For<IStatusBarService>();
    private readonly IDocumentDockService dock = Substitute.For<IDocumentDockService>();
    private readonly IProjectManager projects = Substitute.For<IProjectManager>();
    private readonly LocalizationService localization = new();
    private readonly ProjectDefinition project = new(VBProjectType.EXE, "P");
    private readonly ISettingsService settings = Substitute.For<ISettingsService>();
    private Window? window;
    private RelatedDocumentEditorViewModel? source;
    private RelatedDocumentEditorViewModel? destination;
    private CodeEditorViewModel? code;
    private EditorService? editors;
    private int questions;
    private int launches;
    private bool answerYes;

    public DocumentLinkTests()
    {
        Directory.CreateDirectory(directory);
        localization.Apply("en");
        client.IsRunning.Returns(true);
        client.RequestDocumentLinksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentLink>());
        projects.LoadedProjects.Returns([project]);
        dock.TryActivate(Arg.Any<Func<RelatedDocumentEditorViewModel, bool>>()).Returns(true);
        settings.TabWidth.Returns(4);
    }

    private RelatedDocumentEditorViewModel Carried(string name, string text)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, text);
        var member = new RelatedDocumentDefinition(project, name, path);
        project.AddRelatedDocument(member);
        return new RelatedDocumentEditorViewModel(client, Substitute.For<IWindowManager>(), localization, settings,
            () => editors!).Initialize(member);
    }

    private async Task<TextEditor> Show(string? target, bool codeWindow = false, string? tooltip = "Server tooltip")
    {
        var prompts = new ServerMessagePrompt((_, request, _, _) =>
        {
            questions++;
            return Task.FromResult<Task<MessageActionItem?>?>(Task.FromResult<MessageActionItem?>(request.Actions![answerYes ? 0 : 1]));
        });
        var external = new ServerDocumentNavigation(prompts, localization, (_, _) =>
        {
            launches++;
            return Task.FromResult(true);
        });
        editors = new EditorService(dock, () => throw new InvalidOperationException(), () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(), () => throw new InvalidOperationException(), () => projects, localization,
            followExternalLink: uri => external.ShowExternalAsync("Server", uri, default), linkStatus: status);
        destination = Carried("other.txt", "first\nsecond\nthird");
        dock.OpenDocuments.Returns([destination]);
        client.RequestDocumentLinksAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([new DocumentLink(new(new(0, 1), new(0, 5)), target, tooltip)]);
        Control view;
        if (codeWindow)
        {
            var module = new ModuleDefinition(project, "Source", ModuleKind.StandardModule);
            module.UpdateCode("'link text\n");
            project.AddModule(module);
            code = new CodeEditorViewModel(Substitute.For<IWindowManager>(), editors, Substitute.For<IProjectService>(),
                new EventBus(), client, settings, status, Substitute.For<IBookmarkService>(),
                Substitute.For<HexIDE.Debugging.IBreakpointService>(), Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
                Substitute.For<HexIDE.Debugging.IRunScope>(), localization).Initialize(module);
            view = new CodeEditorView { DataContext = code };
        }
        else
        {
            source = Carried("main.txt", "aLINK text\n");
            view = new RelatedDocumentEditorView { DataContext = source };
        }
        window = new Window { Content = view, Width = 850, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.CaretOffset = 8;
        editor.TextArea.Focus();
        await Task.Delay(450, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        return editor;
    }

    private void Click(TextEditor editor, bool command = true)
    {
        var view = editor.TextArea.TextView;
        var point = view.GetVisualPosition(new TextViewPosition(1, 3), VisualYPosition.TextMiddle) - view.ScrollOffset;
        var screen = view.TranslatePoint(point, window!)!.Value;
        var modifiers = command ? (RawInputModifiers)Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers
            : RawInputModifiers.None;
        window!.MouseMove(screen, modifiers);
        window!.MouseDown(screen, MouseButton.Left, modifiers);
        window!.MouseUp(screen, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(false, "#L2")]
    [InlineData(false, "#2")]
    [InlineData(true, "#L2")]
    [InlineData(true, "#2")]
    [InlineData(false, "#2,1")]
    [InlineData(true, "#L2,1")]
    public async Task CommandClickOpensLoadedTargetWithoutMovingSourceCaret(bool codeWindow, string fragment)
    {
        var target = new Uri(Path.Combine(directory, "other.txt")).AbsoluteUri + fragment;
        var editor = await Show(target, codeWindow);
        Click(editor);
        destination!.CaretOffset.Should().Be(6);
        editor.CaretOffset.Should().Be(8);
        dock.Received().TryActivate(Arg.Any<Func<RelatedDocumentEditorViewModel, bool>>());
        questions.Should().Be(0);
        launches.Should().Be(0);
    }

    [AvaloniaTheory]
    [InlineData(false, "#2,3")]
    [InlineData(true, "#L2,3")]
    public async Task AFragmentColumnPlacesTheCaretOnThatColumn(bool codeWindow, string fragment)
    {
        var target = new Uri(Path.Combine(directory, "other.txt")).AbsoluteUri + fragment;
        var editor = await Show(target, codeWindow);
        Click(editor);
        destination!.CaretOffset.Should().Be(8);
        editor.CaretOffset.Should().Be(8);
    }

    [AvaloniaTheory]
    [InlineData("https://example.com/link", 1)]
    [InlineData("file:///unloaded.txt", 0)]
    public async Task ExternalLinksUseExistingPolicyAndExplainUnloadedTarget(string target, int expectedQuestions)
    {
        var editor = await Show(target);
        Click(editor);
        await Task.Delay(30, TestContext.Current.CancellationToken);
        questions.Should().Be(expectedQuestions);
        launches.Should().Be(0);
        status.Received().SetTemporaryMessage("The link target is not open in HexIDE: " + target, Arg.Any<TimeSpan>());
        editor.CaretOffset.Should().Be(8);
    }

    [AvaloniaFact]
    public async Task AnAcceptedWebLinkLaunchesAndReportsNothing()
    {
        answerYes = true;
        var editor = await Show("https://example.com/link");
        Click(editor);
        await Task.Delay(30, TestContext.Current.CancellationToken);
        questions.Should().Be(1);
        launches.Should().Be(1);
        status.DidNotReceive().SetTemporaryMessage(Arg.Any<string>(), Arg.Any<TimeSpan>());
        editor.CaretOffset.Should().Be(8);
    }

    [AvaloniaFact]
    public async Task PlainClickPlacesCaretAndFollowsNothing()
    {
        var editor = await Show("https://example.com/link");
        Click(editor, false);
        editor.CaretOffset.Should().Be(2);
        questions.Should().Be(0);
        launches.Should().Be(0);
        status.DidNotReceive().SetTemporaryMessage(Arg.Any<string>(), Arg.Any<TimeSpan>());
    }

    [AvaloniaFact]
    public async Task TargetlessLinkDoesNothingAndPreservesCaret()
    {
        var editor = await Show(null);
        Click(editor);
        editor.CaretOffset.Should().Be(8);
        questions.Should().Be(0);
        launches.Should().Be(0);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinksAreCachedPerVersionAndInvalidatedByServerRefresh(bool codeWindow)
    {
        var editor = await Show("https://example.com/link", codeWindow);
        var token = TestContext.Current.CancellationToken;
        var uri = codeWindow ? code!.GetDocumentUriPublic() : source!.DocumentUri;
        Task<DocumentLink[]> Ask() => codeWindow ? code!.Analysis.Links(token) : source!.RequestDocumentLinksAsync(token);
        await Ask();
        await Ask();
        await client.Received(1).RequestDocumentLinksAsync(uri!, Arg.Any<CancellationToken>());
        var view = window!.Content;
        window.Content = null;
        window.Content = view;
        await Task.Delay(450, token);
        await Ask();
        await client.Received(1).RequestDocumentLinksAsync(uri!, Arg.Any<CancellationToken>());
        editor.Document.Insert(editor.Document.TextLength, "more");
        await Ask();
        await client.Received(2).RequestDocumentLinksAsync(uri!, Arg.Any<CancellationToken>());
        client.ReceivedCalls().Where(call => call.GetArguments().FirstOrDefault() as string == uri)
            .Select(call => call.GetMethodInfo().Name)
            .Where(name => name == nameof(ILspClient.RequestDocumentLinksAsync) || name == nameof(ILspClient.ChangeDocumentAsync))
            .Should().Equal(nameof(ILspClient.RequestDocumentLinksAsync), nameof(ILspClient.ChangeDocumentAsync),
                nameof(ILspClient.RequestDocumentLinksAsync));
        client.HandlesRefresh(Arg.Any<string>(), client).Returns(true);
        client.SemanticTokensRefreshRequested += Raise.Event<EventHandler>(client, EventArgs.Empty);
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(750, token);
        Dispatcher.UIThread.RunJobs();
        await Ask();
        await client.Received(3).RequestDocumentLinksAsync(uri!, Arg.Any<CancellationToken>());
        client.StateChanged += Raise.Event<EventHandler>(client, EventArgs.Empty);
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(450, token);
        Dispatcher.UIThread.RunJobs();
        await Ask();
        await client.Received(4).RequestDocumentLinksAsync(uri!, Arg.Any<CancellationToken>());
    }

    [AvaloniaTheory]
    [InlineData(1, true)]
    [InlineData(1.5, true)]
    [InlineData(1, false)]
    [InlineData(1.5, false)]
    public async Task ModifierShowsHandAndTooltipAndReleasingItClearsThem(double scaling, bool tooltip)
    {
        const string target = "https://example.com/link";
        var editor = await Show(target, tooltip: tooltip ? "Server tooltip" : null);
        window!.SetRenderScaling(scaling);
        Dispatcher.UIThread.RunJobs();
        var view = editor.TextArea.TextView;
        var at = view.GetVisualPosition(new TextViewPosition(1, 3), VisualYPosition.TextMiddle) - view.ScrollOffset;
        var point = view.TranslatePoint(at, window!)!.Value;
        var command = (RawInputModifiers)Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers;
        var originalCursor = editor.TextArea.Cursor;
        window!.MouseMove(point, command);
        editor.TextArea.Cursor.Should().NotBeNull();
        editor.TextArea.Cursor!.ToString().Should().Be(new Cursor(StandardCursorType.Hand).ToString());
        ToolTip.GetTip(editor).Should().Be(tooltip ? "Server tooltip" : target);
        ToolTip.GetIsOpen(editor).Should().BeTrue();
        view.EnsureVisualLines();
        view.VisualLines.SelectMany(line => line.Elements).Should().Contain(element =>
            element.TextRunProperties.TextDecorations != null && element.TextRunProperties.TextDecorations.Any(
                decoration => decoration.Location == TextDecorationLocation.Underline));
        window!.MouseMove(point, RawInputModifiers.None);
        ToolTip.GetIsOpen(editor).Should().BeFalse();
        editor.TextArea.Cursor.Should().BeSameAs(originalCursor);
        view.EnsureVisualLines();
        view.VisualLines.SelectMany(line => line.Elements).Should().NotContain(element =>
            element.TextRunProperties.TextDecorations != null && element.TextRunProperties.TextDecorations.Any(
                decoration => decoration.Location == TextDecorationLocation.Underline));
    }

    [AvaloniaFact]
    public void QueuedServerStateAfterCloseDoesNotReachDisposedLinkCache()
    {
        source = Carried("queued.txt", "text");
        Exception? observed = null;
        void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs args)
        {
            observed = args.Exception;
            args.Handled = true;
        }
        Dispatcher.UIThread.UnhandledException += OnUnhandled;
        try
        {
            client.StateChanged += Raise.Event<EventHandler>(client, EventArgs.Empty);
            source.Dispose();
            Action drain = () => Dispatcher.UIThread.RunJobs();
            drain.Should().NotThrow();
            observed.Should().BeNull();
        }
        finally { Dispatcher.UIThread.UnhandledException -= OnUnhandled; }
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.5)]
    public async Task WaylandLinkTooltipAnchorsToHoveredWord(double scaling)
    {
        HexIDE.Automation.NativeWayland.ForTest = _ => true;
        try
        {
            var editor = await Show("https://example.com/link");
            var host = window!;
            host.SetRenderScaling(scaling);
            Dispatcher.UIThread.RunJobs();
            var view = editor.TextArea.TextView;
            var middle = view.GetVisualPosition(new TextViewPosition(1, 3), VisualYPosition.TextMiddle) - view.ScrollOffset;
            var command = (RawInputModifiers)Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers;
            host.MouseMove(view.TranslatePoint(middle, host)!.Value, command);
            ToolTip.GetIsOpen(editor).Should().BeTrue();
            ToolTip.GetPlacement(editor).Should().Be(PlacementMode.Custom);
            var callback = ToolTip.GetCustomPopupPlacementCallback(editor);
            callback.Should().NotBeNull();
            ToolTip.SetIsOpen(editor, false);
            Rect? actual = null;
            ToolTip.SetCustomPopupPlacementCallback(editor, placement =>
            {
                callback!(placement);
                actual = placement.AnchorRectangle;
            });
            ToolTip.SetIsOpen(editor, true);
            Dispatcher.UIThread.RunJobs();
            var start = view.GetVisualPosition(new TextViewPosition(1, 1), VisualYPosition.LineTop) - view.ScrollOffset;
            var end = view.GetVisualPosition(new TextViewPosition(1, 6), VisualYPosition.LineBottom) - view.ScrollOffset;
            actual.Should().Be(new Rect(view.TranslatePoint(start, host)!.Value, view.TranslatePoint(end, host)!.Value));
        }
        finally { HexIDE.Automation.NativeWayland.ForTest = null; }
    }

    public void Dispose()
    {
        window?.Close();
        source?.Dispose();
        destination?.Dispose();
        code?.Dispose();
        Directory.Delete(directory, true);
    }
}
