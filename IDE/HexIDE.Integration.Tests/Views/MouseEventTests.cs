using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

    private static string Handlers(string name, bool index = false, bool click = true)
    {
        var head = index ? "Index As Integer, " : "";
        var pre = index ? "\"(\" & Index & \") \" & " : "";
        var code = "";
        foreach (var ev in new[] { "MouseDown", "MouseUp", "MouseMove" })
            code += $"Private Sub {name}_{ev}({head}Button As Integer, Shift As Integer, X As Single, Y As Single)\r\n"
                  + $"    Debug.Print \"{name} {ev} \" & {pre}Button & \" \" & Shift & \" \" & X & \" \" & Y\r\nEnd Sub\r\n";
        if (click)
            code += $"Private Sub {name}_Click({(index ? "Index As Integer" : "")})\r\n    Debug.Print \"{name} Click\"\r\nEnd Sub\r\n";
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
