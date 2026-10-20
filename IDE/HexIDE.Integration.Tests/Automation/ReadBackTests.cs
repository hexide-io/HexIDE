using Avalonia.Automation;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// interact replies with what an action left, read back once it returns, as select does (#1259), and says when that
/// could not be read, rather than claiming the action took; and it refuses to act where it cannot read whether the
/// control shows (#1280).
/// </summary>
public class ReadBackTests
{
    private static T Shown<T>(T control) where T : Control
    {
        var window = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { control } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return control;
    }

    private static string Reply(InteractOutcome outcome) => (outcome.Success ? outcome.Detail : outcome.Error)!;

    [AvaloniaFact]
    public void A_toggle_says_what_state_it_left()
    {
        var box = Shown(new CheckBox { Name = "Wrap", Content = "Wrap" });

        var outcome = UiAutomationDriver.Interact(box, "toggle", null);

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Be("toggled 'Wrap': it is now on");
        Reply(UiAutomationDriver.Interact(box, "toggle", null)).Should().Be("toggled 'Wrap': it is now off");
    }

    [AvaloniaFact]
    public void A_toggle_the_control_put_back_is_refused_as_not_taken()
    {
        var box = Shown(new CheckBox { Name = "Wrap", Content = "Wrap" });
        box.IsCheckedChanged += (_, _) => { if (box.IsChecked == true) box.IsChecked = false; };

        var outcome = UiAutomationDriver.Interact(box, "toggle", null);

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Be("'Wrap' was not toggled: it is still off");
    }

    [AvaloniaFact]
    public void A_toggle_whose_state_cannot_be_read_says_so()
    {
        var toggle = Substitute.For<IToggleProvider>();
        toggle.ToggleState.Returns(_ => throw new InvalidOperationException("peer gone"));

        var outcome = UiAutomationDriver.ToggleVerified(toggle, "Wrap");

        toggle.Received(1).Toggle();
        outcome.Success.Should().BeFalse();
        outcome.Error.Should().StartWith("'Wrap': toggle was sent, but its state could not be read back");
    }

    [AvaloniaFact]
    public void An_expand_the_control_put_back_is_refused_as_not_taken()
    {
        var expander = Shown(new Expander { Name = "More", Header = "More" });
        expander.PropertyChanged += (_, e) =>
        {
            if (e.Property == Expander.IsExpandedProperty && expander.IsExpanded)
                expander.IsExpanded = false;
        };

        var outcome = UiAutomationDriver.Interact(expander, "expand", null);

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Be("'More' was not expanded: it is still collapsed");
    }

    [AvaloniaFact]
    public void An_expand_and_a_collapse_that_took_say_so()
    {
        var expander = Shown(new Expander { Name = "More", Header = "More" });

        Reply(UiAutomationDriver.Interact(expander, "expand", null)).Should().Be("expanded 'More'");
        Reply(UiAutomationDriver.Interact(expander, "collapse", null)).Should().Be("collapsed 'More'");
    }

    [AvaloniaTheory]
    [InlineData(true, ExpandCollapseState.Collapsed, "'More' was not expanded: it is still collapsed")]
    [InlineData(true, ExpandCollapseState.LeafNode, "'More' was not expanded: it has nothing to show")]
    [InlineData(true, ExpandCollapseState.PartiallyExpanded, "expanded 'More'")]
    [InlineData(false, ExpandCollapseState.Expanded, "'More' was not collapsed: it is still expanded")]
    [InlineData(false, ExpandCollapseState.PartiallyExpanded, "'More' was not collapsed: it is still expanded")]
    [InlineData(false, ExpandCollapseState.LeafNode, "collapsed 'More'")]
    public void An_expand_or_collapse_is_replied_to_from_the_state_read_back(bool expand, ExpandCollapseState now, string reply)
    {
        var provider = Substitute.For<IExpandCollapseProvider>();
        provider.ExpandCollapseState.Returns(now);

        Reply(UiAutomationDriver.ExpandedVerified(provider, "More", expand)).Should().Be(reply);
    }

