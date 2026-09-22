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

        // Deliberately OUTSIDE the #if DEBUG below, and the asymmetry is the point: developer mode is
        // inert in a distributed build by design, while the capture ships in one. Gating this the same way
        // would take the inspector away from the people it exists for.
        HexIDE.Static.CaptureLsp = ServerOptions.CaptureLsp;

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
                    // Through the shared helper, NOT a second copy of the same path rule. This line
                    // used to build "%TEMP%/hexide_{Name}" itself, and since --newproject always names
                    // the project Project1, every automation run in every session saved into one
                    // directory — the larger half of what hexide-io/HexIDE#260 observed, because the MCP
                    // dev loop uses this flag constantly. Two implementations of one rule is the same
                    // shape as the didOpen replay that drifted in #272.
                    var dir = ProjectService.ProjectFilesDirectory(project);
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

            var ctx = new IdeContext(setup.ProjectManager, setup.DocumentDockService, setup.LspClient, setup.EditorService, setup.ProjectRunnerService, setup.ProjectService, setup.BookmarkService, setup.BreakpointService, setup.DebugController, HexIDE.Static.RootViewModel!, setup.ToolBoxViewModel, setup.PersonalityService, setup.AddinProjectTemplateService, setup.LanguageSwitchService, setup.Capture, setup.Pseudonyms, setup.LocalizationService);
            var cts = new CancellationTokenSource();

            desktop.MainWindow!.Opened += (_, _) =>
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await IdeServer.RunAsync(port, ctx, cts.Token);
                    }
                    // Fatal, and said aloud. Carrying on would leave an IDE that looks normal and serves
                    // nothing, while a client goes on driving whichever process still holds the port --
                    // the old one, after a relaunch that did not take (hexide-io/HexIDE#53). No dialog: a
                    // modal would wedge exactly the automation run that needs to see this exit.
                    catch (IdeServerStartException ex)
                    {
                        var (exitCode, message) = HexIDE.Net.ServerStartFailure.Describe(ex.InnerException!, ex.Port);
                        Serilog.Log.Error(ex, "HexIDE MCP server failed to start on port {Port}; exiting with {ExitCode}", ex.Port, exitCode);
                        ConsoleOutput.Write(message);
                        // The forced Shutdown still closes every window, so the main window's Closing handler
                        // runs and cannot veto. What it skips is ShutdownRequested, so the cleanup that matters
                        // is done here by hand. The add-in loader goes first, because it must be disposed before
                        // the forced Shutdown closes a consent dialog: until then, that dialog reads on its
                        // return as a refusal and is persisted as Block for an add-in the user never answered
                        // about (hexide-io/HexIDE#547). The log is closed only once the windows have closed, so
                        // anything they log on the way out, a failed save on exit included, still reaches the
                        // file; the language server and file watcher are left to process exit. The exit itself is
                        // unconditional, because exit code 3 is what the rebuild cycle relies on (#53, #525).
                        // A launch that failed still writes to the profile it shares: hexide-io/HexIDE#557.
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                loader.Dispose();
                                cts.Cancel();
                                ctx.Dispose();
                            }
                            catch (Exception cleanup)
                            {
                                Serilog.Log.Warning(cleanup, "Cleanup before exiting with {ExitCode} failed; exiting anyway", exitCode);
                            }
                            finally
                            {
                                desktop.Shutdown(exitCode);
                                HexIDE.Infrastructure.LoggingSetup.Shutdown();
                            }
                        });
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Serilog.Log.Error(ex, "HexIDE MCP server stopped with an error on port {Port}", port);
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
