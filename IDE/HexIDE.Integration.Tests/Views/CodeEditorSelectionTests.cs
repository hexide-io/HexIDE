using HexIDE.Runtime.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using HexIDE.Addins;
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

public class CodeEditorSelectionTests : IDisposable
{
    private readonly ILspClient _lspClient = Substitute.For<ILspClient>();
    private readonly List<Window> _windows = [];

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        GC.SuppressFinalize(this);
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

        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);

        _lspClient.IsRunning.Returns(true);
        _lspClient.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DocumentSymbol>());

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
            localization, new Vb6ProjectFileFormat(), new HexIDE.Runtime.Serialization.Vb6ReadOnlyRegionReader());
    }

    private static ModuleDefinition AModule()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Sub Main()\r\nEnd Sub\r\n");
        return module;
    }

    /// <summary>Puts the view in a real visual tree — which is what raises the attach the wiring hangs off.</summary>
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

    [AvaloniaFact]
    public void BreakpointContextItemUsesTheSharedCommandAndGesture()
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        using var vm = MakeViewModel().Initialize(AModule());
        var view = Show(vm);
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        var flyout = (MenuFlyout)editor.ContextFlyout!;
        try
        {
            editor.TextArea.Focus();
            flyout.ShowAt(editor);
            Dispatcher.UIThread.RunJobs();
            var item = flyout.Items.OfType<MenuItem>()
                .Single(i => Equals(i.Header, localization.GetString("Str.CodeEditor.Context.ToggleBreakpoint")));

            item.Command.Should().BeSameAs(ApplicationCommands.ToggleBreakpointCommand);
            item.InputGesture.Should().Be(ApplicationCommands.ToggleBreakpointCommand.Gestures.FirstOrDefault());
        }
        finally { flyout.Hide(); }
    }

    [AvaloniaFact]
    public void FormatSelectionHasACapabilityGatedCommandBesideFormatDocument()
    {
        using var vm = MakeViewModel().Initialize(AModule());
        var view = Show(vm);
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        var items = ((MenuFlyout)editor.ContextFlyout!).Items.OfType<MenuItem>().ToList();
        var document = items.FindIndex(i => ReferenceEquals(i.Command, ApplicationCommands.FormatDocumentCommand));
        document.Should().BeGreaterThanOrEqualTo(0);
        items[document + 1].Command.Should().NotBeNull();
        ((Avalonia.Labs.Input.RoutedCommand)items[document + 1].Command!).Name.Should().Be("FormatSelectionCommand");
        items[document + 1].Command!.CanExecute(null).Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData(2)]
    [InlineData(8)]
    public async System.Threading.Tasks.Task FormatSelectionSendsRangeAndAppliesGuardedEditsAsOneUndoStep(int tabWidth)
    {
        using var vm = MakeViewModel().Initialize(AModule());
        var view = Show(vm);
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        vm.Settings.TabWidth.Returns(tabWidth);
        var declaration = vm.Document.GetLineByOffset(vm.Document.Text.IndexOf("Sub Main", StringComparison.Ordinal));
        vm.Document.Insert(declaration.EndOffset, "\r\nAttribute Main.VB_Description = \"keep\"");
        var before = vm.Document.Text;
        var offset = before.IndexOf("Sub Main", StringComparison.Ordinal);
        editor.Select(offset, "Sub Main()".Length);
        var start = vm.Document.GetLocation(offset);
        var endLine = vm.Document.GetLocation(before.IndexOf("End Sub", StringComparison.Ordinal)).Line - 1;
        var range = new HexIDE.Lsp.Messages.Range(new Position(start.Line - 1, 0),
            new Position(start.Line - 1, 10));
        _lspClient.SupportsRangeFormatting(Arg.Any<string>()).Returns(true);
        _lspClient.RequestRangeFormattingAsync(Arg.Any<string>(), range, new FormattingOptions(tabWidth, true), Arg.Any<CancellationToken>())
            .Returns([new TextEdit(new HexIDE.Lsp.Messages.Range(new Position(start.Line - 1, 0),
                new Position(start.Line - 1, 10)), "Sub MAIN()"),
                new TextEdit(new HexIDE.Lsp.Messages.Range(new Position(endLine, 0),
                    new Position(endLine, 7)), "End SUB"),
                new TextEdit(new HexIDE.Lsp.Messages.Range(new Position(start.Line, 0),
                    new Position(start.Line, 9)), "FORBIDDEN"),
                new TextEdit(new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(0, 0)), "' forbidden\r\n")]);
        vm.Document.UndoStack.ClearAll();

        var format = typeof(CodeEditorView).GetMethod("FormatSelectionAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        format.Should().NotBeNull();
        await (System.Threading.Tasks.Task)format!.Invoke(view, null)!;

        await _lspClient.Received(1).RequestRangeFormattingAsync(vm.GetDocumentUriPublic(), range,
            new FormattingOptions(tabWidth, true), Arg.Any<CancellationToken>());
        vm.Document.Text.Should().Be(before.Replace("Sub Main()", "Sub MAIN()").Replace("End Sub", "End SUB"));
        vm.Document.UndoStack.Undo();
        vm.Document.Text.Should().Be(before);
        vm.Document.UndoStack.CanUndo.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, false)]
    [InlineData(false, 3, false)]
    [InlineData(true, 3, true)]
    public void FormatSelectionCommandRequiresSelectionAndRangeProvider(bool provider, int length, bool expected)
    {
        using var vm = MakeViewModel().Initialize(AModule());
        var view = Show(vm);
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        _lspClient.SupportsRangeFormatting(Arg.Any<string>()).Returns(provider);
        editor.Select(vm.Document.Text.IndexOf("Sub Main", StringComparison.Ordinal), length);
        var field = typeof(ApplicationCommands).GetField("FormatSelectionCommand");
        field.Should().NotBeNull();
        var command = (Avalonia.Labs.Input.RoutedCommand)field!.GetValue(null)!;
        command.CanExecute(null, editor).Should().Be(expected);
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task FormatSelectionRequeriesWhenServerCapabilitiesChange()
    {
        using var vm = MakeViewModel().Initialize(AModule());
        var view = Show(vm);
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.Focus();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle);
        var field = typeof(ApplicationCommands).GetField("FormatSelectionCommand");
        field.Should().NotBeNull();
        var command = (System.Windows.Input.ICommand)field!.GetValue(null)!;
        var requeries = 0;
        EventHandler handler = (_, _) => ++requeries;
        command.CanExecuteChanged += handler;
        try
        {
            _lspClient.StateChanged += Raise.EventWith(_lspClient, EventArgs.Empty);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle);
            requeries.Should().BeGreaterThan(0);
            requeries = 0;
            vm.SelectionStart = vm.Document.Text.IndexOf("Sub Main", StringComparison.Ordinal);
            vm.SelectionLength = 3;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle);
            requeries.Should().BeGreaterThan(0);
        }
        finally { command.CanExecuteChanged -= handler; }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServerSelectionPlacedBeforeTheViewAttachesIsShown(bool carried)
    {
        using BaseEditorWindowViewModel vm = carried
            ? new RelatedDocumentEditorViewModel(_lspClient, Substitute.For<IWindowManager>(),
                Substitute.For<ILocalizationService>(), Substitute.For<HexIDE.IDE.ISettingsService>())
            : MakeViewModel().Initialize(AModule());
        var searchable = (ISearchableDocument)vm;
        searchable.Document.Text = "first line\nsecond line\n";
        searchable.CaretOffset = 11;
        searchable.SelectionStart = 11;
        searchable.SelectionLength = 6;
        UserControl view = carried ? new RelatedDocumentEditorView() : new CodeEditorView();
        view.DataContext = vm;
        var window = new Window { Width = 1200, Height = 800, Content = view };
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.SelectionStart.Should().Be(11);
        editor.SelectionLength.Should().Be(6);
        editor.SelectedText.Should().Be("second");
    }

    [AvaloniaFact]
    public void EditorSelectionKeepsViewModelAndAddinInStep()
    {
        using var vm = MakeViewModel();
        vm.Initialize(AModule());
        var view = Show(vm);
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        var start = vm.Document.Text.IndexOf("Sub Main", StringComparison.Ordinal);
        var dock = Substitute.For<IDocumentDockService>();
        dock.ActiveDocument.Returns(vm);
        var addin = new AddinEditorService(dock, Substitute.For<IEditorService>(),
            Substitute.For<IProjectManager>(), format: new Vb6ProjectFileFormat(), reader: new HexIDE.Runtime.Serialization.Vb6ReadOnlyRegionReader());

        foreach (var length in new[] { 3, 8, 0 })
        {
            editor.Select(start, length);
            vm.SelectionStart.Should().Be(start);
            vm.SelectionLength.Should().Be(length);
            addin.GetSelection()!.SelectedText.Should().Be(vm.Document.GetText(start, length));
        }

        vm.SelectionStart = start + 4;
        vm.SelectionLength = 4;
        editor.SelectionStart.Should().Be(start + 4);
        editor.SelectionLength.Should().Be(4);
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task SelectedTextFindUsesTheEditorSelectionOnly()
    {
        using var vm = MakeViewModel();
        var module = AModule();
        module.UpdateCode("Sub Main()\r\nDebug.Print \"beta\"\r\nDebug.Print \"beta\"\r\nEnd Sub\r\n");
        vm.Initialize(module);
        var view = Show(vm);
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        var start = vm.Document.Text.IndexOf("Debug.Print", StringComparison.Ordinal);
        editor.Select(start, "Debug.Print \"beta\"".Length);
        var dock = Substitute.For<IDocumentDockService>();
        dock.ActiveDocument.Returns(vm);
        var find = new FindReplaceViewModel(Substitute.For<IWindowManager>(), dock,
            Substitute.For<ILocalizationService>())
        {
            Scope = FindScope.SelectedText,
            SearchText = "beta"
        };

        await find.FindNextAsync();
        vm.SelectionStart.Should().Be(start + 13);
        vm.SelectionLength.Should().Be(4);
        await find.FindNextAsync();
        vm.SelectionStart.Should().Be(start + 13);
        editor.SelectedText.Should().Be("beta");
    }

}
