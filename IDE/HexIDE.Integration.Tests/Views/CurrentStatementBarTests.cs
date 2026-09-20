using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The amber current-statement bar, which is where reading a module's name out of its wire name showed.
///
/// <para>
/// The interpreter reports a pause by bare module name, and rightly so — a run is one project and its gate
/// knows nothing of the IDE's documents. The editor used to compare that name against the tail of its own
/// URI, captured when the view attached. A module whose file is named differently from the module then
/// never matched, and the bar simply never appeared: no error, no log, a debugger that looks broken.
/// </para>
/// </summary>
public class CurrentStatementBarTests : IDisposable
{
    private readonly ILspClient _lspClient = Substitute.For<ILspClient>();
    private readonly IDebugController _debug = Substitute.For<IDebugController>();
    private readonly RunScope _runScope = new();
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

        _lspClient.IsRunning.Returns(false);
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
            Substitute.For<IBreakpointService>(),
            _debug,
            _runScope,
            localization);
    }

    /// <summary>A module whose own name and whose file name deliberately disagree.</summary>
    private static ModuleDefinition UtilitiesInUtilBas(ProjectDefinition project)
    {
        var module = new ModuleDefinition(project, "Utilities", ModuleKind.StandardModule)
        {
            AbsolutePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "util.bas"),
        };
        module.UpdateCode("Sub One()\r\nEnd Sub\r\n\r\nSub Two()\r\nEnd Sub\r\n");
        project.AddModule(module);
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

    private static CurrentLineRenderer BarOf(CodeEditorView view) =>
        view.FindControl<TextEditor>("TextEditor")!.TextArea.TextView
            .BackgroundRenderers.OfType<CurrentLineRenderer>().Single();

    private void Pause(string module, int line)
    {
        _debug.Stopped += Raise.Event<Action<StoppedInfo>>(
            new StoppedInfo(StopReason.Breakpoint, module, line));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void PausingInAModuleWhoseFileIsNamedDifferentlyShowsTheBar()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = UtilitiesInUtilBas(project);
        _runScope.RunningProject = project;

        using var vm = MakeViewModel();
        vm.Initialize(module);
        var view = Show(vm);

        Pause("Utilities", 4);

        BarOf(view).Line.Should().Be(4,
            "the module answers to Utilities; util.bas is only where it happens to be stored");
    }

    /// <summary>
    /// The half a name comparison alone cannot get right: a group whose two projects each hold a
    /// <c>Module1</c>. A pause in one must not light the bar in the other's editor.
    /// </summary>
    [AvaloniaFact]
    public void PausingInOneProjectDoesNotLightTheOtherProjectsEditor()
    {
        var running = new ProjectDefinition(VBProjectType.EXE, "Running");
        var runningModule = new ModuleDefinition(running, "Module1", ModuleKind.StandardModule);
        runningModule.UpdateCode("Sub Main()\r\nEnd Sub\r\n");
        running.AddModule(runningModule);

        var idle = new ProjectDefinition(VBProjectType.EXE, "Idle");
        var idleModule = new ModuleDefinition(idle, "Module1", ModuleKind.StandardModule);
        idleModule.UpdateCode("Sub Main()\r\nEnd Sub\r\n");
        idle.AddModule(idleModule);

        _runScope.RunningProject = running;

        using var runningVm = MakeViewModel();
        runningVm.Initialize(runningModule);
        var runningView = Show(runningVm);

        using var idleVm = MakeViewModel();
        idleVm.Initialize(idleModule);
        var idleView = Show(idleVm);

        Pause("Module1", 1);

        BarOf(runningView).Line.Should().Be(1);
        BarOf(idleView).Line.Should().BeNull(
            "that Module1 is in a project that is not running, and nothing in it is executing");
    }

    [AvaloniaFact]
    public void ARenamedModuleStillShowsTheBar()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = UtilitiesInUtilBas(project);
        _runScope.RunningProject = project;

        using var vm = MakeViewModel();
        vm.Initialize(module);
        var view = Show(vm);

        module.Name = "Helpers";
        Pause("Helpers", 1);

        BarOf(view).Line.Should().Be(1,
            "the name is read off the definition when the pause arrives, not captured when the view attached");
    }

    [AvaloniaFact]
    public void APauseInAnotherModuleClearsTheBar()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = UtilitiesInUtilBas(project);
        _runScope.RunningProject = project;

        using var vm = MakeViewModel();
        vm.Initialize(module);
        var view = Show(vm);

        Pause("Utilities", 4);
        Pause("SomethingElse", 2);

        BarOf(view).Line.Should().BeNull();
    }
}
