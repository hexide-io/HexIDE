using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// hover reports a tip's text, and a tip that is a control has its text in its text blocks (#2336). Read by its ToString,
/// the designer box's tip, a StackPanel of three text blocks, was reported as "tip: Avalonia.Controls.StackPanel".
/// </summary>
public class TipTextTests
{
    private sealed record Labelled(string Label)
    {
        public override string ToString() => Label;
    }

    private sealed class Plain;

    [AvaloniaFact]
    public void A_control_tip_reads_as_its_visible_text_blocks_in_order()
    {
        var tip = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "Toolbar1" },
                new TextBlock { Text = "MSComctlLib.Toolbar" },
                new TextBlock { Text = "Hidden", IsVisible = false },
                new StackPanel { IsVisible = false, Children = { new TextBlock { Text = "Also hidden" } } },
                new TextBlock { Text = "  Not supported in HexIDE  " },
            },
        };

        UiAutomationDriver.TipText(tip).Should().Be("Toolbar1 / MSComctlLib.Toolbar / Not supported in HexIDE");
    }

    [AvaloniaFact]
    public void Text_and_other_tips_read_as_they_show_and_never_as_a_type_name()
    {
        UiAutomationDriver.TipText(null).Should().BeNull();
        UiAutomationDriver.TipText("  Save  ").Should().Be("Save");
        UiAutomationDriver.TipText("   ").Should().BeNull();
        UiAutomationDriver.TipText(new TextBlock { Text = "One block" }).Should().Be("One block");
        UiAutomationDriver.TipText(new Border()).Should().Be("a Border with no text to read");
        UiAutomationDriver.TipText(new Labelled("From ToString")).Should().Be("From ToString");
        UiAutomationDriver.TipText(new Plain()).Should().Be("a Plain with no text to read");
    }

    [AvaloniaFact]
    public void A_declared_control_tip_is_reported_by_its_text()
    {
        var button = new Button { Content = "Go", Width = 80, Height = 30 };
        ToolTip.SetTip(button, new StackPanel { Children = { new TextBlock { Text = "First" }, new TextBlock { Text = "Second" } } });
        var window = new Window { Width = 200, Height = 100, Content = button };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            UiAutomationDriver.DeclaredToolTipAt(button, new Point(10, 10)).Should().Be("First / Second");
        }
        finally { window.Close(); }
    }
}
