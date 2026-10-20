using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// inspect_element's selectionItems is the selection, and read as the list's items it made a dropdown holding two look
/// like one holding the one selected: a working change to Startup Object read as a failed one (#1263). itemCount says
/// how many items there are, open or closed.
/// </summary>
public class InspectItemCountTests
{
    [AvaloniaFact]
    public void A_dropdown_reports_how_many_items_it_holds_closed_and_open()
    {
        var combo = new ComboBox { ItemsSource = new[] { "Sub Main", "Form1" }, SelectedIndex = 1 };
        var window = new Window { Width = 300, Height = 200, Content = combo };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var closed = UiAutomationDriver.Inspect(combo, "Window/ComboBox", window);
            closed.SelectionItems.Should().Equal("Form1");
            closed.ItemCount.Should().Be(2);

            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            var open = UiAutomationDriver.Inspect(combo, "Window/ComboBox", window);
            open.SelectionItems.Should().Equal("Form1");
            open.ItemCount.Should().Be(2);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_control_that_holds_no_items_reports_no_count()
    {
        var button = new Button { Content = "OK" };
        var window = new Window { Content = button };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            UiAutomationDriver.Inspect(button, "Window/Button", window).ItemCount.Should().BeNull();
        }
        finally { window.Close(); }
    }
}
