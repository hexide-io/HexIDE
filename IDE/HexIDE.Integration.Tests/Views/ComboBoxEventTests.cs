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
/// A ComboBox's Click, Change and DropDown (#1429). Each case replays one of the probe's sequences on its form, and every
/// line is the one vb6.exe logged for the same input, native and p-code agreeing (the oracle's "A ComboBox's Click, Change
/// and DropDown, with real input").
/// </summary>
public class ComboBoxEventTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string ProbeForm = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "CEProbe"
           ClientHeight    =   3600
           ClientLeft      =   60
           ClientTop       =   400
           ClientWidth     =   6000
           ScaleHeight     =   3600
           ScaleWidth      =   6000
           Begin VB.ComboBox Combo3
              Height          =   315
              Left            =   3960
              Style           =   2  'Dropdown List
              TabIndex        =   2
              Top             =   120
              Width           =   1800
           End
           Begin VB.ComboBox Combo2
              Height          =   1335
              Left            =   2040
              Style           =   1  'Simple Combo
              TabIndex        =   1
              Text            =   "Combo2"
              Top             =   120
              Width           =   1800
           End
           Begin VB.ComboBox Combo1
              Height          =   315
              Left            =   120
              TabIndex        =   0
              Text            =   "Combo1"
              Top             =   120
              Width           =   1800
           End
        End
        Attribute VB_Name = "Form1"
        Private Function St(ByVal c As Object) As String
            St = "li=" & c.ListIndex & " text=[" & c.Text & "]"
        End Function
        Private Sub Form_Load()
            Combo1.AddItem "apple": Combo1.AddItem "Banana": Combo1.AddItem "cherry"
            Combo2.AddItem "apple": Combo2.AddItem "Banana": Combo2.AddItem "cherry"
            Combo3.AddItem "apple": Combo3.AddItem "Banana": Combo3.AddItem "cherry"
        End Sub
        Private Sub Combo1_Click()
            Debug.Print "  Combo1 Click " & St(Combo1)
        End Sub
        Private Sub Combo1_Change()
            Debug.Print "  Combo1 Change " & St(Combo1)
        End Sub
        Private Sub Combo1_DropDown()
            Debug.Print "  Combo1 DropDown " & St(Combo1)
        End Sub
        Private Sub Combo2_Click()
            Debug.Print "  Combo2 Click " & St(Combo2)
        End Sub
        Private Sub Combo2_Change()
            Debug.Print "  Combo2 Change " & St(Combo2)
        End Sub
        Private Sub Combo3_Click()
            Debug.Print "  Combo3 Click " & St(Combo3)
        End Sub
        Private Sub Combo3_Change()
            Debug.Print "  Combo3 Change " & St(Combo3)
        End Sub
        Private Sub Combo3_DropDown()
            Debug.Print "  Combo3 DropDown " & St(Combo3)
        End Sub

        """;

    /// <summary>A running form, kept open so input can be sent to it; disposing closes it.</summary>
    private sealed class Running : IDisposable
    {
        public required VBFormRuntime Window { get; init; }
        public List<string> Printed { get; } = [];
        public List<Exception> Errors { get; } = [];
        public Action? Detach { get; set; }

        public VBComboBox Combo(string name) =>
            Window.GetVisualDescendants().OfType<VBComboBox>().First(c => VBProps.GetName(c) == name);

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
        VBLoader.RunForm(form, CancellationToken.None, out var window, null);
        var run = new Running { Window = window };
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
        run.Printed.Clear();
        return run;
    }

    private static Avalonia.Input.Platform.PlatformHotkeyConfiguration Hotkeys =>
        Application.Current!.PlatformSettings!.HotkeyConfiguration;

    // Clicks the middle of a visual, through whichever top level holds it: the dropdown's items are in its popup.
    private static void ClickOn(Visual target)
    {
        var top = TopLevel.GetTopLevel(target)!;
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), top)!.Value;
        top.MouseDown(point, MouseButton.Left);
        top.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(Running run, Key key, PhysicalKey physical, RawInputModifiers mods = RawInputModifiers.None)
    {
        run.Window.KeyPress(key, mods, physical, null);
        run.Window.KeyRelease(key, mods, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Type(Running run, string text)
    {
        foreach (var c in text)
            run.Window.KeyTextInput(c.ToString());
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Sends the input a step's label describes, as the probe sent it.</summary>
    private static void Perform(Running run, string list, string action)
    {
        var combo = run.Combo(list);
        switch (action)
        {
            case "click arrow":
                ClickOn(combo.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First(b => b.Name == "toggle"));
                break;
            case var a when a.StartsWith("click item ", StringComparison.Ordinal):
                ClickOn(combo.ContainerFromIndex(int.Parse(a["click item ".Length..]))!);
                break;
            case "click arrow, click same item":
                Perform(run, list, "click arrow");
                Perform(run, list, $"click item {combo.SelectedIndex}");
                break;
            case var a when a.StartsWith("type ", StringComparison.Ordinal):
                Type(run, a["type ".Length..]);
                break;
            case "select all":
                combo.GetVisualDescendants().OfType<TextBox>().First().SelectAll();
                break;
            case "Backspace":
                Press(run, Key.Back, PhysicalKey.Backspace);
                break;
            case "Up":
                Press(run, Key.Up, PhysicalKey.ArrowUp);
                break;
            case "Down":
                Press(run, Key.Down, PhysicalKey.ArrowDown);
                break;
            case "F4":
                Press(run, Key.F4, PhysicalKey.F4);
                break;
            case "Enter":
                Press(run, Key.Enter, PhysicalKey.Enter);
                break;
            case "Escape":
                Press(run, Key.Escape, PhysicalKey.Escape);
                break;
            case "Alt+Down":
                Press(run, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.Alt);
                break;
            default:
                throw new InvalidOperationException($"No input for '{action}'");
        }
    }

    private static void Replay(string list, (string Action, string[] Lines)[] steps)
    {
        using var run = Start();
        run.Combo(list).Focus();
        Dispatcher.UIThread.RunJobs();
        foreach (var (action, lines) in steps)
        {
            run.Printed.Clear();
            Perform(run, list, action);
            run.Errors.Should().BeEmpty(action);
            run.Printed.Should().Equal(lines, action);
        }
    }

    [AvaloniaFact]
    public void A_Dropdown_Combo_s_mouse_and_typing_raise_DropDown_Click_and_Change_as_vb6_exe_s_do() =>
        Replay("Combo1",
        [
            ("click arrow", ["  Combo1 DropDown li=-1 text=[Combo1]"]),
            ("click item 2", ["  Combo1 Click li=2 text=[cherry]"]),
            ("type x", ["  Combo1 Change li=-1 text=[x]"]),
            ("Backspace", ["  Combo1 Change li=-1 text=[]"]),
            ("Up", ["  Combo1 Click li=0 text=[apple]"]),
            ("Down", ["  Combo1 Click li=1 text=[Banana]"]),
            ("select all", []),
            ("type apple",
            [
                "  Combo1 Change li=-1 text=[a]", "  Combo1 Change li=-1 text=[ap]", "  Combo1 Change li=-1 text=[app]",
                "  Combo1 Change li=-1 text=[appl]", "  Combo1 Change li=-1 text=[apple]",
            ]),
            ("Alt+Down", ["  Combo1 DropDown li=-1 text=[apple]"]),
            ("Escape", []),
        ]);

    [AvaloniaFact]
    public void A_Dropdown_Combo_s_keys_in_its_open_list_move_the_selection_at_once_as_vb6_exe_s_do() =>
        Replay("Combo1",
        [
            ("click arrow", ["  Combo1 DropDown li=-1 text=[Combo1]"]),
            ("click item 1", ["  Combo1 Click li=1 text=[Banana]"]),
            ("F4", ["  Combo1 DropDown li=1 text=[Banana]"]),
            ("Down", ["  Combo1 Click li=2 text=[cherry]"]),
            ("Down", []),
            ("Enter", []),
            ("F4", ["  Combo1 DropDown li=2 text=[cherry]"]),
            ("Up", ["  Combo1 Click li=1 text=[Banana]"]),
            ("Escape", []),
            ("click arrow, click same item", ["  Combo1 DropDown li=1 text=[Banana]", "  Combo1 Click li=1 text=[Banana]"]),
        ]);

    [AvaloniaFact]
    public void A_Dropdown_List_takes_the_mouse_and_keys_as_vb6_exe_s_does() =>
        Replay("Combo3",
        [
            ("click arrow", ["  Combo3 DropDown li=-1 text=[]"]),
            ("click item 1", ["  Combo3 Click li=1 text=[Banana]"]),
            ("Down", ["  Combo3 Click li=2 text=[cherry]"]),
            ("type a", ["  Combo3 Click li=0 text=[apple]"]),
            ("click arrow, click same item", ["  Combo3 DropDown li=0 text=[apple]", "  Combo3 Click li=0 text=[apple]"]),
        ]);

    [AvaloniaFact]
    public void A_Dropdown_List_s_keys_in_its_open_list_move_the_selection_at_once_as_vb6_exe_s_do() =>
        Replay("Combo3",
        [
            ("F4", ["  Combo3 DropDown li=-1 text=[]"]),
            ("Down", ["  Combo3 Click li=0 text=[apple]"]),
            ("Down", ["  Combo3 Click li=1 text=[Banana]"]),
            ("Enter", []),
            ("F4", ["  Combo3 DropDown li=1 text=[Banana]"]),
            ("Up", ["  Combo3 Click li=0 text=[apple]"]),
            ("Escape", []),
        ]);

    // A Simple Combo's list is always shown in VB6, which is #1603's; its keys and typing are what is measured here.
    [AvaloniaFact]
    public void A_Simple_Combo_s_typing_and_keys_raise_Change_and_Click_as_vb6_exe_s_do() =>
        Replay("Combo2",
        [
            ("select all", []),
            ("type x", ["  Combo2 Change li=-1 text=[x]"]),
            ("Down", ["  Combo2 Click li=0 text=[apple]"]),
        ]);

    // From code (the oracle's "A ComboBox's Text, at each Style"): ListIndex raises Click when it moves the selection, Text
    // raises Change unless it is the Text already, and Clear raises neither. The last two rows hold an item whose text
    // another shares in another case: the one chosen stays chosen, where Avalonia would select the first match.
    [AvaloniaFact]
    public void Code_s_ListIndex_raises_Click_and_its_Text_raises_Change_only_when_they_change_something()
    {
        var code = ProbeForm.Replace("""
            Combo3.AddItem "apple": Combo3.AddItem "Banana": Combo3.AddItem "cherry"
            """, """
            Combo3.AddItem "apple": Combo3.AddItem "Banana": Combo3.AddItem "cherry"
                Debug.Print "ListIndex = 1": Combo1.ListIndex = 1
                Debug.Print "ListIndex = 1": Combo1.ListIndex = 1
                Debug.Print "Text = zz": Combo1.Text = "zz"
                Debug.Print "Text = zz": Combo1.Text = "zz"
                Debug.Print "ListIndex = 0": Combo1.ListIndex = 0
                Debug.Print "Clear": Combo1.Clear
                Combo1.AddItem "Banana": Combo1.AddItem "banana"
                Debug.Print "ListIndex = 1": Combo1.ListIndex = 1
                Debug.Print "ListIndex = 0": Combo1.ListIndex = 0
            """);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, code.ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form);
        var printed = new List<string>();
        Action<string> print = printed.Add;
        VBDebugConsole.Output += print;
        try
        {
            VBLoader.RunForm(form, CancellationToken.None, out var window, null);
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
        finally
        {
            VBDebugConsole.Output -= print;
        }

        printed.Should().Equal(
            "ListIndex = 1", "  Combo1 Click li=1 text=[Banana]",
            "ListIndex = 1",
            "Text = zz", "  Combo1 Change li=-1 text=[zz]",
            "Text = zz",
            "ListIndex = 0", "  Combo1 Click li=0 text=[apple]",
            "Clear",
            "ListIndex = 1", "  Combo1 Click li=1 text=[banana]",
            "ListIndex = 0", "  Combo1 Click li=0 text=[Banana]");
    }
}
