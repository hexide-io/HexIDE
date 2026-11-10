using Avalonia;
using Avalonia.Headless;
using Avalonia.Input.Platform;
using Avalonia.Input;
using System.Linq;
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

public class CodeEditorSplitTests : IDisposable
{
    private readonly ILspClient _lspClient = Substitute.For<ILspClient>();
    private readonly List<Window> _windows = [];
    private readonly HexIDE.Debugging.IRunScope _runScope = Substitute.For<HexIDE.Debugging.IRunScope>();

    public CodeEditorSplitTests()
    {
        _lspClient.IsRunning.Returns(true);
        _lspClient.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DocumentSymbol>());
        _lspClient.RequestFoldingRangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<FoldingRange>());
    }

    public void Dispose()
    {
        foreach (var window in _windows) window.Close();
        GC.SuppressFinalize(this);
    }

    [AvaloniaFact]
    public void SplitPanesShareEditsAndUndoButKeepTheirCaretAndScroll()
    {
        using var vm = MakeViewModel();
        vm.Initialize(AModule());
        vm.Document.Insert(vm.Document.TextLength, string.Concat(Enumerable.Repeat("' line\r\n", 200)));
        var view = Show(vm);
        var toggle = typeof(CodeEditorView).GetMethod("ToggleSplit");
        toggle.Should().NotBeNull("Window Split must create a second editor");
        toggle!.Invoke(view, null);
        Dispatcher.UIThread.RunJobs();
        var panes = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view)
            .OfType<HexIDE.Controls.TextEditing.TextEditor>().ToArray();
        panes.Should().HaveCount(2);
        var first = panes[0];
        var second = panes[1];
        first.Document.Should().BeSameAs(second.Document).And.BeSameAs(vm.Document);
        second.CaretOffset = vm.BufferPrefixLength;
        first.TextArea.Focus().Should().BeTrue();
        first.Select(first.Document.TextLength - 5, 3);
        second.SelectionLength.Should().Be(0);
        first.CaretOffset = first.Document.TextLength;
        var otherCaret = second.CaretOffset;
        first.Document.Insert(first.Document.TextLength, "' shared");
        second.Text.Should().EndWith("' shared");
        second.CaretOffset.Should().Be(otherCaret);
        second.TextArea.Focus().Should().BeTrue();
        second.CaretOffset = second.Document.TextLength;
        first.CaretOffset = 0;
        vm.CaretOffset.Should().Be(second.CaretOffset);
        second.Undo();
        first.Text.Should().NotEndWith("' shared");
        Dispatcher.UIThread.RunJobs();
        _windows.Single().UpdateLayout();
        first.TextArea.TextView.EnsureVisualLines();
        first.Document.LineCount.Should().BeGreaterThan(100);
        for (var i = 0; i < 5; i++)
        {
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
        second.ScrollToLine(1);
        first.ScrollToLine(80);
        for (var i = 0; i < 5; i++)
        {
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
        first.TextArea.TextView.ScrollOffset.Y.Should().BeGreaterThan(0);
        second.TextArea.TextView.ScrollOffset.Y.Should().Be(0);
        var command = (Avalonia.Labs.Input.RoutedCommand)typeof(ApplicationCommands)
            .GetField("SplitWindowCommand")!.GetValue(null)!;
        command.CanExecute(null, second.TextArea).Should().BeTrue();
        command.Execute(null, second.TextArea);
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view)
            .OfType<HexIDE.Controls.TextEditing.TextEditor>().Should().HaveCount(1);
    }

    [AvaloniaFact]
    public void ReopeningSplitAfterResizeRestoresEqualRows()
    {
        using var vm = MakeViewModel();
        vm.Initialize(AModule());
        var view = Show(vm);
        view.ToggleSplit();
        var host = view.FindControl<Grid>("SplitHost")!;
        // GridSplitter writes both adjacent row heights when dragged.
        host.RowDefinitions[0].Height = new GridLength(480);
        host.RowDefinitions[2].Height = new GridLength(160);

        view.ToggleSplit();
        host.RowDefinitions.Select(row => row.Height).Should().Equal(
            new GridLength(1, GridUnitType.Star), new GridLength(0), new GridLength(0));
        view.ToggleSplit();
        host.RowDefinitions.Select(row => row.Height).Should().Equal(
            new GridLength(1, GridUnitType.Star), new GridLength(5), new GridLength(1, GridUnitType.Star));
        _windows.Single().UpdateLayout();
        var panes = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view)
            .OfType<HexIDE.Controls.TextEditing.TextEditor>().ToArray();
        panes.Should().HaveCount(2);
        panes[0].Bounds.Height.Should().BeApproximately(panes[1].Bounds.Height, 1);
    }

    [AvaloniaTheory]
    [InlineData("open_file")]
    [InlineData("definition")]
    [InlineData("outline")]
    [InlineData("bookmark")]
    public async Task NavigationAfterChangingPaneDoesNotImportTheOtherSelection(string route)
    {
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        var window = HexIDE.Integration.Tests.Automation.ToolbarAutomationNameTests.ShowMainView();
        _windows.Add(window);
        var main = (MainView)window.Content!;
        main.DataTemplates.Add(new ViewLocator());
        main.DataContext = composition.Root;
        var module = AModule();
        module.Owner.AddModule(module);
        composition.ProjectManager.AddProject(module.Owner);
        try
        {
            composition.EditorService.EditCode(module);
            Dispatcher.UIThread.RunJobs();
            using var vm = composition.DocumentDockService.OpenDocuments.OfType<CodeEditorViewModel>().Single();
            var view = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(main).OfType<CodeEditorView>().Single();
            view.ToggleSplit();
            Dispatcher.UIThread.RunJobs();
            var panes = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view)
                .OfType<HexIDE.Controls.TextEditing.TextEditor>().ToArray();
            var top = panes[0];
            var bottom = panes[1];
            top.Select(vm.BufferPrefixLength, vm.Document.TextLength - vm.BufferPrefixLength);
            bottom.TextArea.Focus().Should().BeTrue();
            var line = vm.Document.GetLineByNumber(vm.Document.LineCount - 2);
            bottom.Select(line.Offset, vm.Document.TextLength - line.Offset);
            var selection = bottom.SelectedText;

            // Navigate inside an existing selection: moving the caret alone leaves it active.
            composition.EditorService.EditCode(module);
            if (route != "open_file") top.TextArea.Focus().Should().BeTrue();
            switch (route)
            {
                case "open_file":
                    var app = Application.Current!;
                    var lifetimeField = typeof(Application).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        .Single(field => field.FieldType == typeof(Avalonia.Controls.ApplicationLifetimes.IApplicationLifetime));
                    var previous = lifetimeField.GetValue(app);
                    var lifetime = Substitute.For<Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime>();
                    lifetime.MainWindow.Returns(window);
                    lifetime.Windows.Returns(new[] { window });
                    lifetimeField.SetValue(app, lifetime);
                    using (var context = new HexIDE.Desktop.Server.IdeContext(composition.ProjectManager,
                        composition.DocumentDockService, _lspClient, composition.EditorService,
                        null!, null!, null!, null!, null!, null!, composition.Root, null!, null!, null!, null!,
                        null!, null!, composition.LocalizationService, null!))
                    {
                        try
                        {
                            var reply = await new HexIDE.Desktop.Server.HexIdeTools(context)
                                .OpenFileAsync(module.Name, line: line.LineNumber);
                            reply.Success.Should().BeTrue(reply.Error);
                        }
                        finally { lifetimeField.SetValue(app, previous); }
                    }
                    break;
                case "definition":
                    composition.EditorService.NavigateTo(vm.GetDocumentUriPublic(), line.LineNumber, 1).Should().BeTrue();
                    break;
                case "bookmark":
                    vm.BookmarkService.Toggle(vm.Identity, line.LineNumber - 1);
                    view.NextBookmark();
                    break;
                case "outline":
                    vm.NavigateToSymbol(new DocumentSymbol("Main", SymbolKind.Function,
                        new HexIDE.Lsp.Messages.Range(new Position(line.LineNumber - 1, 0), new Position(line.LineNumber - 1, 1)),
                        new HexIDE.Lsp.Messages.Range(new Position(line.LineNumber - 1, 0), new Position(line.LineNumber - 1, 1))));
                    break;
            }
            Dispatcher.UIThread.RunJobs();
            top.SelectionLength.Should().Be(0);
            top.CaretOffset.Should().Be(line.Offset);
            bottom.SelectedText.Should().Be(selection);
            ((System.Windows.Input.ICommand)ApplicationCommands.DeleteLineCommand).Execute(null);
            vm.Document.Text.Should().Contain("End Sub");
        }
        finally { composition.ProjectManager.UnloadAllProjects(); }
    }

    [AvaloniaFact]
    public void BreakpointNavigationClearsOnlyTheActivePanesSelection()
    {
        using var vm = MakeViewModel();
        var module = AModule();
        _runScope.RunningProject.Returns(module.Owner);
        vm.Initialize(module);
        var view = Show(vm);
        view.ToggleSplit();
        Dispatcher.UIThread.RunJobs();
        var panes = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view)
            .OfType<HexIDE.Controls.TextEditing.TextEditor>().ToArray();
        panes[0].Select(vm.BufferPrefixLength, 4);
        var otherSelection = panes[0].SelectedText;
        panes[1].TextArea.Focus().Should().BeTrue();
        panes[1].Select(vm.BufferPrefixLength, vm.Document.TextLength - vm.BufferPrefixLength);
        var line = vm.Document.LineCount - 2;
        vm.DebugController.Stopped += Raise.Event<Action<HexIDE.Runtime.Debugging.StoppedInfo>>(
            new HexIDE.Runtime.Debugging.StoppedInfo(HexIDE.Runtime.Debugging.StopReason.Breakpoint, module.Name, line));
        panes[1].SelectionLength.Should().Be(0);
        panes[1].TextArea.Caret.Line.Should().Be(line);
        panes[0].SelectedText.Should().Be(otherSelection);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SplitterDragReturnsFocusToPreviouslyActivePane(bool lower)
    {
        using var vm = MakeViewModel();
        vm.Initialize(AModule());
        var view = Show(vm);
        view.ToggleSplit();
        Dispatcher.UIThread.RunJobs();
        var panes = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view)
            .OfType<HexIDE.Controls.TextEditing.TextEditor>().ToArray();
        var pane = panes[lower ? 1 : 0];
        pane.TextArea.Focus().Should().BeTrue();
        var host = view.FindControl<Grid>("SplitHost")!;
        var splitter = host.Children.OfType<GridSplitter>().Single();
        var window = _windows.Single();
        window.UpdateLayout();
        var point = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseMove(point.WithY(point.Y + 40));
        window.MouseUp(point.WithY(point.Y + 40), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        pane.TextArea.IsKeyboardFocusWithin.Should().BeTrue();
        ((System.Windows.Input.ICommand)ApplicationCommands.SplitWindowCommand).CanExecute(null).Should().BeTrue();
        host.RowDefinitions[0].Height.Should().NotBe(host.RowDefinitions[2].Height);
    }

    public static IEnumerable<object[]> PaneCommands =>
        new[] { "DeleteLine", "SelectAll", "Cut", "Copy", "Paste", "Indent", "Outdent",
            "PasteAsComment", "GoToDefinition", "ToggleBookmark", "NextBookmark", "PreviousBookmark", "Delete" }
            .SelectMany(name => new[] { new object[] { name, false }, new object[] { name, true } });

    [AvaloniaTheory]
    [MemberData(nameof(PaneCommands))]
    public async Task CommandsUseFocusedPane(string name, bool key)
    {
        using var vm = MakeViewModel();
        vm.Initialize(AModule());
        var view = Show(vm);
        view.ToggleSplit();
        Dispatcher.UIThread.RunJobs();
        var panes = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view)
            .OfType<HexIDE.Controls.TextEditing.TextEditor>().ToArray();
        var top = panes[0];
        var bottom = panes[1];
        var firstLine = vm.Document.GetLineByNumber(vm.Document.LineCount - 3);
        var targetLine = vm.Document.GetLineByNumber(vm.Document.LineCount - 2);
        top.CaretOffset = firstLine.Offset;
        bottom.CaretOffset = targetLine.Offset;
        bottom.TextArea.Focus().Should().BeTrue();
        var offset = targetLine.Offset;
        var originalTop = vm.Document.GetText(firstLine);
        var originalTarget = vm.Document.GetText(targetLine);
        var clipboard = _windows.Single().Clipboard!;
        await clipboard.SetTextAsync("marker");
        var command = (Avalonia.Labs.Input.RoutedCommand)typeof(ApplicationCommands)
            .GetField(name + "Command")!.GetValue(null)!;
        if (key)
        {
            var gestures = command.Gestures;
            try
            {
                // An unassigned key exercises the binding route without assuming a platform modifier.
                command.Gestures = new[] { new Avalonia.Input.KeyGesture(Avalonia.Input.Key.F24) };
                bottom.TextArea.RaiseEvent(new Avalonia.Input.KeyEventArgs
                {
                    RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
                    Key = Avalonia.Input.Key.F24
                });
            }
            finally { command.Gestures = gestures; }
        }
        else
        {
            // ICommand is the menu route: its target is the last focused text area.
            ((System.Windows.Input.ICommand)command).Execute(null);
        }
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
        switch (name)
        {
            case "DeleteLine":
            case "Cut":
                vm.Document.Text.Should().Contain(originalTop).And.NotContain(originalTarget);
                break;
            case "SelectAll":
                bottom.SelectionLength.Should().Be(vm.Document.TextLength);
                top.SelectionLength.Should().Be(0);
                break;
            case "Copy":
                (await clipboard.TryGetTextAsync())!.ReplaceLineEndings("\n").Should().Be(originalTarget + "\n");
                break;
            case "Delete":
                vm.Document.GetText(targetLine).Should().Be(originalTarget[1..]);
                break;
            case "NextBookmark":
                vm.BookmarkService.Received().NextBookmark(vm.Identity, targetLine.LineNumber - 1);
                break;
            case "PreviousBookmark":
                vm.BookmarkService.Received().PreviousBookmark(vm.Identity, targetLine.LineNumber - 1);
                break;
            case "Paste":
                vm.Document.GetText(offset, 6).Should().Be("marker");
                break;
            case "PasteAsComment":
                vm.Document.GetText(offset, 1).Should().Be("'");
                break;
            case "Indent":
                vm.Document.GetText(targetLine).Length.Should().BeGreaterThan(originalTarget.Length);
                vm.Document.GetText(firstLine).Should().Be(originalTop);
                break;
            case "Outdent":
                vm.Document.GetText(targetLine).Length.Should().BeLessThan(originalTarget.Length);
                vm.Document.GetText(firstLine).Should().Be(originalTop);
                break;
            case "GoToDefinition":
                await _lspClient.Received().RequestDefinitionAsync(Arg.Any<string>(),
                    Arg.Is<Position>(p => p.Line == targetLine.LineNumber - 1), Arg.Any<CancellationToken>());
                break;
            case "ToggleBookmark":
                vm.BookmarkService.Received().Toggle(vm.Identity, targetLine.LineNumber - 1);
                break;
        }
    }

    [AvaloniaFact]
    public void SplitSizesSurviveViewRecreationAfterDesignerTab()
    {
        using var vm = MakeViewModel();
        vm.Initialize(AModule());
        var locator = new ViewLocator();
        var view = (CodeEditorView)locator.Build(vm)!;
        view.DataContext = vm;
        var window = new Window { Width = 1200, Height = 800, Content = view };
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        view.ToggleSplit();
        var host = view.FindControl<Grid>("SplitHost")!;
        host.RowDefinitions[0].Height = new GridLength(480);
        host.RowDefinitions[2].Height = new GridLength(160);
        window.Content = new Border();
        Dispatcher.UIThread.RunJobs();
        var reopened = (CodeEditorView)locator.Build(vm)!;
        reopened.DataContext = vm;
        window.Content = reopened;
        Dispatcher.UIThread.RunJobs();
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(reopened)
            .OfType<HexIDE.Controls.TextEditing.TextEditor>().Should().HaveCount(2);
        reopened.FindControl<Grid>("SplitHost")!.RowDefinitions.Select(row => row.Height)
            .Should().Equal(new GridLength(480), new GridLength(5), new GridLength(160));
    }

    [AvaloniaTheory]
    [InlineData("Find")]
    [InlineData("ToggleBreakpoint")]
    public async Task ShellCommandsUseFocusedPanesSharedNavigation(string name)
    {
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        var window = HexIDE.Integration.Tests.Automation.ToolbarAutomationNameTests.ShowMainView();
        _windows.Add(window);
        var main = (MainView)window.Content!;
        main.DataTemplates.Add(new ViewLocator());
        main.DataContext = composition.Root;
        var module = AModule();
        module.Owner.AddModule(module);
        composition.ProjectManager.AddProject(module.Owner);
        try
        {
            composition.EditorService.EditCode(module);
            Dispatcher.UIThread.RunJobs();
            var vm = composition.DocumentDockService.OpenDocuments.OfType<CodeEditorViewModel>().Single();
            var view = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(main).OfType<CodeEditorView>().Single();
            view.ToggleSplit();
            Dispatcher.UIThread.RunJobs();
            var panes = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view)
                .OfType<HexIDE.Controls.TextEditing.TextEditor>().ToArray();
            panes[0].CaretOffset = vm.BufferPrefixLength;
            var line = vm.Document.GetLineByNumber(vm.Document.LineCount - 2);
            panes[1].CaretOffset = line.Offset;
            panes[1].TextArea.Focus().Should().BeTrue();
            if (name == "ToggleBreakpoint")
            {
                ((System.Windows.Input.ICommand)ApplicationCommands.ToggleBreakpointCommand).Execute(null);
                vm.HasBreakpoint(line.LineNumber).Should().BeTrue();
                vm.HasBreakpoint(vm.Document.GetLineByOffset(panes[0].CaretOffset).LineNumber).Should().BeFalse();
            }
            else
            {
                // The Find dialog searches through the shared document navigation, not a view reference.
                var search = new FindReplaceViewModel(Substitute.For<IWindowManager>(),
                    composition.DocumentDockService, composition.LocalizationService);
                search.Scope = FindScope.CurrentModule;
                search.SearchText = "Debug";
                await search.FindNextAsync();
                panes[1].SelectedText.Should().Be("Debug");
                panes[0].SelectionLength.Should().Be(0);
            }
        }
        finally
        {
            composition.ProjectManager.UnloadAllProjects();
        }
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
            _runScope,
            localization);
    }

    private static ModuleDefinition AModule()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Sub Main()\r\n    Debug.Print 1\r\nEnd Sub\r\n");
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
