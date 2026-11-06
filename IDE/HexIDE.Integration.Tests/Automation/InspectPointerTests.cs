using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// inspect_element says which mouse pointer a control shows and where it is set (#2453). Nothing did: a program's
/// MousePointer, Screen.MousePointer over it, or a TextBox's I-beam could not be checked in the running IDE, as a
/// snapshot draws no pointer and Avalonia's Cursor does not say what it shows.
/// </summary>
public class InspectPointerTests
{
    private static Window Show(Control content)
    {
        var window = new Window { Width = 300, Height = 200, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void A_control_with_no_pointer_set_reports_none()
    {
        var button = new Button { Content = "OK" };
        var window = Show(button);
        try
        {
            UiAutomationDriver.Inspect(button, "Window/Button", window).Pointer.Should().BeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_program_s_MousePointer_is_given_by_number_and_name_and_what_it_draws()
    {
        var button = new Button { Content = "OK", Cursor = VBCursorType.Hourglass.ToCursor() };
        var window = Show(button);
        try
        {
            UiAutomationDriver.Inspect(button, "Window/Button", window).Pointer.Should()
                .Be(new PointerState(11, "Hourglass", "Wait", PointerSource.Own, "self", null));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_pointer_set_on_a_container_is_named_where_it_is_set()
    {
        var button = new Button { Content = "OK" };
        var panel = new StackPanel { Cursor = VBCursorType.IBeam.ToCursor(), Children = { button } };
        var window = Show(panel);
        try
        {
            var pointer = UiAutomationDriver.Inspect(button, "Window/Button", window).Pointer!;

            (pointer.MousePointer, pointer.Name, pointer.Shows, pointer.SetBy).Should().Be((3, "I-Beam", "Ibeam", PointerSource.Own));
            pointer.SetOn.Should().NotBe("self").And.NotBeNullOrEmpty("the button inherits it from the panel");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_pointer_set_above_the_control_s_own_is_an_override_as_Screen_MousePointer_sets_it()
    {
        var button = new Button { Content = "OK", Cursor = VBCursorType.Crosshair.ToCursor() };
        var window = Show(button);
        try
        {
            // As #2448's Screen.MousePointer sets it over every control of a form.
            button.SetValue(InputElement.CursorProperty, VBCursorType.Hourglass.ToCursor(), BindingPriority.Animation);
            Dispatcher.UIThread.RunJobs();

            UiAutomationDriver.Inspect(button, "Window/Button", window).Pointer.Should()
                .Be(new PointerState(11, "Hourglass", "Wait", PointerSource.Override, "self", null));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_pointer_a_style_sets_is_said_to_come_from_the_style()
    {
        var button = new Button { Content = "OK" };
        button.Styles.Add(new Style(x => x.OfType<Button>())
        {
            Setters = { new Setter(InputElement.CursorProperty, VBCursorType.Crosshair.ToCursor()) },
        });
        var window = Show(button);
        try
        {
            UiAutomationDriver.Inspect(button, "Window/Button", window).Pointer.Should()
                .Be(new PointerState(2, "Cross", "Cross", PointerSource.Style, "self", null));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_pointer_the_runtime_did_not_make_is_said_to_be_unrecorded_not_guessed()
    {
        var button = new Button { Content = "OK" };
        var window = Show(button);
        window.Styles.Add(new Style(x => x.OfType<Button>())
        {
            Setters = { new Setter(InputElement.CursorProperty, new Cursor(StandardCursorType.Hand)) },
        });
        Dispatcher.UIThread.RunJobs();
        try
        {
            var pointer = UiAutomationDriver.Inspect(button, "Window/Button", window).Pointer!;

            (pointer.MousePointer, pointer.Name, pointer.Shows).Should().Be(((int?)null, (string?)null, (string?)null));
            pointer.SetBy.Should().Be(PointerSource.Style);
            pointer.Note.Should().Contain("whose kind is not recorded");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Each_cursor_the_runtime_makes_is_its_own_and_remembers_its_pointer()
    {
        var first = VBCursorType.Hourglass.ToCursor();
        var second = VBCursorType.Hourglass.ToCursor();

        first.Should().NotBeSameAs(second, "callers compare cursors by reference, so recording must not cache them");
        VBCursorTypeExtensions.PointerOf(second).Should().Be(VBCursorType.Hourglass);
        VBCursorTypeExtensions.PointerOf(new Cursor(StandardCursorType.Wait)).Should().BeNull();
        VBCursorTypeExtensions.PointerOf(null).Should().BeNull();
    }
}
