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

    private static Running Start(string frm, string code, HexIDE.Runtime.Debugging.IDebugGate? debugger = null)
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
    [InlineData(15L, false)]
    [InlineData(-1L, false)]
    [InlineData(15L, true)]
    [InlineData(-1L, true)]
    public async Task Automation_click_double_runs_the_forms_DblClick_once_in_the_measured_order(long timeSliceMs, bool slow)
    {
        // Real-input oracle #2417: arrival, then a move after the first release, none after the second.
        using var run = Start(Form(Twips, Controls), Handlers("Form", dblClick: true), Debugger(timeSliceMs));
        // #3167: handler time must not decide whether automation sends a double click.
        if (slow)
        {
            var releases = 0;
            run.Window.AddHandler(InputElement.PointerReleasedEvent, (_, _) =>
            {
                if (++releases == 1)
                    Thread.Sleep(Application.Current!.PlatformSettings!.GetDoubleTapTime(PointerType.Mouse)
                        + System.TimeSpan.FromMilliseconds(50));
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        }
        var outcome = await AutomationClick(run, "300,10 double");
        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Contain("double-clicked").And.Contain("left button").And.Contain("(300, 10)");
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Form MouseMove 0 0 4500 150", "Form MouseDown 1 0 4500 150", "Form MouseUp 1 0 4500 150",
            "Form Click", "Form MouseMove 0 0 4500 150", "Form DblClick", "Form MouseUp 1 0 4500 150");
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

}
