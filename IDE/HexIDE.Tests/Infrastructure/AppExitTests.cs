using System.IO;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using HexIDE.Forms.ViewModels;
using HexIDE.Addins;
using HexIDE.Desktop;
using HexIDE.IDE;
using HexIDE.Infrastructure;
using HexIDE.Lsp;

namespace HexIDE.Tests.Infrastructure;

public class AppExitTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_App_TryShutdown_waits_for_stdio_exit_after_close_confirmation(bool force)
    {
        var configuration = LanguageServerConfigLoader.DefaultFilePath();
        var oldConfiguration = File.Exists(configuration) ? File.ReadAllText(configuration) : null;
        var file = Path.GetTempFileName();
        var oldStartup = Static.StartupProjectHook;
        var oldDesktop = Static.DesktopStartupHook;
        var oldForce = Static.ForceCloseWithoutPrompt;
        var oldMain = Static.MainView;
        var oldRoot = Static.RootViewModel;
        var oldSingle = Static.SingleView;
        var oldRunning = RunningProgram.Current;
        var oldLogger = Serilog.Log.Logger;
        var application = Application.Current!;
        var lifetimeField = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(IApplicationLifetime));
        var oldLifetime = lifetimeField.GetValue(application);
        DISetup? setup = null;
        var loader = Substitute.For<IAddinLoader>();
        using var desktop = new ClassicDesktopStyleApplicationLifetime();
        try
        {
            var host = Path.Join(AppContext.BaseDirectory, "LspTestHost", "HexIDE.Lsp.TestHost.dll");
            File.WriteAllText(configuration, JsonSerializer.Serialize(new
            {
                servers = new[] { new { id = "exit-probe", extensions = new[] { ".exitprobe" },
                    languageId = "probe", transport = "stdio", command = "dotnet",
                    arguments = new[] { host, "recording-server", file } } },
            }));
            Static.ForceCloseWithoutPrompt = force;
            Static.StartupProjectHook = (_, _) => Task.CompletedTask;
            Static.DesktopStartupHook = (di, lifetime) =>
            {
                setup = di;
                DesktopStartup.DisposeOnShutdown(lifetime, loader);
            };
            lifetimeField.SetValue(application, desktop);
            // The headless runner has already set up its application. Invoke the lifetime's internal
            // setup hook to track real windows without starting a second dispatcher main loop.
            typeof(ClassicDesktopStyleApplicationLifetime).GetMethod("BeforeInit", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(desktop, null);
            var app = new App { ApplicationLifetime = desktop };
            app.LoadMarkup();
            app.OnFrameworkInitializationCompleted();
            setup.Should().NotBeNull();
            desktop.MainWindow!.Show();
            await setup!.LspClient.OpenDocumentAsync("file:///exit.exitprobe", "probe", TestContext.Current.CancellationToken);
            ExitStopTests.ReadMethods(file).Should().Contain("initialize");
            setup.ProjectManager.AddProject(TestHelpers.CreateProjectWithForm("Unsaved"));

            var retry = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelledRequests = 0;
            desktop.ShutdownRequested += (_, e) =>
            {
                if (e.Cancel) cancelledRequests++;
                else retry.TrySetResult(ExitStopTests.ReadMethods(file));
                // Keep the test runner's dispatcher alive after observing the real retry's decision.
                e.Cancel = true;
            };

            desktop.TryShutdown().Should().BeFalse();
            loader.DidNotReceive().Dispose();
            if (!force)
            {
                var prompt = await SavePrompt();
                ExitStopTests.ReadMethods(file).Should().NotContain("shutdown", "the save prompt precedes server stop");
                prompt.Cancel();
                await Task.Yield();
                loader.DidNotReceive().Dispose();
                desktop.MainWindow.IsVisible.Should().BeTrue();
                ExitStopTests.ReadMethods(file).Should().NotContain("shutdown", "cancelling save must keep the connection alive");
                desktop.TryShutdown().Should().BeFalse();
                (await SavePrompt()).No();
            }
            var received = await retry.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            received.Should().ContainInOrder("shutdown", "exit");
            loader.Received(1).Dispose();
            cancelledRequests.Should().BeGreaterThan(0);
            desktop.MainWindow.IsVisible.Should().BeFalse("save-on-exit finished before the retry");

            async Task<SaveChangesViewModel> SavePrompt()
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (true)
                {
                    if (desktop.Windows.Where(w => w.IsLoaded).Select(w => w.DataContext)
                        .OfType<SaveChangesViewModel>().SingleOrDefault() is { } prompt)
                        return prompt;
                    await Task.Delay(20, timeout.Token);
                }
            }
        }
        finally
        {
            if (setup is not null)
            {
                setup.FileWatcherService.Dispose();
                setup.Recovery.EndRun();
                await setup.LspClient.DisposeAsync();
            }
            desktop.Dispose();
            Static.ForceCloseWithoutPrompt = true;
            desktop.MainWindow?.Close();
            LoggingSetup.Shutdown();
            Serilog.Log.Logger = oldLogger;
            lifetimeField.SetValue(application, oldLifetime);
            Static.StartupProjectHook = oldStartup;
            Static.DesktopStartupHook = oldDesktop;
            Static.ForceCloseWithoutPrompt = oldForce;
            Static.MainView = oldMain;
            Static.RootViewModel = oldRoot;
            Static.SingleView = oldSingle;
            RunningProgram.Current = oldRunning;
            if (oldConfiguration is null) File.Delete(configuration);
            else File.WriteAllText(configuration, oldConfiguration);
            File.Delete(file);
        }
    }
}
