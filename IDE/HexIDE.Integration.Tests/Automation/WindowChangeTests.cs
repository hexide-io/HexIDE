using System.Threading.Tasks;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

public class WindowChangeTests
{
    [AvaloniaFact]
    public async Task Wait_reads_confirmation_after_a_later_dispatcher_post()
    {
        var confirmed = false;
        var wait = WindowChange.WaitAsync(() => confirmed, TestContext.Current.CancellationToken);
        wait.IsCompleted.Should().BeFalse();
        var later = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            confirmed = true;
        }, DispatcherPriority.Background);
        try
        {
            (await wait).Should().BeTrue();
            confirmed.Should().BeTrue();
        }
        finally { await later; }
    }

    [AvaloniaFact]
    public async Task Wait_reports_an_unapplied_request()
    {
        (await WindowChange.WaitAsync(() => false, TestContext.Current.CancellationToken, 25)).Should().BeFalse();
    }
}
