using Avalonia.Controls.ApplicationLifetimes;
using HexIDE.Addins;
using HexIDE.Desktop;

namespace HexIDE.Tests.Infrastructure;

public class AddinShutdownTests
{
    [AvaloniaFact]
    public void Cancelled_request_keeps_addins_loaded()
    {
        using var desktop = new ClassicDesktopStyleApplicationLifetime();
        var loader = Substitute.For<IAddinLoader>();
        desktop.ShutdownRequested += (_, e) => e.Cancel = true;
        DesktopStartup.DisposeOnShutdown(desktop, loader);

        desktop.TryShutdown().Should().BeFalse();

        loader.DidNotReceive().Dispose();
    }

    [AvaloniaFact]
    public void Accepted_request_disposes_addins_once()
    {
        using var desktop = new ClassicDesktopStyleApplicationLifetime();
        var loader = Substitute.For<IAddinLoader>();
        DesktopStartup.DisposeOnShutdown(desktop, loader);
        // Subscribed after the guard, so the guard sees the request still going through. Cancelling it here keeps the
        // lifetime from calling Dispatcher.UIThread.InvokeShutdown(), which would end the dispatcher the headless test
        // session shares with every later test (AppExitTests does the same).
        desktop.ShutdownRequested += (_, e) => e.Cancel = true;

        desktop.TryShutdown();

        loader.Received(1).Dispose();
    }
}
