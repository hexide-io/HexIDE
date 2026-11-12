using System;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class TabStripKeyboardTests
{
    [AvaloniaFact]
    public void Mnemonic_raises_GotFocus_before_selection_and_leaving_raises_LostFocus()
    {
        var window = TabStripForm.Open(focus: true);
        try
        {
            var text = window.GetVisualDescendants().OfType<VBTextBox>().Single();
            text.Focus();
            Dispatcher.UIThread.RunJobs();
            text.Text = "";
            UiAutomationDriver.PressKey(text, "D", "Alt").Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            text.Text.Should().Be("GotBefore1Click4");
            text.Focus();
            Dispatcher.UIThread.RunJobs();
            text.Text.Should().Be("GotBefore1Click4Lost");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Right", "Before1Click2")]
    [InlineData("End", "Before1Click4")]
    public void Navigation_runs_BeforeClick_with_old_selection_then_Click(string key, string expected)
    {
        var window = TabStripForm.Open();
        try
        {
            var strip = window.GetVisualDescendants().OfType<VBTabStrip>().Single();
            UiAutomationDriver.PressKey(strip, key, null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            window.GetVisualDescendants().OfType<VBTextBox>().Single().Text.Should().Be(expected);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Left", "Before1Click4Before4Click3")]
    [InlineData("Home", "Before1Click4Before4Click1")]
    public void Navigation_back_from_the_last_tab(string key, string expected)
    {
        var window = TabStripForm.Open();
        try
        {
            var strip = window.GetVisualDescendants().OfType<VBTabStrip>().Single();
            UiAutomationDriver.PressKey(strip, "End", null).Success.Should().BeTrue();
            UiAutomationDriver.PressKey(strip, key, null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            window.GetVisualDescendants().OfType<VBTextBox>().Single().Text.Should().Be(expected);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Cancelled_arrow_keeps_selection_and_does_not_Click()
    {
        var window = TabStripForm.Open(cancel: true);
        try
        {
            var strip = window.GetVisualDescendants().OfType<VBTabStrip>().Single();
            UiAutomationDriver.PressKey(strip, "Right", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            strip.Model.SelectedItem!.Index.Should().Be(1);
            window.GetVisualDescendants().OfType<VBTextBox>().Single().Text.Should().Be("Before1");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Mnemonic_selects_the_tab_and_takes_focus_from_another_control()
    {
        var window = TabStripForm.Open();
        try
        {
            var text = window.GetVisualDescendants().OfType<VBTextBox>().Single();
            UiAutomationDriver.PressKey(text, "D", "Alt").Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            var strip = window.GetVisualDescendants().OfType<VBTabStrip>().Single();
            strip.IsFocused.Should().BeTrue();
            strip.Model.SelectedItem!.Index.Should().Be(4);
            text.Text.Should().Be("Before1Click4");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("D", "Alt")]
    [InlineData("Home", null)]
    [InlineData("End", null)]
    public void Cancelled_non_arrow_selection_refuses_the_unmeasured_outcome(string key, string? modifiers)
    {
        var window = TabStripForm.Open(cancel: true);
        Exception? fault = null;
        void Faulted(VBWindowContext context, Exception error)
        {
            if (ReferenceEquals(context, ((IFormView)window).Context))
                fault = error;
        }
        VBWindowContext.Faulted += Faulted;
        try
        {
            var text = window.GetVisualDescendants().OfType<VBTextBox>().Single();
            var strip = window.GetVisualDescendants().OfType<VBTabStrip>().Single();
            if (key == "Home")
                strip.Model.Select(strip.Model.Tabs[3]);
            UiAutomationDriver.PressKey(modifiers is null ? strip : text, key, modifiers).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            fault.Should().BeOfType<NotImplementedException>().Which.Message.Should().Contain("cancelled keyboard");
        }
        finally
        {
            VBWindowContext.Faulted -= Faulted;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Down_does_not_change_selection_or_raise_selection_events()
    {
        var window = TabStripForm.Open();
        try
        {
            var strip = window.GetVisualDescendants().OfType<VBTabStrip>().Single();
            UiAutomationDriver.PressKey(strip, "Down", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            strip.Model.SelectedItem!.Index.Should().Be(1);
            window.GetVisualDescendants().OfType<VBTextBox>().Single().Text.Should().BeEmpty();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TabStrip_is_excluded_from_designer_container_classes()
    {
        ContainerClasses.IsContainer(TabStripComponentClass.Instance).Should().BeFalse();
    }
}
