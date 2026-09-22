using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The runtime-error dialog offers only the choices its view model allows (hexide-io/HexIDE#594).
///
/// <para>
/// Each button's command is a view-model method, and Avalonia consults <c>Can{Name}</c> only when it takes one
/// <c>object?</c> parameter. The four took none, so every button was enabled although only End is meant to be.
/// The assertion has to be on the rendered buttons: the view model always answered correctly, and nothing
/// was asking it.
/// </para>
/// </summary>
public class RuntimeErrorViewIntegrationTests
{
    [AvaloniaFact]
    public void Only_End_is_enabled()
    {
        var view = new RuntimeErrorView { DataContext = new RuntimeErrorViewModel("Run-time error '5'") };
        var window = new Window { Content = view };
        window.Show();

        // In the order the view lays them out: Continue, End, Debug, Help.
        var enabled = view.GetLogicalDescendants().OfType<Button>().Select(b => b.IsEffectivelyEnabled).ToList();

        enabled.Should().Equal(false, true, false, false);
        window.Close();
    }
}
