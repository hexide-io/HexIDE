using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using HexIDE.Addins;
#if DEBUG
using System.Threading;
using HexIDE.Desktop.Server;   // MCP server — DEBUG-only (compiled out of Release)
#endif
using HexIDE.Bookmarks;
using HexIDE.IDE;
using HexIDE.Projects;

namespace HexIDE.Desktop;

internal static class DesktopStartup
{
    public static void Register()
    {
        if (ServerOptions.Personality is { } p &&
            System.Enum.TryParse<PersonalityName>(p, ignoreCase: true, out var name))
            HexIDE.Static.Personality = name;

        // Session developer mode — set before the UI builds so the title suffix and Developer node
        // see it. Never persisted; a launch without --developer-mode is never in developer mode.
        // DEBUG-only: in Release builds the flag is inert (and DeveloperModeService.IsEnabled is
        // hard-false), so a distributed binary cannot enter developer mode at all.
#if DEBUG
        HexIDE.Static.DeveloperMode = ServerOptions.DeveloperMode;
#endif

        if (ServerOptions.NewProject)
            HexIDE.Static.StartupProjectHook = async (ps, pm) =>
            {
                await ps.CreateNewProject(IProjectTemplate.StandardEXE);
                if (pm.StartupProject is { } project)
                {
                    var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hexide_" + project.Name);
                    await ps.SaveProjectToDirectory(project, dir);
                }
            };
        else if (ServerOptions.ProjectPath is { } path)
            HexIDE.Static.StartupProjectHook = (ps, _) => ps.OpenProject(path);

        HexIDE.Static.DesktopStartupHook = (setup, desktop) =>
        {
            // Always: load add-ins from the addins/ folder beside the exe.
            var loader = setup.AddinLoader;
            loader.LoadAll(setup.HexIdeHost);
            desktop.ShutdownRequested += (_, _) => loader.Dispose();

            // First-load consent: prompt for add-ins awaiting consent AFTER the window is shown (so the
            // modal has an owner). Not a dev tool — runs in Release too.
            desktop.MainWindow!.Opened += async (_, _) =>
            {
                try { await loader.PromptPendingConsentAsync(); }
                catch (Exception ex) { Serilog.Log.Error(ex, "Add-in consent pass failed"); }
            };

#if DEBUG
            // MCP server: DEV-only (compiled out of Release), and only when --server-port is specified.
            if (ServerOptions.Port is not { } port)
                return;

            var ctx = new IdeContext(setup.ProjectManager, setup.DocumentDockService, setup.LspClient, setup.EditorService, setup.ProjectRunnerService, setup.ProjectService, setup.BookmarkService, setup.BreakpointService, setup.DebugController, HexIDE.Static.RootViewModel!, setup.ToolBoxViewModel, setup.PersonalityService, setup.AddinProjectTemplateService, setup.LanguageSwitchService);
            var cts = new CancellationTokenSource();

            desktop.MainWindow!.Opened += (_, _) =>
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await IdeServer.RunAsync(port, ctx, cts.Token);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Serilog.Log.Error(ex, "HexIDE MCP server failed to start on port {Port}", port);
                    }
                });

            desktop.ShutdownRequested += (_, _) =>
            {
                ctx.Dispose();
                cts.Cancel();
            };
#endif
        };
    }
}
