using Avalonia.VisualTree;
using AvaloniaEdit;
using System.Linq;
using System.ComponentModel;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

public class CodeEditorStickyScrollTests : IDisposable
{
    private readonly ILspClient _lspClient = Substitute.For<ILspClient>();
    private readonly List<Window> _windows = [];

    /// <summary>What the server would answer, once it is up. Empty until then, as the registry answers.</summary>
    private FoldingRange[] _folds = [];

    public CodeEditorStickyScrollTests()
    {
        _lspClient.IsRunning.Returns(true);
        _lspClient.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DocumentSymbol>());
        _lspClient.RequestFoldingRangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                return Task.FromResult(_folds);
            });
    }

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        GC.SuppressFinalize(this);
    }

    [AvaloniaFact]
    public async Task HeadersFollowScrollAndClickNavigates()
    {
        using var vm = MakeViewModel();
        vm.Initialize(AModule());
        _folds = [new FoldingRange(1, 84), new FoldingRange(2, 83)];
        var view = Show(vm);
        await SettleFolding();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.Document.LineCount.Should().BeGreaterThan(80);
        editor.TextArea.TextView.EnsureVisualLines();
        var headers = view.FindControl<StackPanel>("StickyHeaders");
        headers.Should().NotBeNull();
        Scroll(editor, 300);
        headers!.Children.OfType<Button>().Select(b => ((TextBlock)b.Content!).Text).Should().Equal("Sub Main()", "    With value");
        headers.Children.OfType<Button>().Last().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        editor.TextArea.Caret.Line.Should().Be(3);
        editor.TextArea.Caret.Column.Should().Be(1);
        editor.UpdateLayout();
        editor.TextArea.TextView.EnsureVisualLines();
        editor.TextArea.TextView.VisualLines.First().FirstDocumentLine.LineNumber.Should().BeLessThanOrEqualTo(3);
        Scroll(editor, 0);
        headers.Children.Should().BeEmpty();
        Scroll(editor, 300);
        _folds = [];
        RaiseServerStateChanged();
        await SettleFolding();
        headers.Children.Should().BeEmpty("a new server response replaces the enclosing ranges");
    }

    [AvaloniaFact]
    public async Task SettingsLimitDepthAndDisableAlreadyOpenHeaders()
    {
        using var vm = MakeViewModel();
        vm.Initialize(AModule());
        _folds = [new FoldingRange(1, 84), new FoldingRange(2, 83)];
        var view = Show(vm);
        await SettleFolding();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.Document.LineCount.Should().BeGreaterThan(80);
        editor.TextArea.TextView.EnsureVisualLines();
        var headers = view.FindControl<StackPanel>("StickyHeaders");
        headers.Should().NotBeNull();
        Scroll(editor, 300);
        // Reflection lets this regression run on the original branch, where these settings do not exist.
        var depth = vm.Settings.GetType().GetProperty("StickyScrollDepth");
        depth.Should().NotBeNull();
        depth!.SetValue(vm.Settings, 1);
        vm.Settings.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(vm.Settings, new PropertyChangedEventArgs("StickyScrollDepth"));
        headers!.Children.OfType<Button>().Select(b => ((TextBlock)b.Content!).Text).Should().Equal("Sub Main()");
        var enabled = vm.Settings.GetType().GetProperty("IsStickyScrollEnabled")!;
        enabled.SetValue(vm.Settings, false);
        vm.Settings.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(vm.Settings, new PropertyChangedEventArgs("IsStickyScrollEnabled"));
        headers.Children.Should().BeEmpty();
    }

    private static void Scroll(TextEditor editor, double offset)
    {
        // Drive the actual scroll container: TextEditor's scroll helper is inert in this headless host.
        editor.GetVisualDescendants().OfType<ScrollViewer>().First().Offset = new Avalonia.Vector(0, offset);
        editor.UpdateLayout();
        editor.TextArea.TextView.EnsureVisualLines();
        Dispatcher.UIThread.RunJobs();
        if (offset > 0) editor.TextArea.TextView.VerticalOffset.Should().BeGreaterThan(0);
    }

    private void RaiseServerStateChanged()
    {
        _lspClient.StateChanged += Raise.Event<EventHandler>(_lspClient, EventArgs.Empty);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Waits out the view's own settle before the request it schedules can be observed.
    /// </summary>
    /// <remarks>
    /// The view debounces folding by 500 ms, so this waits longer than that and then pumps, because the
    /// continuation posts back to the UI thread. A shorter wait here would assert that the request had not
    /// been made yet, which is true of the defect and of the fix alike.
    /// </remarks>
    private static async Task SettleFolding()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(900));
        Dispatcher.UIThread.RunJobs();
    }

    private CodeEditorViewModel MakeViewModel()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(
            Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(
            Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(
            Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<DocumentSavedEvent>(
            Arg.Any<Action<DocumentSavedEvent>>()).Returns(Substitute.For<IDisposable>());

        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Document.CodeSuffix").Returns("Code");

        // A real value: the view pushes it into AvaloniaEdit, which rejects a non-positive indentation
        // size, so a substitute's default of 0 throws out of the attach handler.
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        settings.GetType().GetProperty("IsStickyScrollEnabled")?.SetValue(settings, true);
        settings.GetType().GetProperty("StickyScrollDepth")?.SetValue(settings, 3);

        return new CodeEditorViewModel(
            Substitute.For<IWindowManager>(),
            Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(),
            eventBus,
            _lspClient,
            settings,
            Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(),
            Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<HexIDE.Debugging.IRunScope>(),
            localization);
    }

    private static ModuleDefinition AModule()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Sub Main()\n    With value\n" + string.Concat(Enumerable.Repeat("        Debug.Print 1\n", 80)) + "    End With\nEnd Sub\n");
        return module;
    }

    private CodeEditorView Show(CodeEditorViewModel vm)
    {
        var window = new Window { Width = 1200, Height = 800 };
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var view = new CodeEditorView { DataContext = vm };
        window.Content = view;
        Dispatcher.UIThread.RunJobs();
        return view;
    }
}
