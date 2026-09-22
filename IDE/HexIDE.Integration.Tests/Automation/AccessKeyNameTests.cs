using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// A control captioned with an access key is named by its caption as displayed, not by the string with its
/// "_" marker (#578). The New Project dialog's Open button was "_Open" to UI Automation, so a screen reader
/// was handed the marker and an automation path had to carry it.
/// </summary>
public class AccessKeyNameTests
{
    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static string NameOf(Control control) => ControlAutomationPeer.CreatePeerForElement(control).GetName();

    [AvaloniaTheory]
    [InlineData("_Open", "Open")]
    [InlineData("Don't show this dialog in the f_uture", "Don't show this dialog in the future")]
    [InlineData("Save__As", "Save_As")]            // doubled: a literal underscore, no marker
    [InlineData("A__b_c", "A_bc")]                 // the first single one is the marker, wherever it falls
    [InlineData("Cancel", "Cancel")]
    public void Captioned_controls_are_named_as_displayed(string caption, string expected)
    {
        var controls = new Control[]
        {
            new Button { Content = caption },
            new CheckBox { Content = caption },
            new RadioButton { Content = caption },
            new ToggleButton { Content = caption },
            new RepeatButton { Content = caption },
        };
        var panel = new StackPanel();
        foreach (var c in controls) panel.Children.Add(c);
        var window = Show(panel);
        try
        {
            foreach (var c in controls)
                NameOf(c).Should().Be(expected, "a {0} captioned \"{1}\" displays \"{2}\"", c.GetType().Name, caption, expected);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_name_the_view_sets_itself_is_kept()
    {
        var button = new Button { Content = "_Open" };
        AutomationProperties.SetName(button, "Open the selected project");
        var window = Show(button);
        try
        {
            NameOf(button).Should().Be("Open the selected project");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Content_that_is_not_a_caption_keeps_the_peers_own_name()
    {
        var button = new Button { Content = new TextBlock { Text = "_x" } };
        var window = Show(button);
        try
        {
            AutomationProperties.GetName(button).Should().BeNull("the style sets a name only for a caption");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void An_icon_button_is_named_by_its_tooltip()
    {
        // Its peer named it by the icon's type, "Avalonia.Controls.Shapes.Path", on every such button (#542).
        var button = new Button { Content = new Avalonia.Controls.Shapes.Path() };
        ToolTip.SetTip(button, "Close");
        var window = Show(button);
        try
        {
            NameOf(button).Should().Be("Close");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void An_icon_button_with_no_tooltip_is_not_given_its_icons_type_name_in_a_dump()
    {
        var button = new Button { Content = new Avalonia.Controls.Shapes.Path() };
        var window = Show(new StackPanel { Children = { button } });
        try
        {
            var node = UiAutomationDriver.Dump(window, "Window", 5, interactiveOnly: true).Children.Single();

            node.Name.Should().BeNull("a type name neither describes the button nor tells it from its siblings");
            node.TypeNameAsName.Should().Be("Avalonia.Controls.Shapes.Path");
            node.Path.Should().Be("Window/Button");
            UiAutomationDriver.Resolve(window, node.Path).control.Should().BeSameAs(button);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_name_follows_the_caption_when_it_changes()
    {
        // A language switch replaces every caption in place.
        var button = new Button { Content = "_Open" };
        var window = Show(button);
        try
        {
            button.Content = "Ö_ffnen";
            Dispatcher.UIThread.RunJobs();
            NameOf(button).Should().Be("Öffnen");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Invoking_a_button_that_closes_its_window_reports_it_by_its_name()
    {
        // The New Project dialog's Open closes the dialog. Named after the invoke, the detached button had
        // lost its style and answered with its raw caption: "invoked Button '_Open'".
        var button = new Button { Content = "_Open" };
        var window = Show(button);
        button.Click += (_, _) => window.Close();

        var outcome = UiAutomationDriver.Interact(button, "invoke", null);

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Be("invoked Button 'Open'");
    }

    [AvaloniaFact]
    public void The_displayed_caption_addresses_the_control()
    {
        // Mid-word, because the path matcher already forgave a LEADING underscore; this one it could not.
        var box = new CheckBox { Content = "Don't show this dialog in the f_uture" };
        var window = Show(new StackPanel { Children = { box } });
        try
        {
            var (resolved, error) = UiAutomationDriver.Resolve(window, "Window/CheckBox[Don't show this dialog in the future]");
            resolved.Should().BeSameAs(box, error);
        }
        finally { window.Close(); }
    }
}
