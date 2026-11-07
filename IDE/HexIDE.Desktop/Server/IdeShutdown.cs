using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace HexIDE.Desktop.Server;

internal static class IdeShutdown
{
    public static (bool ProjectStopped, int DialogsClosed) Prepare(IdeContext ctx, bool force)
    {
        Static.ForceCloseWithoutPrompt = force;

        // Only when force was asked for. force=false promises exactly what a user closing the window
        // sees, and a user closing it does not have their running program stopped or their open
        // dialogs shut from under them — so that path is left alone, and the IDE staying up is then
        // a correct outcome rather than the bug below.
        //
        // A running program owns windows the main window does not: its VBFormRuntime, and any MsgBox
        // or InputBox layered over that. Under ShutdownMode.OnLastWindowClose those keep the app
        // alive after the main window goes, and the symptom lands on whoever builds next —
        // "file is locked by HexIDE.Desktop" — which looks nothing like a shutdown problem.
        var stopped = false;
        if (force && ctx.ProjectRunnerService.CanEndProject)
        {
            ctx.ProjectRunnerService.EndProject();
            stopped = true;
        }

        var lifetime = Avalonia.Application.Current!.ApplicationLifetime as
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        var mainWindow = lifetime?.MainWindow;

        // Whatever survived that — a dialog whose owner is gone, an IDE modal, a runtime error box.
        // Close children before parents: closing an owner does not close what it owns, and a dialog
        // outliving its owner is exactly the window that keeps the process up.
        var closed = 0;
        if (force && lifetime is not null)
        {
            foreach (var w in lifetime.Windows.Reverse().ToList())
            {
                if (w == mainWindow || !w.IsVisible)
                    continue;
                w.Close();
                closed++;
            }
        }
        return (stopped, closed);
    }

    public static void CloseMainWindow()
    {
        var lifetime = Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        lifetime?.MainWindow?.Close();
    }
}
