using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class TabStripMouseTests
{
    [AvaloniaTheory]
    [InlineData(false, false, "Down1Before1Up2(1,0,1050,150)Click2")]
    [InlineData(true, false, "Down1Before1Up1(1,0,1050,150)Click1")]
    [InlineData(false, true, "Down1Up1(2,0,1050,150)Click1")]
    public void MouseUp_sees_selection_and_cancel_still_Clicks(bool cancel, bool right, string expected)
    {
        var window = TabStripKeyboardTests.Open(cancel, mouse: true);
        try
        {
            Click(window, new Point(70, 10), right ? MouseButton.Right : MouseButton.Left);
            Text(window).Should().Be(expected);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Selected_tab_click_does_not_raise_BeforeClick()
    {
        var window = TabStripKeyboardTests.Open(mouse: true);
        try
        {
            Click(window, new Point(10, 10));
            Text(window).Should().Be("Down1Up1(1,0,150,150)Click1");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Client_area_raises_no_control_or_form_events()
    {
        var window = TabStripKeyboardTests.Open(mouse: true);
        try
        {
            Click(window, new Point(70, 70));
            Text(window).Should().BeEmpty();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, "Down1Up2(1,0,1050,150)Click2")]
    [InlineData(true, "Down1Up1(1,2,1050,150)Click1")]
    public void MultiSelect_clicks_skip_BeforeClick(bool control, string expected)
    {
        var window = TabStripKeyboardTests.Open(mouse: true, multi: true);
        try
        {
            Click(window, new Point(70, 10), modifiers: control ? RawInputModifiers.Control : RawInputModifiers.None);
            Text(window).Should().Be(expected);
            var strip = window.GetVisualDescendants().OfType<VBTabStrip>().Single();
            strip.Model.Tabs[1].Selected.Should().BeTrue();
            strip.Model.Tabs[0].Selected.Should().Be(control);
            Click(window, new Point(110, 10));
            Text(window).Should().Be(expected + (control ? "Down1Up3(1,0,1650,150)Click3" : "Down2Up3(1,0,1650,150)Click3"));
            strip.Model.Tabs.Select(tab => tab.Selected).Should().Equal(false, false, true, false);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Double_click_has_a_second_MouseUp_and_Click_without_MouseDown()
    {
        var window = TabStripKeyboardTests.Open(mouse: true);
        var platform = window.PlatformImpl!;
        var inputProperty = platform.GetType().GetProperty("Input")!;
        var input = (System.Action<RawInputEventArgs>)inputProperty.GetValue(platform)!;
        var timestampProperty = typeof(RawInputEventArgs).GetProperty("Timestamp")!;
        ulong timestamp = 100;
        inputProperty.SetValue(platform, (System.Action<RawInputEventArgs>)(e =>
        {
            if (e is RawPointerEventArgs)
                timestampProperty.SetValue(e, timestamp);
            input(e);
        }));
        try
        {
            Click(window, new Point(70, 10));
            timestamp += 100;
            Click(window, new Point(70, 10));
            Text(window).Should().Be("Down1Before1Up2(1,0,1050,150)Click2Up2(1,0,1050,150)Click2");
        }
        finally
        {
            inputProperty.SetValue(platform, input);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("Placement")]
    [InlineData("MultiRow")]
    [InlineData("TabWidthStyle")]
    [InlineData("TabFixedWidth")]
    [InlineData("TabFixedHeight")]
    public void Header_layouts_reserved_for_drawing_refuse_mouse_input(string property)
    {
        var window = TabStripKeyboardTests.Open(mouse: true);
        Exception? fault = null;
        void Faulted(VBWindowContext context, Exception error)
        {
            if (ReferenceEquals(context, ((IFormView)window).Context))
                fault = error;
        }
        VBWindowContext.Faulted += Faulted;
        try
        {
            var strip = window.GetVisualDescendants().OfType<VBTabStrip>().Single();
            strip.Values[property] = property == "MultiRow" ? new Vb6Value(true)
                : property.StartsWith("TabFixed", StringComparison.Ordinal) ? new Vb6Value(100) : new Vb6Value(1L);
            Click(window, new Point(70, 10));
            fault.Should().BeOfType<NotImplementedException>().Which.Message.Should().Contain("mouse header layout");
        }
        finally
        {
            VBWindowContext.Faulted -= Faulted;
            window.Close();
        }
    }

    private static string? Text(Window window) => window.GetVisualDescendants().OfType<VBTextBox>().Single().Text;

    private static void Click(Window window, Point local, MouseButton button = MouseButton.Left,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var strip = window.GetVisualDescendants().OfType<VBTabStrip>().Single();
        var point = strip.TranslatePoint(local, window)!.Value;
        window.MouseDown(point, button, modifiers);
        window.MouseUp(point, button, modifiers);
        Dispatcher.UIThread.RunJobs();
    }
}
