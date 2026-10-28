using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.Automation;
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
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// MouseDown, MouseUp and MouseMove on a running form (#974), driven with real pointer input. Every order and argument
/// pinned here was measured on vb6.exe with synthetic mouse messages, native and p-code agreeing (the oracle's "Mouse
/// events"). Where VB6 reads something from the keyboard or the mouse itself rather than from the message, the test says
/// so.
/// </summary>
public class MouseEventTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Twips = "   ScaleHeight     =   3090\r\n   ScaleWidth      =   4680\r\n";
    private const string Pixels = "   ScaleHeight     =   206\r\n   ScaleMode       =   3  'Pixel\r\n   ScaleWidth      =   312\r\n";

    private static string Ctl(string kind, string name, int left, int top, int width, int height, string extra = "", string children = "") =>
        $"   Begin VB.{kind} {name} \r\n{extra}      Height          =   {height}\r\n      Left            =   {left}\r\n"
        + $"      Top             =   {top}\r\n      Width           =   {width}\r\n{children}   End\r\n";

    private static readonly string Controls =
        Ctl("CommandButton", "Command1", 240, 240, 1215, 495, "      Caption         =   \"c1\"\r\n")
        + Ctl("Label", "Label1", 240, 1200, 1200, 255, "      Caption         =   \"l1\"\r\n")
        + Ctl("TextBox", "Text1", 2400, 240, 1215, 495, "      Text            =   \"t1\"\r\n")
        + Ctl("CheckBox", "Check1", 2400, 1200, 1215, 255, "      Caption         =   \"k1\"\r\n")
        + Ctl("Label", "lblCell", 240, 1800, 600, 255, "      Caption         =   \"a0\"\r\n      Index           =   0\r\n");

    private static string Form(string scale, string controls) =>
        "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\n   ClientHeight    =   3090\r\n"
        + "   ClientWidth     =   4680\r\n" + scale + controls + "End\r\nAttribute VB_Name = \"Form1\"\r\n";

    private static string Handlers(string name, bool index = false, bool click = true, bool dblClick = false)
    {
        var head = index ? "Index As Integer, " : "";
        var pre = index ? "\"(\" & Index & \") \" & " : "";
        var code = "";
        foreach (var ev in new[] { "MouseDown", "MouseUp", "MouseMove" })
            code += $"Private Sub {name}_{ev}({head}Button As Integer, Shift As Integer, X As Single, Y As Single)\r\n"
                  + $"    Debug.Print \"{name} {ev} \" & {pre}Button & \" \" & Shift & \" \" & X & \" \" & Y\r\nEnd Sub\r\n";
        if (click)
            code += $"Private Sub {name}_Click({(index ? "Index As Integer" : "")})\r\n    Debug.Print \"{name} Click\"\r\nEnd Sub\r\n";
        if (dblClick)
            code += $"Private Sub {name}_DblClick({(index ? "Index As Integer" : "")})\r\n"
                  + $"    Debug.Print \"{name} DblClick\"{(index ? " & \" (\" & Index & \")\"" : "")}\r\nEnd Sub\r\n";
        return code;
    }

    /// <summary>A running form, kept open so pointer input can be sent to it; disposing closes it.</summary>
    private sealed class Running : System.IDisposable
    {
        public required VBFormRuntime Window { get; init; }
        public List<string> Printed { get; } = [];
        public List<System.Exception> Errors { get; } = [];
        public System.Action? Detach { get; set; }

        public Control Named(string name) =>
            Window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => VBProps.GetName(c) == name)
            ?? throw new System.InvalidOperationException(
                $"No {name}; named: {string.Join(", ", Window.GetVisualDescendants().OfType<Control>().Select(VBProps.GetName).Where(n => n != null))}");

        public Point At(string name, double x, double y) => Named(name).TranslatePoint(new Point(x, y), Window)!.Value;

        public void Click(Point point, MouseButton button = MouseButton.Left, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.MouseDown(point, button, modifiers);
            Window.MouseUp(point, button, modifiers);
            Dispatcher.UIThread.RunJobs();
        }

        // Headless MouseDown/MouseUp render and drain the dispatcher around every event. Their
        // stopwatch timestamps therefore include JIT, rendering and GC time: two consecutive Click
        // calls can exceed Avalonia's 500 ms double-click interval. Supply gesture time at the raw
        // input boundary, leaving hit testing, capture and Avalonia's click counting intact.
        public void ClickSequence(Point point, int count = 2, ulong intervalMs = 100)
        {
            // Avalonia 12 hides Input and raw Timestamp from its reference assembly. The
            // implementation exposes them; restore the callback even if a click throws.
            var platform = Window.PlatformImpl!;
            var inputProperty = platform.GetType().GetProperty("Input")!;
            var input = (System.Action<RawInputEventArgs>)inputProperty.GetValue(platform)!;
            var timestampProperty = typeof(RawInputEventArgs).GetProperty("Timestamp")!;
            ulong timestamp = 0;
            inputProperty.SetValue(platform, (System.Action<RawInputEventArgs>)(e =>
            {
                if (e is RawPointerEventArgs)
                    timestampProperty.SetValue(e, timestamp);
                input(e);
            }));
            try
            {
                for (var i = 0; i < count; i++)
                {
                    timestamp += intervalMs;
                    Click(point);
                }
            }
            finally { inputProperty.SetValue(platform, input); }
        }

        public void Dispose()
        {
            Window.Close();
            Detach?.Invoke();
        }
    }

    private static Running Start(string frm, string code, HexIDE.Runtime.Debugging.IDebugController? debugger = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, frm + code, new NullSink())!;
        project.AddForm(form);
        VBLoader.RunForm(form, CancellationToken.None, out var window, debugger);
        var run = new Running { Window = window };
        void OnFaulted(VBWindowContext _, System.Exception e) => run.Errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => run.Errors.Add(e);
        void OnCompileError(VBWindowContext _, VBCompileErrorException e) => run.Errors.Add(e);
        VBDebugConsole.Output += run.Printed.Add;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        VBWindowContext.CompileError += OnCompileError;
        run.Detach = () =>
        {
            VBDebugConsole.Output -= run.Printed.Add;
            VBWindowContext.Faulted -= OnFaulted;
            VBWindowContext.RunTimeError -= OnRunTimeError;
            VBWindowContext.CompileError -= OnCompileError;
        };
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return run;
    }

    // Under the IDE's debugger, which gives up the thread at its time slice: 15 ms is the IDE's own, -1 yields at every
    // statement. Before #766 a handler that yielded let the next event start first, so a CommandButton printed MouseDown,
    // MouseUp, Click; MouseEventTests' other cases run with no debugger, where nothing yields.
    private static HexIDE.Runtime.Debugging.DebugController Debugger(long timeSliceMs)
    {
        var debugger = new HexIDE.Runtime.Debugging.DebugController { TimeSliceMs = timeSliceMs };
        debugger.Reset();
        return debugger;
    }

    [AvaloniaTheory]
    [InlineData(15L)]
    [InlineData(-1L)]
    public void Under_the_debuggers_time_slice_a_human_paced_click_keeps_VB6s_order(long timeSliceMs)
    {
        // A press, a pause longer than the slice, then the release: Click and MouseUp come from that one release, and the
        // first of them yields. VB6's order holds only because MouseUp now waits for Click.
        using var run = Start(Form(Twips, Controls), Handlers("Command1"), Debugger(timeSliceMs));
        var point = run.At("Command1", 10, 5);

        Thread.Sleep(40);
        run.Window.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(40);
        run.Window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Command1 MouseDown 1 0 150 75", "Command1 Click", "Command1 MouseUp 1 0 150 75");
    }

    [AvaloniaFact]
    public void Under_a_slice_at_every_statement_a_TextBox_keeps_MouseDown_MouseUp_then_Click()
    {
        using var run = Start(Form(Twips, Controls), Handlers("Text1"), Debugger(-1));

        run.Click(run.At("Text1", 10, 5));
        Dispatcher.UIThread.RunJobs();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Text1 MouseDown 1 0 120 45", "Text1 MouseUp 1 0 120 45", "Text1 Click");
    }

    // A root that records how each event was raised, rather than running it.
    private sealed class RecordingRoot : Canvas, IModuleExecutionRoot
    {
        public List<(string Name, EventCoalescing Coalescing)> Raised { get; } = new();

        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) => Raised.Add((name, EventCoalescing.None));

        public void ExecuteEvent(string name, System.Func<IReadOnlyList<Vb6Value>> args,
            EventCoalescing coalescing = EventCoalescing.None) => Raised.Add((name, coalescing));
    }

    [AvaloniaFact]
    public void A_move_is_raised_to_be_coalesced_and_a_press_is_not()
    {
        // Which moves wait is EventPumpTests': a move updates only a move at the back of the queue. Real headless input
        // drains the dispatcher after each event, so it cannot hold a handler open long enough to show two moves waiting.
        // What it can show is that a control raises its moves that way, and its presses not.
        var button = new VBCommandButton { Width = 80, Height = 30 };
        VBProps.SetName(button, "Command1");
        Canvas.SetLeft(button, 16);
        Canvas.SetTop(button, 16);
        var root = new RecordingRoot { Width = 300, Height = 200 };
        root.Children.Add(button);
        var window = new Window { Content = root, Width = 300, Height = 200 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var point = button.TranslatePoint(new Point(10, 5), window)!.Value;

            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);

            root.Raised.Should().Contain(("Command1_MouseMove", EventCoalescing.Latest))
                .And.Contain(("Command1_MouseDown", EventCoalescing.None))
                .And.Contain(("Command1_MouseUp", EventCoalescing.None));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_CommandButton_raises_MouseDown_Click_then_MouseUp_in_twips_from_its_corner()
    {
        // Measured: Click comes before MouseUp on a CommandButton, and (10, 5) pixels in is 150, 75.
        using var run = Start(Form(Twips, Controls), Handlers("Command1"));

        run.Click(run.At("Command1", 10, 5));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Command1 MouseDown 1 0 150 75", "Command1 Click", "Command1 MouseUp 1 0 150 75");
    }

    [AvaloniaTheory]
    [InlineData(MouseButton.Right, 2)]
    [InlineData(MouseButton.Middle, 4)]
    public void The_right_and_middle_buttons_raise_no_Click(MouseButton button, int vbButton)
    {
        // Measured: Button 2 and 4, and no Click.
        using var run = Start(Form(Twips, Controls), Handlers("Command1"));

        run.Click(run.At("Command1", 10, 5), button);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal($"Command1 MouseDown {vbButton} 0 150 75", $"Command1 MouseUp {vbButton} 0 150 75");
    }

    [AvaloniaFact]
    public void A_CheckBox_raises_Click_before_MouseUp_and_toggles()
    {
        // Measured: MouseDown, Click, MouseUp, and the Value is then 1.
        using var run = Start(Form(Twips, Controls), Handlers("Check1"));

        run.Click(run.At("Check1", 10, 5));

        run.Errors.Should().BeEmpty();
        ((VBCheckBox)run.Named("Check1")).Value.Should().Be(VBCheckValue.Checked);
    }

    [AvaloniaFact]
    public void A_CheckBox_raises_MouseDown_Click_then_MouseUp()
    {
        using var run = Start(Form(Twips, Controls), Handlers("Check1"));

        run.Click(run.At("Check1", 10, 5));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Check1 MouseDown 1 0 150 75", "Check1 Click", "Check1 MouseUp 1 0 150 75");
    }

    [AvaloniaFact]
    public void A_TextBox_raises_MouseUp_before_Click_and_counts_from_inside_its_border()
    {
        // Measured: MouseDown, MouseUp, Click. Its client area starts 2 pixels in (GetClientRect), so (10, 5) inside it
        // is 150, 75.
        using var run = Start(Form(Twips, Controls), Handlers("Text1"));

        run.Click(run.At("Text1", 12, 7));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Text1 MouseDown 1 0 150 75", "Text1 MouseUp 1 0 150 75", "Text1 Click");
    }

    [AvaloniaFact]
    public void A_Label_and_a_control_array_element_take_the_mouse_and_the_Index_comes_first()
    {
        // Measured: (5, 3) into each is 75, 45, and lblCell's Index leads the arguments. VB6 raised no Click from the
        // synthetic messages a Label was measured with, so no Click is pinned here.
        using var run = Start(Form(Twips, Controls), Handlers("Label1", click: false) + Handlers("lblCell", index: true, click: false));

        run.Click(run.At("Label1", 5, 3));
        run.Click(run.At("lblCell", 5, 3));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Label1 MouseDown 1 0 75 45", "Label1 MouseUp 1 0 75 45",
            "lblCell MouseDown (0) 1 0 75 45", "lblCell MouseUp (0) 1 0 75 45");
    }

    [AvaloniaTheory]
    [InlineData(Twips, "4500 150")]
    [InlineData(Pixels, "300 10")]
    public void The_forms_own_events_are_in_its_ScaleMode(string scale, string expected)
    {
        // Measured: (300, 10) on the form's client area is 4500, 150 in twips and 300, 10 on a pixel form.
        using var run = Start(Form(scale, Controls), Handlers("Form", click: false));

        run.Click(new Point(300, 10));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal($"Form MouseDown 1 0 {expected}", $"Form MouseUp 1 0 {expected}");
    }

    // A form's own Click, measured with real input (the oracle's "A click with real input", #1364). Form1's client area is
    // 312 by 206 pixels, and (300, 10) and (250, 150) are over no control.

    [AvaloniaTheory]
    [InlineData(MouseButton.Left, 1)]
    [InlineData(MouseButton.Right, 2)]
    [InlineData(MouseButton.Middle, 4)]
    public void The_form_raises_Click_after_MouseUp_for_every_button(MouseButton button, int vbButton)
    {
        // Measured: MouseDown, MouseUp, Click, for the right and middle buttons too, unlike a CommandButton.
        using var run = Start(Form(Twips, Controls), Handlers("Form"));

        run.Click(new Point(300, 10), button);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal($"Form MouseDown {vbButton} 0 4500 150", $"Form MouseUp {vbButton} 0 4500 150", "Form Click");
    }

    [AvaloniaFact]
    public void A_press_and_release_in_different_places_on_the_form_is_still_a_Click()
    {
        // Measured: a 50-pixel move across the form between the press and the release changes nothing.
        using var run = Start(Form(Twips, Controls), Handlers("Form"));

        run.Window.MouseDown(new Point(300, 10), MouseButton.Left);
        run.Window.MouseUp(new Point(250, 150), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Form MouseDown 1 0 4500 150", "Form MouseUp 1 0 3750 2250", "Form Click");
    }

    [AvaloniaFact]
    public void A_release_over_a_control_gives_the_form_its_MouseUp_and_no_Click()
    {
        // Measured: pressed on the form and released over the windowless Label1, the form has MouseUp in its own
        // coordinates, and there is no Click on the form or the Label.
        using var run = Start(Form(Twips, Controls), Handlers("Form") + Handlers("Label1"));

        run.Window.MouseDown(new Point(300, 10), MouseButton.Left);
        run.Window.MouseUp(run.At("Label1", 5, 3), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Form MouseDown 1 0 4500 150", "Form MouseUp 1 0 315 1245");
    }

    [AvaloniaFact]
    public void A_release_outside_the_form_gives_it_its_MouseUp_and_no_Click()
    {
        // Measured: released outside the window, the form has MouseUp past its client area, and no Click.
        using var run = Start(Form(Twips, Controls), Handlers("Form"));

        run.Window.MouseDown(new Point(300, 10), MouseButton.Left);
        run.Window.MouseUp(new Point(330, 10), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Form MouseDown 1 0 4500 150", "Form MouseUp 1 0 4950 150");
    }

    // Where a press's release lands (#1373), measured with real input, native and p-code agreeing (the oracle's "A click
    // with real input"): a Label, an Image, a Frame and a PictureBox raise Click only for a release over them again, and a
    // TextBox for any release. Each control's MouseUp comes wherever the release lands.

    private static readonly string Static =
        Ctl("Label", "Label1", 240, 240, 1200, 255, "      Caption         =   \"l1\"\r\n")
        + Ctl("Image", "Image1", 240, 900, 1200, 600)
        + Ctl("Frame", "Frame1", 2400, 240, 1500, 900, "      Caption         =   \"f1\"\r\n")
        + Ctl("PictureBox", "Picture1", 2400, 1500, 1500, 600)
        + Ctl("TextBox", "Text1", 240, 2100, 1200, 315, "      Text            =   \"t1\"\r\n");

    // The events a run printed, without MouseMove and without their arguments: "Label1 MouseDown".
    private static IEnumerable<string> Events(Running run) =>
        run.Printed.Select(p => string.Join(' ', p.Split(' ').Take(2))).Where(p => !p.EndsWith("MouseMove", System.StringComparison.Ordinal));

    private static void PressAndRelease(Running run, Point press, Point release, Point? via = null)
    {
        run.Window.MouseDown(press, MouseButton.Left);
        if (via is { } middle)
            run.Window.MouseMove(middle, RawInputModifiers.LeftMouseButton);
        run.Window.MouseMove(release, RawInputModifiers.LeftMouseButton);
        run.Window.MouseUp(release, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData("Label1")]
    [InlineData("Image1")]
    [InlineData("Frame1")]
    [InlineData("Picture1")]
    public void A_press_on_a_Label_Image_Frame_or_PictureBox_released_on_the_form_raises_no_Click(string name)
    {
        using var run = Start(Form(Twips, Static), Handlers(name));

        PressAndRelease(run, run.At(name, 10, 10), new Point(300, 190));

        run.Errors.Should().BeEmpty();
        Events(run).Should().Equal($"{name} MouseDown", $"{name} MouseUp");
    }

    [AvaloniaFact]
    public void A_press_on_a_TextBox_released_on_the_form_still_raises_Click()
    {
        using var run = Start(Form(Twips, Static), Handlers("Text1"));

        PressAndRelease(run, run.At("Text1", 10, 5), new Point(300, 190));

        run.Errors.Should().BeEmpty();
        Events(run).Should().Equal("Text1 MouseDown", "Text1 MouseUp", "Text1 Click");
    }

    [AvaloniaFact]
    public void A_press_on_a_Label_moved_off_it_and_back_raises_Click()
    {
        using var run = Start(Form(Twips, Static), Handlers("Label1"));

        PressAndRelease(run, run.At("Label1", 10, 10), run.At("Label1", 20, 10), via: new Point(300, 190));

        run.Errors.Should().BeEmpty();
        Events(run).Should().Equal("Label1 MouseDown", "Label1 MouseUp", "Label1 Click");
    }

    [AvaloniaFact]
    public void A_press_on_a_Label_released_on_a_TextBox_raises_no_Click_and_nothing_on_the_TextBox()
    {
        using var run = Start(Form(Twips, Static), Handlers("Label1") + Handlers("Text1"));

        PressAndRelease(run, run.At("Label1", 10, 10), run.At("Text1", 10, 5));

        run.Errors.Should().BeEmpty();
        Events(run).Should().Equal("Label1 MouseDown", "Label1 MouseUp");
    }

    [AvaloniaFact]
    public void A_press_on_a_control_released_on_the_form_is_nothing_to_the_form()
    {
        // Measured: pressed on Command1 and released on the form, the form has no event at all.
        using var run = Start(Form(Twips, Controls), Handlers("Form"));

        run.Window.MouseDown(run.At("Command1", 10, 5), MouseButton.Left);
        run.Window.MouseUp(new Point(300, 10), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void A_double_click_on_the_form_raises_DblClick_in_place_of_the_second_MouseDown_and_a_third_click_is_new()
    {
        // Measured (#1364, #1031): the second press raises DblClick and no MouseDown, its release no Click, and a third
        // press starts a new click.
        using var run = Start(Form(Twips, Controls), Handlers("Form", dblClick: true));

        run.ClickSequence(new Point(300, 10), count: 3);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal(
            "Form MouseDown 1 0 4500 150", "Form MouseUp 1 0 4500 150", "Form Click",
            "Form DblClick", "Form MouseUp 1 0 4500 150",
            "Form MouseDown 1 0 4500 150", "Form MouseUp 1 0 4500 150", "Form Click");
    }

    private static async Task<InteractOutcome> AutomationClick(Running run, string value)
    {
        // As the tool does: the target must have drawn a frame, and the reply waits for the program's work.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        var watch = UiAutomationDriver.WatchProgram(run.Window);
        var outcome = await UiAutomationDriver.FinishAsync(UiAutomationDriver.Interact(run.Window, "click", value));
        return await UiAutomationDriver.WaitForProgramAsync(watch, outcome);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Automation_drag_on_a_Label_clicks_only_when_released_over_it(bool over)
    {
        // #1373: A_press_on_a_Label... and A_press_on_a_Label_moved_off_it_and_back above.
        using var run = Start(Form(Twips, Static), Handlers("Label1"));
        var end = over ? run.At("Label1", 20, 10) : new Point(300, 190);
        var start = run.At("Label1", 10, 10);
        var outcome = await AutomationClick(run, System.FormattableString.Invariant($"{start.X},{start.Y} to {end.X},{end.Y}"));
        outcome.Success.Should().BeTrue(outcome.Error);
        run.Errors.Should().BeEmpty();
        Events(run).Should().Equal(over
            ? new[] { "Label1 MouseDown", "Label1 MouseUp", "Label1 Click" }
            : new[] { "Label1 MouseDown", "Label1 MouseUp" });
    }

    [AvaloniaFact]
    public async Task Automation_drag_selects_the_ListBox_release_row()
    {
        // #1374: A_ListBox_s_selection_follows_a_press_moved_across_the_items... below.
        using var run = Start(Form(Twips, AList), ListHandlers);
        var start = Item(run, 0);
        var end = Item(run, 2);
        var outcome = await AutomationClick(run, System.FormattableString.Invariant($"{start.X},{start.Y} to {end.X},{end.Y}"));
        outcome.Success.Should().BeTrue(outcome.Error);
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("MouseDown 1 0", "Click 2", "MouseUp 1 2");
        ((ListBox)run.Named("List1")).SelectedIndex.Should().Be(2);
    }

    [AvaloniaTheory]
    [InlineData(15L)]
    [InlineData(-1L)]
    public async Task Automation_click_double_runs_the_forms_DblClick_once_in_the_measured_order(long timeSliceMs)
    {
        // Same vb6.exe order as A_double_click_on_the_form_raises_DblClick... above (#1031). Those probes logged no
        // MouseMove, so they say nothing of one (#2412). The pointer's arrival raises one first, as a person's does (#2407), and
        // the second press, where the first left it, raises none.
        using var run = Start(Form(Twips, Controls), Handlers("Form", dblClick: true), Debugger(timeSliceMs));
        var outcome = await AutomationClick(run, "300,10 double");
        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Contain("double-clicked").And.Contain("left button").And.Contain("(300, 10)");
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Form MouseMove 0 0 4500 150", "Form MouseDown 1 0 4500 150", "Form MouseUp 1 0 4500 150",
            "Form Click", "Form DblClick", "Form MouseUp 1 0 4500 150");
    }

    [AvaloniaFact]
    public async Task Automation_click_right_double_runs_the_forms_DblClick_in_the_measured_order()
    {
        // Oracle: A click with real input, form right double click: the left order with Button 2. Its probe logged no
        // MouseMove; the pointer's arrival raises one first (#2407).
        using var run = Start(Form(Twips, Controls), Handlers("Form", dblClick: true));
        var outcome = await AutomationClick(run, "300,10 right double");
        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Contain("double-clicked").And.Contain("right button");
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Form MouseMove 0 0 4500 150", "Form MouseDown 2 0 4500 150", "Form MouseUp 2 0 4500 150",
            "Form Click", "Form DblClick", "Form MouseUp 2 0 4500 150");
    }

    [AvaloniaTheory]
    [InlineData("Unload Me")]
    [InlineData("Me.Hide")]
    public async Task Automation_click_double_stops_when_the_first_Click_removes_the_form(string action)
    {
        using var run = Start(Form(Twips, ""), Handlers("Form", click: false, dblClick: true)
            + $"Private Sub Form_Click()\r\n    Debug.Print \"first click\"\r\n    {action}\r\nEnd Sub\r\n", Debugger(-1));
        var outcome = await AutomationClick(run, "double");
        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Contain("second click").And.Contain("hidden or closed");
        run.Errors.Should().BeEmpty();
        // The pointer's arrival, MouseDown, MouseUp, then the first Click, which removes the form (#2407).
        run.Printed.Should().HaveCount(4).And.NotContain("Form DblClick");
        run.Printed[0].Should().StartWith("Form MouseMove 0 0 ");
        run.Printed[3].Should().Be("first click");
    }

    [AvaloniaFact]
    public void Automation_double_click_explains_that_it_did_not_press()
    {
        using var run = Start(Form(Twips, ""), Handlers("Form", dblClick: true));
        var outcome = UiAutomationDriver.Interact(run.Window, "double_click", null);
        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Contain("DoubleTapped").And.Contain("no pointer press").And.Contain("'click'")
            .And.Contain("'double'").And.Contain("DblClick");
        run.Printed.Should().BeEmpty();
    }

    // DblClick on the controls, measured with real input, native and p-code agreeing (the oracle's "A click with real
    // input", #1031).

    [AvaloniaFact]
    public void A_double_click_on_a_Label_raises_DblClick_in_place_of_the_second_MouseDown()
    {
        using var run = Start(Form(Twips, Controls), Handlers("Label1", dblClick: true));
        var at = run.At("Label1", 5, 3);

        run.ClickSequence(at);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Label1 MouseDown 1 0 75 45", "Label1 MouseUp 1 0 75 45", "Label1 Click",
            "Label1 DblClick", "Label1 MouseUp 1 0 75 45");
    }

    [AvaloniaFact]
    public void A_double_click_on_a_TextBox_raises_DblClick_in_place_of_the_second_MouseDown()
    {
        using var run = Start(Form(Twips, Controls), Handlers("Text1", dblClick: true));
        var at = run.At("Text1", 10, 5);

        run.ClickSequence(at);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Text1 MouseDown 1 0 120 45", "Text1 MouseUp 1 0 120 45", "Text1 Click",
            "Text1 DblClick", "Text1 MouseUp 1 0 120 45");
    }

    [AvaloniaFact]
    public void A_double_click_on_a_Frame_raises_DblClick_in_place_of_the_second_MouseDown()
    {
        using var run = Start(Form(Twips, Ctl("Frame", "Frame1", 240, 1500, 2400, 1200, "      Caption         =   \"f1\"\r\n")),
            Handlers("Frame1", dblClick: true));
        var at = run.At("Frame1", 140, 60);

        run.ClickSequence(at);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Frame1 MouseDown 1 0 2100 900", "Frame1 MouseUp 1 0 2100 900", "Frame1 Click",
            "Frame1 DblClick", "Frame1 MouseUp 1 0 2100 900");
    }

    [AvaloniaFact]
    public void An_OptionButton_s_second_press_raises_MouseDown_then_DblClick()
    {
        // Measured: unlike every other control with a DblClick, the second press raises MouseDown too. Click comes only as
        // the option becomes selected, so the second click raises none.
        using var run = Start(Form(Twips, Ctl("OptionButton", "Option1", 240, 240, 1215, 255, "      Caption         =   \"o1\"\r\n")),
            Handlers("Option1", dblClick: true));
        var at = run.At("Option1", 10, 5);

        run.ClickSequence(at);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Option1 MouseDown 1 0 150 75", "Option1 Click", "Option1 MouseUp 1 0 150 75",
            "Option1 MouseDown 1 0 150 75", "Option1 DblClick", "Option1 MouseUp 1 0 150 75");
    }

    [AvaloniaFact]
    public void OptionButton_presses_outside_the_double_click_interval_raise_no_DblClick()
    {
        using var run = Start(Form(Twips, Ctl("OptionButton", "Option1", 240, 240, 1215, 255, "      Caption         =   \"o1\"\r\n")),
            Handlers("Option1", dblClick: true));

        run.ClickSequence(run.At("Option1", 10, 5), intervalMs: 600);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Option1 MouseDown 1 0 150 75", "Option1 Click", "Option1 MouseUp 1 0 150 75",
            "Option1 MouseDown 1 0 150 75", "Option1 MouseUp 1 0 150 75");
    }

    [AvaloniaFact]
    public void A_double_click_on_a_CommandButton_is_two_clicks()
    {
        // Measured: a CommandButton has no DblClick, and a double click on it is two whole clicks.
        using var run = Start(Form(Twips, Controls), Handlers("Command1"));
        var at = run.At("Command1", 10, 5);

        run.ClickSequence(at);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Command1 MouseDown 1 0 150 75", "Command1 Click", "Command1 MouseUp 1 0 150 75",
            "Command1 MouseDown 1 0 150 75", "Command1 Click", "Command1 MouseUp 1 0 150 75");
    }

    [AvaloniaFact]
    public void A_control_array_element_s_DblClick_carries_its_Index()
    {
        using var run = Start(Form(Twips, Controls), Handlers("lblCell", index: true, click: false, dblClick: true));
        var at = run.At("lblCell", 5, 3);

        run.ClickSequence(at);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("lblCell MouseDown (0) 1 0 75 45", "lblCell MouseUp (0) 1 0 75 45",
            "lblCell DblClick (0)", "lblCell MouseUp (0) 1 0 75 45");
    }

    // A ListBox's Click (#1374), measured with real input, native and p-code agreeing (the oracle's "A ListBox's Click"):
    // a left press on an item raises Click at its release, before MouseUp, and the selection follows the press. Each
    // handler prints ListIndex, so where the selection moves is pinned with the order.

    private static readonly string AList = Ctl("ListBox", "List1", 120, 120, 2400, 1800);

    private static readonly string ListHandlers =
        "Private Sub Form_Load()\r\n    List1.AddItem \"a\"\r\n    List1.AddItem \"b\"\r\n    List1.AddItem \"c\"\r\nEnd Sub\r\n"
        + "Private Sub List1_MouseDown(Button As Integer, Shift As Integer, X As Single, Y As Single)\r\n"
        + "    Debug.Print \"MouseDown \" & Button & \" \" & List1.ListIndex\r\nEnd Sub\r\n"
        + "Private Sub List1_MouseUp(Button As Integer, Shift As Integer, X As Single, Y As Single)\r\n"
        + "    Debug.Print \"MouseUp \" & Button & \" \" & List1.ListIndex\r\nEnd Sub\r\n"
        + "Private Sub List1_Click()\r\n    Debug.Print \"Click \" & List1.ListIndex\r\nEnd Sub\r\n"
        + "Private Sub List1_DblClick()\r\n    Debug.Print \"DblClick \" & List1.ListIndex\r\nEnd Sub\r\n";

    // The middle of item `index`, moved `dx` across so that two clicks on one item are not a double click.
    private static Point Item(Running run, int index, double dx = 0)
    {
        var item = ((ListBox)run.Named("List1")).ContainerFromIndex(index)!;
        return item.TranslatePoint(new Point(item.Bounds.Width / 2 + dx, item.Bounds.Height / 2), run.Window)!.Value;
    }

    private static Point BelowTheItems(Running run)
    {
        var last = ((ListBox)run.Named("List1")).ContainerFromIndex(2)!;
        return last.TranslatePoint(new Point(last.Bounds.Width / 2, last.Bounds.Height + 12), run.Window)!.Value;
    }

    [AvaloniaFact]
    public void A_click_on_a_ListBox_item_selects_it_at_the_press_and_raises_Click_before_MouseUp()
    {
        using var run = Start(Form(Twips, AList), ListHandlers);

        run.Click(Item(run, 0));
        run.Click(Item(run, 0, dx: 30));

        run.Errors.Should().BeEmpty();
        // The second click is on the item already selected, and still raises Click.
        run.Printed.Should().Equal("MouseDown 1 0", "Click 0", "MouseUp 1 0", "MouseDown 1 0", "Click 0", "MouseUp 1 0");
    }

    [AvaloniaFact]
    public void A_click_below_the_items_raises_no_Click_and_keeps_the_selection()
    {
        using var run = Start(Form(Twips, AList), ListHandlers);
        run.Click(Item(run, 0));
        run.Printed.Clear();

        run.Click(BelowTheItems(run));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("MouseDown 1 0", "MouseUp 1 0");
    }

    [AvaloniaFact]
    public void A_ListBox_s_selection_follows_a_press_moved_across_the_items_and_Click_has_where_it_ended()
    {
        using var run = Start(Form(Twips, AList), ListHandlers);

        run.Window.MouseDown(Item(run, 0), MouseButton.Left);
        run.Window.MouseMove(Item(run, 2), RawInputModifiers.LeftMouseButton);
        run.Window.MouseUp(Item(run, 2), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("MouseDown 1 0", "Click 2", "MouseUp 1 2");
    }

    [AvaloniaFact]
    public void A_press_on_a_ListBox_item_moved_below_the_items_keeps_it_selected_and_still_raises_Click()
    {
        using var run = Start(Form(Twips, AList), ListHandlers);

        run.Window.MouseDown(Item(run, 2), MouseButton.Left);
        run.Window.MouseMove(BelowTheItems(run), RawInputModifiers.LeftMouseButton);
        run.Window.MouseUp(BelowTheItems(run), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("MouseDown 1 2", "Click 2", "MouseUp 1 2");
    }

    [AvaloniaFact]
    public void A_press_below_the_items_moved_onto_one_selects_nothing_and_raises_no_Click()
    {
        using var run = Start(Form(Twips, AList), ListHandlers);

        run.Window.MouseDown(BelowTheItems(run), MouseButton.Left);
        run.Window.MouseMove(Item(run, 0), RawInputModifiers.LeftMouseButton);
        run.Window.MouseUp(Item(run, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("MouseDown 1 -1", "MouseUp 1 -1");
    }

    [AvaloniaTheory]
    [InlineData(MouseButton.Right, 2)]
    [InlineData(MouseButton.Middle, 4)]
    public void A_right_or_middle_click_on_a_ListBox_item_selects_nothing_and_raises_no_Click(MouseButton button, int vbButton)
    {
        using var run = Start(Form(Twips, AList), ListHandlers);

        run.Click(Item(run, 0), button);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal($"MouseDown {vbButton} -1", $"MouseUp {vbButton} -1");
    }

    [AvaloniaFact]
    public void A_double_click_on_a_ListBox_item_is_MouseDown_Click_MouseUp_DblClick_MouseUp()
    {
        using var run = Start(Form(Twips, AList), ListHandlers);

        run.ClickSequence(Item(run, 0));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("MouseDown 1 0", "Click 0", "MouseUp 1 0", "DblClick 0", "MouseUp 1 0");
    }

    [AvaloniaFact]
    public void An_arrow_key_that_moves_a_ListBox_s_selection_raises_Click_and_one_that_cannot_raises_none()
    {
        using var run = Start(Form(Twips, AList), ListHandlers);
        run.Click(Item(run, 1));
        run.Printed.Clear();

        run.Window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
        run.Window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
        Dispatcher.UIThread.RunJobs();
        run.Window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
        run.Window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
        Dispatcher.UIThread.RunJobs();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Click 0");
    }

    [AvaloniaTheory]
    [InlineData(15L)]
    [InlineData(-1L)]
    public void Under_the_debuggers_time_slice_the_forms_Click_follows_its_MouseUp(long timeSliceMs)
    {
        using var run = Start(Form(Twips, Controls), Handlers("Form"), Debugger(timeSliceMs));

        Thread.Sleep(40);
        run.Window.MouseDown(new Point(300, 10), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(40);
        run.Window.MouseUp(new Point(300, 10), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Form MouseDown 1 0 4500 150", "Form MouseUp 1 0 4500 150", "Form Click");
    }

    [AvaloniaFact]
    public void A_controls_events_are_twips_on_a_pixel_form()
    {
        // Measured: a CommandButton's X and Y are 150, 75 on a pixel form too, unlike its Left.
        using var run = Start(Form(Pixels, Controls), Handlers("Command1"));

        run.Click(run.At("Command1", 10, 5));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Command1 MouseDown 1 0 150 75", "Command1 Click", "Command1 MouseUp 1 0 150 75");
    }

    [AvaloniaFact]
    public void A_click_on_a_control_is_not_the_forms()
    {
        // Measured: a click on a Label raised only the Label's events, not the form's.
        using var run = Start(Form(Twips, Controls), Handlers("Form", click: false));

        run.Click(run.At("Label1", 5, 3));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void MouseMove_carries_the_buttons_held()
    {
        // Measured: a move with no button down is Button 0 at its point, (12, 6) being 180, 90. VB6 reads the buttons
        // from the mouse itself, not the message (a synthetic MK_LBUTTON gave 0), so with the left button really held it
        // is 1.
        using var run = Start(Form(Twips, Controls), Handlers("Command1", click: false));

        run.Window.MouseMove(run.At("Command1", 12, 6));
        Dispatcher.UIThread.RunJobs();
        run.Window.MouseDown(run.At("Command1", 12, 6), MouseButton.Left);
        // Real input carries the buttons held on a move; the headless one carries them only in its modifiers.
        run.Window.MouseMove(run.At("Command1", 13, 7), RawInputModifiers.LeftMouseButton);
        run.Window.MouseUp(run.At("Command1", 13, 7), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Command1 MouseMove 0 0 180 90", "Command1 MouseDown 1 0 180 90",
            "Command1 MouseMove 1 0 195 105", "Command1 MouseUp 1 0 195 105");
    }

    [AvaloniaTheory]
    [InlineData(RawInputModifiers.Shift, 1)]
    [InlineData(RawInputModifiers.Control, 2)]
    [InlineData(RawInputModifiers.Alt, 4)]
    public void Shift_is_VB6s_mask_of_the_keys_held(RawInputModifiers modifiers, int shift)
    {
        // VB6.OLB's vbShiftMask, vbCtrlMask and vbAltMask. VB6 reads the keys from the keyboard, so a synthetic message's
        // MK_SHIFT gave 0 and the mask itself could not be measured that way.
        using var run = Start(Form(Twips, Controls), Handlers("Label1", click: false));

        run.Click(run.At("Label1", 5, 3), MouseButton.Left, modifiers);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal($"Label1 MouseDown 1 {shift} 75 45", $"Label1 MouseUp 1 {shift} 75 45");
    }

    [AvaloniaFact]
    public void An_empty_Image_takes_the_mouse_across_its_whole_rectangle()
    {
        // Measured: an Image with no picture, pressed near its far corner, (78, 58) pixels in, is 1170, 870.
        using var run = Start(Form(Twips, Ctl("Image", "Image1", 240, 240, 1200, 900)), Handlers("Image1", click: false));

        run.Click(run.At("Image1", 78, 58));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Image1 MouseDown 1 0 1170 870", "Image1 MouseUp 1 0 1170 870");
    }

    // Real input on vb6.exe, native matching p-code (#861): the oracle's "An Image's Click across its whole rectangle".
    [AvaloniaTheory]
    [InlineData(50, 30, 750, 450)]
    [InlineData(99, 59, 1485, 885)]
    public void An_empty_Image_raises_Click_at_its_centre_and_far_corner(int x, int y, int twipsX, int twipsY)
    {
        using var run = Start(Form(Twips, Ctl("Image", "Image1", 300, 300, 1500, 900)),
            Handlers("Image1") + Handlers("Form"));

        run.Click(run.At("Image1", x, y));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal($"Image1 MouseDown 1 0 {twipsX} {twipsY}",
            $"Image1 MouseUp 1 0 {twipsX} {twipsY}", "Image1 Click");
    }

    [AvaloniaFact]
    public void One_pixel_past_an_empty_Images_right_edge_raises_only_the_forms_Click()
    {
        using var run = Start(Form(Twips, Ctl("Image", "Image1", 300, 300, 1500, 900)),
            Handlers("Image1") + Handlers("Form"));

        run.Click(run.At("Image1", 101, 30));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Form MouseDown 1 0 1815 750", "Form MouseUp 1 0 1815 750", "Form Click");
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_Image_inside_a_Frame_takes_Click_alone_and_the_Frames_empty_area_takes_its_own(bool image)
    {
        var frame = Ctl("Frame", "Frame1", 240, 1500, 2400, 1500,
            children: Ctl("Image", "Image2", 300, 300, 1200, 900));
        using var run = Start(Form(Twips, frame), Handlers("Image2") + Handlers("Frame1") + Handlers("Form"));

        run.Click(image ? run.At("Image2", 40, 30) : run.At("Frame1", 133, 80));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal(image
            ? new[] { "Image2 MouseDown 1 0 600 450", "Image2 MouseUp 1 0 600 450", "Image2 Click" }
            : new[] { "Frame1 MouseDown 1 0 1995 1200", "Frame1 MouseUp 1 0 1995 1200", "Frame1 Click" });
    }

    [AvaloniaFact]
    public void An_unhandled_event_does_not_pass_to_the_container()
    {
        // Measured: an Image with no handlers, inside a Frame with them, gives the Frame nothing; the Frame's own empty
        // part is the Frame's, (140, 60) in being 2100, 900.
        var frame = Ctl("Frame", "Frame1", 240, 1500, 2400, 1200, "      Caption         =   \"f1\"\r\n",
            "      Begin VB.Image Image2 \r\n         Height          =   600\r\n         Left            =   300\r\n"
          + "         Top             =   300\r\n         Width           =   900\r\n      End\r\n");
        using var run = Start(Form(Twips, frame), Handlers("Frame1", click: false));

        run.Click(run.At("Image2", 10, 10));
        run.Click(run.At("Frame1", 140, 60));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Frame1 MouseDown 1 0 2100 900", "Frame1 MouseUp 1 0 2100 900");
    }

    [AvaloniaFact]
    public void A_ScaleMode_HexIDE_cannot_convert_refuses_the_forms_event_as_the_runs_error()
    {
        // A centimetre form's X is unexplained (#990): refused when the event runs, reported as the run's error, never
        // thrown out of the input handler.
        using var run = Start(Form("   ScaleMode       =   7  'Centimeter\r\n", Controls), Handlers("Form", click: false));

        run.Click(new Point(300, 10));

        run.Errors.Should().NotBeEmpty();
        run.Errors[0].Should().BeOfType<System.NotImplementedException>().Which.Message.Should().Contain("#990");
        run.Printed.Should().BeEmpty();
    }
}