    [AvaloniaFact]
    public void An_expand_whose_state_cannot_be_read_says_so()
    {
        var provider = Substitute.For<IExpandCollapseProvider>();
        provider.ExpandCollapseState.Returns(_ => throw new InvalidOperationException("peer gone"));

        var outcome = UiAutomationDriver.ExpandedVerified(provider, "More", true);

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().StartWith("'More': expand was sent, but its state could not be read back");
    }

    [AvaloniaFact]
    public void A_context_menu_that_would_not_open_or_close_is_refused_as_not_done()
    {
        var menu = new ContextMenu { Items = { new MenuItem { Header = "Cut" } } };
        var target = Shown(new Border { Name = "Surface", Width = 100, Height = 50, ContextMenu = menu });
        // Shut as it opens, as light dismissal shuts a popup over a window without focus; then back as it shuts.
        var dismiss = true;
        var reopen = false;
        menu.Opened += (_, _) => { if (dismiss) menu.Close(); };
        menu.Closed += (_, _) => { if (reopen) { reopen = false; menu.Open(target); } };

        Reply(UiAutomationDriver.Interact(target, "expand", null)).Should()
            .StartWith("the context menu on 'Surface' did not open");

        dismiss = false;
        Reply(UiAutomationDriver.Interact(target, "expand", null)).Should().Be("opened context menu on 'Surface'");
        reopen = true;
        Reply(UiAutomationDriver.Interact(target, "collapse", null)).Should()
            .StartWith("the context menu on 'Surface' did not close");
        menu.Close();
    }

    [AvaloniaFact]
    public void A_popup_whose_state_cannot_be_read_says_so()
    {
        var outcome = UiAutomationDriver.PopupVerified(() => throw new InvalidOperationException("gone"), true, "flyout", "Add");

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().StartWith("the flyout on 'Add' was opened, but whether it is open could not be read back");
    }

    [AvaloniaFact]
    public void A_flyout_opened_and_closed_says_so()
    {
        var button = Shown(new Button { Name = "Add", Content = "Add", Flyout = new MenuFlyout { Items = { new MenuItem { Header = "Form" } } } });

        Reply(UiAutomationDriver.Interact(button, "expand", null)).Should().Be("opened flyout on 'Add'");
        Reply(UiAutomationDriver.Interact(button, "collapse", null)).Should().Be("closed flyout on 'Add'");
    }

    [AvaloniaFact]
    public void A_value_the_control_reformatted_is_refused_with_what_it_holds()
    {
        var box = Shown(new TextBox { Name = "Code" });
        // At once, as the property changes: TextChanged comes a dispatcher pass later, which the read-back does not wait for.
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty && box.Text is { } text && text != text.ToUpperInvariant())
                box.Text = text.ToUpperInvariant();
        };

        var outcome = UiAutomationDriver.Interact(box, "set_value", "abc");

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().StartWith("'Code' did not take 'abc': it holds 'ABC'");
        Reply(UiAutomationDriver.Interact(box, "set_value", "XYZ")).Should().Be("set value of 'Code' to 'XYZ'");
    }

    [AvaloniaFact]
    public void A_value_that_cannot_be_read_back_says_so()
    {
        var provider = Substitute.For<IValueProvider>();
        provider.Value.Returns(_ => throw new InvalidOperationException("peer gone"));

        var outcome = UiAutomationDriver.ValueVerified(provider, "abc", "Code", "");

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().StartWith("'Code': set_value was sent, but its value could not be read back");
    }

    // Read off the UI thread, every property of the control throws, as a read of a control being torn down can.
    [AvaloniaFact]
    public async Task A_control_whose_visibility_cannot_be_read_is_not_acted_on()
    {
        var clicks = 0;
        var button = Shown(new Button { Name = "Approve", Content = "Approve" });
        button.Click += (_, _) => clicks++;

        var outcome = await Task.Run(() => UiAutomationDriver.Interact(button, "invoke", null));

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Contain("is not known to be showing: whether it or a container is hidden could not be read")
            .And.Contain("'invoke' was refused and nothing was done");
        clicks.Should().Be(0);
    }
}
