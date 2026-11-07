using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
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

public class CodeEditorOvertypeTests : IDisposable
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

    private static void Insert(Window window)
    {
        window.KeyPress(Key.Insert, RawInputModifiers.None, PhysicalKey.Insert, null);
        window.KeyRelease(Key.Insert, RawInputModifiers.None, PhysicalKey.Insert, null);
    }

    [AvaloniaFact]
    public void InsertTogglesOvertypeAndBackWithoutMovingTheCaret()
    {
        using var vm = MakeViewModel();
        vm.Initialize(AModule());
        var view = Show(vm);
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.TextArea.Focus();
        var offset = editor.CaretOffset;

        Insert(_windows[0]);
        editor.TextArea.OverstrikeMode.Should().BeTrue();
        editor.CaretOffset.Should().Be(offset);
        Insert(_windows[0]);
        editor.TextArea.OverstrikeMode.Should().BeFalse();
        editor.CaretOffset.Should().Be(offset);
    }

    [AvaloniaFact]
    public void InsertUpdatesTheStatusBarImmediatelyInBothDirections()
    {
        using var vm = MakeViewModel();
        vm.Initialize(AModule());
        var view = Show(vm);
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        // Isolate status propagation from the option enabling the library's key binding.
        editor.Options.AllowToggleOverstrikeMode = true;
        editor.TextArea.Focus();
        vm.StatusBar.InsertMode.Should().BeTrue();

        Insert(_windows[0]);
        vm.StatusBar.InsertMode.Should().BeFalse();
        Insert(_windows[0]);
        vm.StatusBar.InsertMode.Should().BeTrue();
    }
}
