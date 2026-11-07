using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.IDE;

namespace HexIDE.Integration.Tests.Views;

public class ConflictGateTests
{
    [AvaloniaFact]
    public void BackgroundConflict_ShowsOnceAfterActivationBeforeIsActive()
    {
        var window = new Window { ShowActivated = false };
        var windows = Substitute.For<IWindowManager>();
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(Task.FromResult(false));
        var gate = new ConflictGate(windows, _ => Task.CompletedTask, _ => Task.CompletedTask, () => window);
        try
        {
            window.Show();
            window.IsActive.Should().BeFalse();
            gate.Enqueue([new WatchedFileTarget("Module1.bas", null, null, null, null)]);
            windows.DidNotReceive().ShowDialog(Arg.Any<IDialog>());

            bool? activeDuringEvent = null;
            window.Activated += (_, _) => activeDuringEvent = window.IsActive;
            Activate(window);
            activeDuringEvent.Should().BeFalse("Avalonia raises Activated before setting IsActive (#2881)");
            window.IsActive.Should().BeTrue();
            RunJobsKeepingContext();
            windows.Received(1).ShowDialog(Arg.Any<IDialog>());

            Activate(window);
            RunJobsKeepingContext();
            windows.Received(1).ShowDialog(Arg.Any<IDialog>());

            gate.Enqueue([new WatchedFileTarget("Module2.bas", null, null, null, null)]);
            windows.Received(2).ShowDialog(Arg.Any<IDialog>());
        }
        finally
        {
            RunJobsKeepingContext();
            window.Close();
        }
    }

    // Headless Window.Activate does not deliver a platform activation. Invoke Avalonia's handler so
    // the test exercises its actual Activated-before-IsActive ordering measured in #2881.
    private static void Activate(Window window) =>
        typeof(WindowBase).GetMethod("HandleActivated", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, null);

    private static void RunJobsKeepingContext()
    {
        var context = ExecutionContext.Capture();
        try { Dispatcher.UIThread.RunJobs(); }
        finally { if (context is not null) ExecutionContext.Restore(context); }
    }
}
