using System.Threading.Tasks;
using Avalonia.Headless.XUnit;

namespace HexIDE.Integration.Tests.Views;

public class MainWindowStartupTests : WindowTestFixture
{
    [AvaloniaFact]
    public void Opening_without_activation_runs_the_startup_hook_once()
    {
        var previous = Static.StartupProjectHook;
        var calls = 0;
        Static.StartupProjectHook = (_, _) =>
        {
            calls++;
            return Task.CompletedTask;
        };
        try
        {
            var composition = new DISetup();
            var window = new MainWindow { DataContext = composition.Root, ShowActivated = false };
            var activations = 0;
            window.Activated += (_, _) => activations++;

            window.Show();

            activations.Should().Be(0);
            calls.Should().Be(1);

            window.Activate();
            window.Hide();
            window.Show();
            calls.Should().Be(1);
        }
        finally { Static.StartupProjectHook = previous; }
    }
}
