using System;
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
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A ListBox with MultiSelect 1 (Simple) and 2 (Extended), driven with pointer and key input (#1413). The form is the
/// probe's, with its handlers, and every step's lines are the ones vb6.exe logged for the same input, native and p-code
/// agreeing (the oracle's "A ListBox's Selected, SelCount and Text", under "With real input" and "The anchor"). VB6's
/// KeyDown lines are left out, since HexIDE's ListBox raises no KeyDown.
/// </summary>
public class MultiSelectListTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string ProbeForm = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "MSProbe"
           ClientHeight    =   2400
           ClientLeft      =   60
           ClientTop       =   400
           ClientWidth     =   6000
           ScaleHeight     =   2400
           ScaleWidth      =   6000
           Begin VB.ListBox List2
              Height          =   1815
              Left            =   2880
              MultiSelect     =   2  'Extended
              TabIndex        =   1
              Top             =   120
              Width           =   2400
           End
           Begin VB.ListBox List1
              Height          =   1815
              Left            =   120
              MultiSelect     =   1  'Simple
              TabIndex        =   0
              Top             =   120
              Width           =   2400
           End
        End
        Attribute VB_Name = "Form1"
        Private Function St1() As String
            Dim i As Integer, bits As String
            For i = 0 To List1.ListCount - 1
                If List1.Selected(i) Then bits = bits & "1" Else bits = bits & "0"
            Next
            St1 = "li=" & List1.ListIndex & " sel=" & bits & " cnt=" & List1.SelCount & " text=[" & List1.Text & "]"
        End Function
        Private Function St2() As String
            Dim i As Integer, bits As String
            For i = 0 To List2.ListCount - 1
                If List2.Selected(i) Then bits = bits & "1" Else bits = bits & "0"
            Next
            St2 = "li=" & List2.ListIndex & " sel=" & bits & " cnt=" & List2.SelCount & " text=[" & List2.Text & "]"
        End Function
        Private Sub Form_Load()
            Dim v As Variant
            Debug.Print "empty: List1.ListIndex=" & List1.ListIndex & " List2.ListIndex=" & List2.ListIndex & " List1.Text=[" & List1.Text & "]"
            List1.AddItem "a": List1.AddItem "b": List1.AddItem "c": List1.AddItem "d": List1.AddItem "e": List1.AddItem "f"
            List2.AddItem "a": List2.AddItem "b": List2.AddItem "c": List2.AddItem "d": List2.AddItem "e": List2.AddItem "f"
            v = List1.MultiSelect
            Debug.Print "List1.MultiSelect=" & v & " " & TypeName(v) & " List2.MultiSelect=" & List2.MultiSelect
            Debug.Print "start List1 " & St1()
            Debug.Print "start List2 " & St2()
        End Sub
        Private Sub List1_Click()
            Debug.Print "List1 Click " & St1()
        End Sub
        Private Sub List1_DblClick()
            Debug.Print "List1 DblClick " & St1()
        End Sub
        Private Sub List1_MouseDown(Button As Integer, Shift As Integer, X As Single, Y As Single)
            Debug.Print "List1 MouseDown b=" & Button & " s=" & Shift & " " & St1()
        End Sub
        Private Sub List1_MouseUp(Button As Integer, Shift As Integer, X As Single, Y As Single)
            Debug.Print "List1 MouseUp b=" & Button & " s=" & Shift & " " & St1()
        End Sub
        Private Sub List2_Click()
            Debug.Print "List2 Click " & St2()
        End Sub
        Private Sub List2_DblClick()
            Debug.Print "List2 DblClick " & St2()
        End Sub
        Private Sub List2_MouseDown(Button As Integer, Shift As Integer, X As Single, Y As Single)
            Debug.Print "List2 MouseDown b=" & Button & " s=" & Shift & " " & St2()
        End Sub
        Private Sub List2_MouseUp(Button As Integer, Shift As Integer, X As Single, Y As Single)
            Debug.Print "List2 MouseUp b=" & Button & " s=" & Shift & " " & St2()
        End Sub

        """;

    /// <summary>A running form, kept open so input can be sent to it; disposing closes it.</summary>
    private sealed class Running : IDisposable
    {
        public required VBFormRuntime Window { get; init; }
        public List<string> Printed { get; } = [];
        public List<Exception> Errors { get; } = [];
        public Action? Detach { get; set; }
        // Clicks land across an item, a different place each time, so that two in a row are not a double click.
        private int nextOffset;

        public ListBox List(string name) =>
            Window.GetVisualDescendants().OfType<ListBox>().First(c => VBProps.GetName(c) == name);

        public Point Item(string list, int index, double dx = 0)
        {
            var item = List(list).ContainerFromIndex(index)!;
            return item.TranslatePoint(new Point(item.Bounds.Width / 2 + dx, item.Bounds.Height / 2), Window)!.Value;
        }

        public double NextOffset() => new[] { -50, -25, 0, 25, 50 }[nextOffset++ % 5];

        public void Dispose()
        {
            Window.Close();
            Detach?.Invoke();
        }
    }

    private static Running Start()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, ProbeForm.ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form);
        var printed = new List<string>();
        Action<string> print = printed.Add;
        VBDebugConsole.Output += print;
        VBLoader.RunForm(form, CancellationToken.None, out var window, null);
        var run = new Running { Window = window };
        run.Printed.AddRange(printed);
        VBDebugConsole.Output -= print;
        void OnFaulted(VBWindowContext _, Exception e) => run.Errors.Add(e);
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

    /// <summary>Sends the input a step's label describes, as the probe's driver sent it.</summary>
    private static void Perform(Running run, string list, string step)
    {
        // "12 Simple: press d, drag to f, release" or "b2 Ctrl+click c": the action follows the step's number and list.
        var action = step[(step.IndexOf(' ') + 1)..];
        if (action.IndexOf(": ", StringComparison.Ordinal) is var colon and >= 0)
            action = action[(colon + 2)..];
        var mods = RawInputModifiers.None;
        // "Ctrl+Shift+click b" takes both, in that order.
        foreach (var (name, flag) in new[] { ("Ctrl+", RawInputModifiers.Control), ("Shift+", RawInputModifiers.Shift) })
            if (action.StartsWith(name, StringComparison.Ordinal))
            {
                mods |= flag;
                action = action[name.Length..];
            }
        static int Index(string letter) => letter.Trim()[0] - 'a';
        var window = run.Window;
        switch (action)
        {
            case "click the empty area":
            {
                var last = run.List(list).ContainerFromIndex(5)!;
                var below = last.TranslatePoint(new Point(last.Bounds.Width / 2, last.Bounds.Height + 12), window)!.Value;
                window.MouseDown(below, MouseButton.Left);
                window.MouseUp(below, MouseButton.Left);
                break;
            }
            case var a when a.StartsWith("right click ", StringComparison.Ordinal):
            {
                var p = run.Item(list, Index(a["right click ".Length..]), run.NextOffset());
                window.MouseDown(p, MouseButton.Right);
                window.MouseUp(p, MouseButton.Right);
                break;
            }
            case var a when a.StartsWith("double click ", StringComparison.Ordinal):
            {
                var p = run.Item(list, Index(a["double click ".Length..]), run.NextOffset());
                window.MouseDown(p, MouseButton.Left);
                window.MouseUp(p, MouseButton.Left);
                window.MouseDown(p, MouseButton.Left);
                window.MouseUp(p, MouseButton.Left);
                break;
            }
            case var a when a.StartsWith("press ", StringComparison.Ordinal):
            {
                // "press d, drag to f, release"
                var from = run.Item(list, Index(a["press ".Length..]));
                var to = run.Item(list, Index(a[(a.IndexOf("drag to ", StringComparison.Ordinal) + "drag to ".Length)..]));
                window.MouseDown(from, MouseButton.Left);
                window.MouseMove(to, RawInputModifiers.LeftMouseButton);
                window.MouseUp(to, MouseButton.Left);
                break;
            }
            case var a when a.StartsWith("click ", StringComparison.Ordinal):
            {
                // "click a" or "click a again"
                var p = run.Item(list, Index(a["click ".Length..]), run.NextOffset());
                window.MouseDown(p, MouseButton.Left, mods);
                window.MouseUp(p, MouseButton.Left, mods);
                break;
            }
            case "Up" or "Down" or "Space":
            {
                var (key, physical) = action switch
                {
                    "Up" => (Key.Up, PhysicalKey.ArrowUp),
                    "Down" => (Key.Down, PhysicalKey.ArrowDown),
                    _ => (Key.Space, PhysicalKey.Space),
                };
                window.KeyPress(key, mods, physical, null);
                window.KeyRelease(key, mods, physical, null);
                break;
            }
            default:
                throw new InvalidOperationException($"No input for step '{step}'");
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Replay(Running run, string list, (string Step, string[] Lines)[] steps)
    {
        foreach (var (step, lines) in steps)
        {
            run.Printed.Clear();
            Perform(run, list, step);
            run.Errors.Should().BeEmpty(step);
            run.Printed.Should().Equal(lines, step);
        }
    }

    [AvaloniaFact]
    public void A_multi_select_list_starts_with_its_focus_on_the_first_item_and_nothing_selected()
    {
        using var run = Start();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal(
            "empty: List1.ListIndex=0 List2.ListIndex=0 List1.Text=[]",
            "List1.MultiSelect=1 Integer List2.MultiSelect=2",
            "start List1 li=0 sel=000000 cnt=0 text=[a]",
            "start List2 li=0 sel=000000 cnt=0 text=[a]");
    }

    [AvaloniaFact]
    public void A_Simple_list_takes_input_as_vb6_exe_does() => ReplayOn("List1", SimpleRun);

    [AvaloniaFact]
    public void An_Extended_list_takes_input_as_vb6_exe_does() => ReplayOn("List2", ExtendedRun);

    [AvaloniaFact]
    public void An_Extended_list_s_Shift_ranges_run_from_its_anchor_as_vb6_exe_s_do() => ReplayOn("List2", AnchorRun);

    private static void ReplayOn(string list, (string Step, string[] Lines)[] steps)
    {
        using var run = Start();
        Replay(run, list, steps);
    }

    // The lines vb6.exe logged, step by step, with its KeyDown lines left out. Generated from the probes' logs.
    private static readonly (string Step, string[] Lines)[] SimpleRun =
    [
        ("1 Simple: click a", ["List1 MouseDown b=1 s=0 li=0 sel=100000 cnt=1 text=[a]", "List1 Click li=0 sel=100000 cnt=1 text=[a]", "List1 MouseUp b=1 s=0 li=0 sel=100000 cnt=1 text=[a]"]),
        ("2 Simple: click c", ["List1 MouseDown b=1 s=0 li=2 sel=101000 cnt=2 text=[c]", "List1 Click li=2 sel=101000 cnt=2 text=[c]", "List1 MouseUp b=1 s=0 li=2 sel=101000 cnt=2 text=[c]"]),
        ("3 Simple: click a again", ["List1 MouseDown b=1 s=0 li=0 sel=001000 cnt=1 text=[a]", "List1 Click li=0 sel=001000 cnt=1 text=[a]", "List1 MouseUp b=1 s=0 li=0 sel=001000 cnt=1 text=[a]"]),
        ("4 Simple: Shift+click e", ["List1 MouseDown b=1 s=1 li=4 sel=001010 cnt=2 text=[e]", "List1 Click li=4 sel=001010 cnt=2 text=[e]", "List1 MouseUp b=1 s=1 li=4 sel=001010 cnt=2 text=[e]"]),
        ("5 Simple: Ctrl+click f", ["List1 MouseDown b=1 s=2 li=5 sel=001011 cnt=3 text=[f]", "List1 Click li=5 sel=001011 cnt=3 text=[f]", "List1 MouseUp b=1 s=2 li=5 sel=001011 cnt=3 text=[f]"]),
        ("6 Simple: Up", ["List1 Click li=4 sel=001011 cnt=3 text=[e]"]),
        ("7 Simple: Space", ["List1 Click li=4 sel=001001 cnt=2 text=[e]"]),
        ("8 Simple: Down", ["List1 Click li=5 sel=001001 cnt=2 text=[f]"]),
        ("9 Simple: Space", ["List1 Click li=5 sel=001000 cnt=1 text=[f]"]),
        ("10 Simple: Shift+Up", ["List1 Click li=4 sel=001000 cnt=1 text=[e]"]),
        ("11 Simple: double click b", ["List1 MouseDown b=1 s=0 li=1 sel=011000 cnt=2 text=[b]", "List1 Click li=1 sel=011000 cnt=2 text=[b]", "List1 MouseUp b=1 s=0 li=1 sel=011000 cnt=2 text=[b]", "List1 DblClick li=1 sel=011000 cnt=2 text=[b]", "List1 MouseUp b=1 s=0 li=1 sel=011000 cnt=2 text=[b]"]),
        ("12 Simple: press d, drag to f, release", ["List1 MouseDown b=1 s=0 li=3 sel=011100 cnt=3 text=[d]", "List1 Click li=5 sel=011100 cnt=3 text=[f]", "List1 MouseUp b=1 s=0 li=5 sel=011100 cnt=3 text=[f]"]),
        ("13 Simple: click the empty area", ["List1 MouseDown b=1 s=0 li=5 sel=011100 cnt=3 text=[f]", "List1 MouseUp b=1 s=0 li=5 sel=011100 cnt=3 text=[f]"]),
        ("14 Simple: right click e", ["List1 MouseDown b=2 s=0 li=5 sel=011100 cnt=3 text=[f]", "List1 MouseUp b=2 s=0 li=5 sel=011100 cnt=3 text=[f]"]),
    ];

    private static readonly (string Step, string[] Lines)[] ExtendedRun =
    [
        ("20 Extended: click a", ["List2 MouseDown b=1 s=0 li=0 sel=100000 cnt=1 text=[a]", "List2 Click li=0 sel=100000 cnt=1 text=[a]", "List2 MouseUp b=1 s=0 li=0 sel=100000 cnt=1 text=[a]"]),
        ("21 Extended: click c", ["List2 MouseDown b=1 s=0 li=2 sel=001000 cnt=1 text=[c]", "List2 Click li=2 sel=001000 cnt=1 text=[c]", "List2 MouseUp b=1 s=0 li=2 sel=001000 cnt=1 text=[c]"]),
        ("22 Extended: Ctrl+click e", ["List2 MouseDown b=1 s=2 li=4 sel=001010 cnt=2 text=[e]", "List2 Click li=4 sel=001010 cnt=2 text=[e]", "List2 MouseUp b=1 s=2 li=4 sel=001010 cnt=2 text=[e]"]),
        ("23 Extended: Ctrl+click c", ["List2 MouseDown b=1 s=2 li=2 sel=000010 cnt=1 text=[c]", "List2 Click li=2 sel=000010 cnt=1 text=[c]", "List2 MouseUp b=1 s=2 li=2 sel=000010 cnt=1 text=[c]"]),
        ("24 Extended: Shift+click f", ["List2 MouseDown b=1 s=1 li=5 sel=000000 cnt=0 text=[f]", "List2 Click li=5 sel=000111 cnt=3 text=[f]", "List2 MouseUp b=1 s=1 li=5 sel=000111 cnt=3 text=[f]"]),
        ("25 Extended: Up", ["List2 Click li=4 sel=000010 cnt=1 text=[e]"]),
        ("26 Extended: Shift+Up", ["List2 Click li=3 sel=000110 cnt=2 text=[d]"]),
        ("27 Extended: Ctrl+Up", ["List2 Click li=2 sel=001000 cnt=1 text=[c]"]),
        ("28 Extended: Ctrl+Space", ["List2 Click li=2 sel=001000 cnt=1 text=[c]"]),
        ("29 Extended: Space", ["List2 Click li=2 sel=001000 cnt=1 text=[c]"]),
        ("30 Extended: press a, drag to d, release", ["List2 MouseDown b=1 s=0 li=0 sel=100000 cnt=1 text=[a]", "List2 Click li=3 sel=111100 cnt=4 text=[d]", "List2 MouseUp b=1 s=0 li=3 sel=111100 cnt=4 text=[d]"]),
        ("31 Extended: double click b", ["List2 MouseDown b=1 s=0 li=1 sel=010000 cnt=1 text=[b]", "List2 Click li=1 sel=010000 cnt=1 text=[b]", "List2 MouseUp b=1 s=0 li=1 sel=010000 cnt=1 text=[b]", "List2 DblClick li=1 sel=010000 cnt=1 text=[b]", "List2 MouseUp b=1 s=0 li=1 sel=010000 cnt=1 text=[b]"]),
        ("32 Extended: click the empty area", ["List2 MouseDown b=1 s=0 li=1 sel=010000 cnt=1 text=[b]", "List2 MouseUp b=1 s=0 li=1 sel=010000 cnt=1 text=[b]"]),
        ("33 Extended: right click e", ["List2 MouseDown b=2 s=0 li=1 sel=010000 cnt=1 text=[b]", "List2 MouseUp b=2 s=0 li=1 sel=010000 cnt=1 text=[b]"]),
    ];

    private static readonly (string Step, string[] Lines)[] AnchorRun =
    [
        ("a1 click c", ["List2 MouseDown b=1 s=0 li=2 sel=001000 cnt=1 text=[c]", "List2 Click li=2 sel=001000 cnt=1 text=[c]", "List2 MouseUp b=1 s=0 li=2 sel=001000 cnt=1 text=[c]"]),
        ("a2 Shift+click f", ["List2 MouseDown b=1 s=1 li=5 sel=001000 cnt=1 text=[f]", "List2 Click li=5 sel=001111 cnt=4 text=[f]", "List2 MouseUp b=1 s=1 li=5 sel=001111 cnt=4 text=[f]"]),
        ("b1 click a", ["List2 MouseDown b=1 s=0 li=0 sel=100000 cnt=1 text=[a]", "List2 Click li=0 sel=100000 cnt=1 text=[a]", "List2 MouseUp b=1 s=0 li=0 sel=100000 cnt=1 text=[a]"]),
        ("b2 Ctrl+click c", ["List2 MouseDown b=1 s=2 li=2 sel=101000 cnt=2 text=[c]", "List2 Click li=2 sel=101000 cnt=2 text=[c]", "List2 MouseUp b=1 s=2 li=2 sel=101000 cnt=2 text=[c]"]),
        ("b3 Shift+click f", ["List2 MouseDown b=1 s=1 li=5 sel=001000 cnt=1 text=[f]", "List2 Click li=5 sel=001111 cnt=4 text=[f]", "List2 MouseUp b=1 s=1 li=5 sel=001111 cnt=4 text=[f]"]),
        ("c1 click c", ["List2 MouseDown b=1 s=0 li=2 sel=001000 cnt=1 text=[c]", "List2 Click li=2 sel=001000 cnt=1 text=[c]", "List2 MouseUp b=1 s=0 li=2 sel=001000 cnt=1 text=[c]"]),
        ("c2 Ctrl+click c", ["List2 MouseDown b=1 s=2 li=2 sel=000000 cnt=0 text=[c]", "List2 Click li=2 sel=000000 cnt=0 text=[c]", "List2 MouseUp b=1 s=2 li=2 sel=000000 cnt=0 text=[c]"]),
        ("c3 Shift+click a", ["List2 MouseDown b=1 s=1 li=0 sel=000000 cnt=0 text=[a]", "List2 Click li=0 sel=110000 cnt=2 text=[a]", "List2 MouseUp b=1 s=1 li=0 sel=110000 cnt=2 text=[a]"]),
        ("d1 click b", ["List2 MouseDown b=1 s=0 li=1 sel=010000 cnt=1 text=[b]", "List2 Click li=1 sel=010000 cnt=1 text=[b]", "List2 MouseUp b=1 s=0 li=1 sel=010000 cnt=1 text=[b]"]),
        ("d2 Shift+Down", ["List2 Click li=2 sel=011000 cnt=2 text=[c]"]),
        ("d3 Shift+Down", ["List2 Click li=3 sel=011100 cnt=3 text=[d]"]),
        ("d4 Shift+Up", ["List2 Click li=2 sel=011000 cnt=2 text=[c]"]),
        ("e1 click c", ["List2 MouseDown b=1 s=0 li=2 sel=001000 cnt=1 text=[c]", "List2 Click li=2 sel=001000 cnt=1 text=[c]", "List2 MouseUp b=1 s=0 li=2 sel=001000 cnt=1 text=[c]"]),
        ("e2 Ctrl+click e", ["List2 MouseDown b=1 s=2 li=4 sel=001010 cnt=2 text=[e]", "List2 Click li=4 sel=001010 cnt=2 text=[e]", "List2 MouseUp b=1 s=2 li=4 sel=001010 cnt=2 text=[e]"]),
        ("e3 Ctrl+click c", ["List2 MouseDown b=1 s=2 li=2 sel=000010 cnt=1 text=[c]", "List2 Click li=2 sel=000010 cnt=1 text=[c]", "List2 MouseUp b=1 s=2 li=2 sel=000010 cnt=1 text=[c]"]),
        ("e4 Shift+click f", ["List2 MouseDown b=1 s=1 li=5 sel=000000 cnt=0 text=[f]", "List2 Click li=5 sel=000111 cnt=3 text=[f]", "List2 MouseUp b=1 s=1 li=5 sel=000111 cnt=3 text=[f]"]),
        ("f1 click e", ["List2 MouseDown b=1 s=0 li=4 sel=000010 cnt=1 text=[e]", "List2 Click li=4 sel=000010 cnt=1 text=[e]", "List2 MouseUp b=1 s=0 li=4 sel=000010 cnt=1 text=[e]"]),
        ("f2 Shift+click b", ["List2 MouseDown b=1 s=1 li=1 sel=000010 cnt=1 text=[b]", "List2 Click li=1 sel=011110 cnt=4 text=[b]", "List2 MouseUp b=1 s=1 li=1 sel=011110 cnt=4 text=[b]"]),
        ("g1 click a", ["List2 MouseDown b=1 s=0 li=0 sel=100000 cnt=1 text=[a]", "List2 Click li=0 sel=100000 cnt=1 text=[a]", "List2 MouseUp b=1 s=0 li=0 sel=100000 cnt=1 text=[a]"]),
        ("g2 Ctrl+click d", ["List2 MouseDown b=1 s=2 li=3 sel=100100 cnt=2 text=[d]", "List2 Click li=3 sel=100100 cnt=2 text=[d]", "List2 MouseUp b=1 s=2 li=3 sel=100100 cnt=2 text=[d]"]),
        ("g3 Shift+click f", ["List2 MouseDown b=1 s=1 li=5 sel=000100 cnt=1 text=[f]", "List2 Click li=5 sel=000111 cnt=3 text=[f]", "List2 MouseUp b=1 s=1 li=5 sel=000111 cnt=3 text=[f]"]),
        ("g4 Ctrl+Shift+click b", ["List2 MouseDown b=1 s=3 li=1 sel=000100 cnt=1 text=[b]", "List2 Click li=1 sel=011100 cnt=3 text=[b]", "List2 MouseUp b=1 s=3 li=1 sel=011100 cnt=3 text=[b]"]),
    ];
}
