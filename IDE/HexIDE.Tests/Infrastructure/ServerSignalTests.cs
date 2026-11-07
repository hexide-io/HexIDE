using System.IO;

namespace HexIDE.Tests.Infrastructure;

public class ServerSignalTests
{
    private static string Source(string file) => ToolSource.WithoutComments(File.ReadAllText(
        Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", file))).ReplaceLineEndings("\n");

    [Fact]
    public void Embedded_host_does_not_own_process_signals()
    {
        var source = Source("Server/IdeServer.cs");
        source.Should().Contain("builder.Services.RemoveAll<IHostLifetime>();");
        source.Should().Contain("builder.Services.AddSingleton<IHostLifetime, IdeServerLifetime>();");
    }

    [Fact]
    public void Sigterm_posts_the_shared_shutdown_to_the_UI_thread_and_is_disposed_on_exit()
    {
        var source = Source("DesktopStartup.cs");
        source.Should().Contain("PosixSignalRegistration.Create(PosixSignal.SIGTERM,");
        // The signal closes without the prompt, as shutdown_ide does, but keeps the unsaved work for the next start: nobody
        // chose to lose it (#499). The keep comes first, so the close that follows does not delete the backup.
        source.Should().MatchRegex(@"signal.Cancel = true;\s*Avalonia.Threading.Dispatcher.UIThread.Post\(\(\) =>\s*\{\s*setup.Recovery.KeepForRecovery\(\);\s*IdeShutdown.Prepare\(ctx, force: true\);\s*IdeShutdown.CloseMainWindow\(\);");
        source.Should().MatchRegex(@"desktop.Exit \+= [\s\S]*?termination\?\.Dispose\(\);");
    }

    [Fact]
    public void Tool_and_signal_use_the_same_shutdown_cleanup()
    {
        var source = Source("Server/HexIdeTools.cs");
        source.Should().Contain("IdeShutdown.Prepare(ctx, force)");
        source.Should().Contain("Dispatcher.UIThread.Post(IdeShutdown.CloseMainWindow);");
        // shutdown_ide's caller chose to discard, so its close deletes the backup: only the signal keeps it (#499).
        source.Should().NotContain("KeepForRecovery");
        Source("Server/IdeShutdown.cs").Should().NotContain("KeepForRecovery");
    }
}
