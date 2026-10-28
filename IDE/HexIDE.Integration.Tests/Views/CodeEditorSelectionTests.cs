using System;
using System.Collections.Generic;
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
            localization);
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
            Substitute.For<IProjectManager>());

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
