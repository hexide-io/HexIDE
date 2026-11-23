namespace HexIDE.Tests;

public class StartupCompositionTests : CompositionTestBase
{
    [AvaloniaFact]
    public void ErrorTrappingReachesTheRunnerInitiallyAndWhenSettingsChange()
    {
        var settings = Composition.SettingsService;
        var saved = settings.ErrorTrapping;
        try
        {
            settings.ErrorTrapping = HexIDE.Runtime.Debugging.ErrorTrapping.BreakInClassModule;
            _ = Composition.Root;
            Composition.ProjectRunnerService.ErrorTrapping.Should().Be(settings.ErrorTrapping);
            settings.ErrorTrapping = HexIDE.Runtime.Debugging.ErrorTrapping.BreakOnAllErrors;
            Composition.ProjectRunnerService.ErrorTrapping.Should().Be(settings.ErrorTrapping);
            ((HexIDE.Runtime.Debugging.IDebugGate)Composition.DebugController).ErrorTrapping.Should().Be(settings.ErrorTrapping);
        }
        finally { settings.ErrorTrapping = saved; }
    }

    [AvaloniaFact]
    public void DesignerFactoryUsesRuntimeConstructorAndSharedToolbox()
    {
        _ = Composition.Root;
        var factory = typeof(EditorService).GetField("formEditViewModelFactory",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(Composition.EditorService).Should().BeOfType<Func<HexIDE.VisualDesigner.FormEditViewModel>>().Subject;

        using var first = factory();
        using var second = factory();
        first.Should().NotBeSameAs(second);
        first.ToolsBoxToolViewModel.Should().BeSameAs(Composition.ToolBoxViewModel);
        second.ToolsBoxToolViewModel.Should().BeSameAs(Composition.ToolBoxViewModel);
        first.UndoStack.Should().NotBeNull();
        second.UndoStack.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void TheRunnerSharesTheArchiveSingleton()
    {
        // The runner refuses to start while an archive document is focused, so it must hold the archive service the
        // rest of the IDE uses. Under Pure.DI 2.5.7 it got null when the root was resolved first, as App does: the
        // generated code read the archive singleton's field without creating it (#4213; fixed upstream in
        // DevTeam/Pure.DI 392efa0e0, after 2.5.7).
        _ = Composition.Root;
        var runner = Composition.ProjectRunnerService.Should().BeOfType<ProjectRunnerService>().Subject;
        typeof(ProjectRunnerService).GetField("archives",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(runner).Should().BeSameAs(Composition.ArchiveProjects);
    }

    [AvaloniaFact]
    public void ResolvesEveryStartupRootInApplicationOrder()
    {
        // Keep the order in App.OnFrameworkInitializationCompleted: a different first root can hide a cycle.
        Composition.RunningProgram.Should().NotBeNull();
        Composition.ThemeService.Should().NotBeNull();
        Composition.SettingsService.Should().NotBeNull();
        Composition.KeymapService.Should().NotBeNull();
        Composition.LocalizationService.Should().NotBeNull();
        Composition.Root.Should().NotBeNull();
        Composition.LanguageServerMessages.Should().NotBeNull();
        Composition.LspClient.Should().NotBeNull();
        try
        {
            Composition.FileWatcherService.Should().NotBeNull();
            Composition.Recovery.Should().NotBeNull();
            Composition.WindowStateService.Should().NotBeNull();

            // DesktopStartupHook also resolves the add-in host and, in Debug, the server context.
            Composition.AddinLoader.Should().NotBeNull();
            Composition.HexIdeHost.Should().NotBeNull();
            Composition.ContributedProjects.Should().NotBeNull();
            Composition.ProjectManager.Should().NotBeNull();
            Composition.DocumentDockService.Should().NotBeNull();
            Composition.EditorService.Should().NotBeNull();
            Composition.ProjectRunnerService.Should().NotBeNull();
            Composition.ProjectService.Should().NotBeNull();
            Composition.EventBus.Should().NotBeNull();
            Composition.BookmarkService.Should().NotBeNull();
            Composition.BreakpointService.Should().NotBeNull();
            Composition.DebugController.Should().NotBeNull();
            Composition.ToolBoxViewModel.Should().NotBeNull();
            Composition.ProfileService.Should().NotBeNull();
            Composition.AddinProjectTemplateService.Should().NotBeNull();
            Composition.LanguageSwitchService.Should().NotBeNull();
            Composition.Capture.Should().NotBeNull();
            Composition.Pseudonyms.Should().NotBeNull();
            Composition.ArchiveProjects.Should().NotBeNull();
        }
        finally
        {
            Composition.AddinLoader.Dispose();
            Composition.FileWatcherService.Dispose();
        }
    }
}
