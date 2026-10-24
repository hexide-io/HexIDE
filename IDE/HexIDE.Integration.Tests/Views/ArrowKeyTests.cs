using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The arrow keys on an option button, a check box and a command button move the focus to the previous or next control in
/// its own container, by TabIndex, wrapping, and select an option button they land on (#1625). Every expectation is a line
/// vb6.exe's built EXEs logged, native and p-code agreeing, on the oracle guest's console: #1583's two probes on
/// <see cref="OptionTabStopTests.Form"/> (the oracle's "Tab and the arrow keys among option buttons") and #1625's two (its
/// "Where the arrow keys go from a button").
/// </summary>
public class ArrowKeyTests
{
    // #1583's first probe, steps 16 to 25, after its Tab and Shift+Tab steps 1 to 15 have selected Option4. On a TextBox the
    // arrows do nothing.
    [AvaloniaFact]
    public void Down_moves_from_an_option_to_the_next_control_as_vb6_exe_s_does()
    {
        using var run = OptionTabStopTests.Start();
        run.SetFocus("Text1");
        for (var i = 0; i < 7; i++)
            run.Tab();
        for (var i = 0; i < 7; i++)
            run.Tab(shift: true);

        OptionTabStopTests.Replay(run,
            (() => run.SetFocus("Text1"), []),
            (() => run.Tab(), ["  Option2_GotFocus -2- 4-"]),
            (() => run.Press(PhysicalKey.ArrowDown), ["  Option3_GotFocus --3 4-", "  Option3_Click --3 4-"]),
            (() => run.Press(PhysicalKey.ArrowDown), ["  Text2_GotFocus"]),
            (() => run.Press(PhysicalKey.ArrowDown), []),
            (() => run.Press(PhysicalKey.ArrowUp), []),
            (() => run.Press(PhysicalKey.ArrowRight), []),
            (() => run.Press(PhysicalKey.ArrowLeft), []),
            (() => run.Tab(), ["  Option4_GotFocus --3 4-"]),
            (() => run.Tab(shift: true), ["  Text2_GotFocus"]));
    }

    // #1583's second probe, every step: Up to the previous control, round the Frame's two options, Right as Down and Left as
    // Up, and Space on the selected option doing nothing.
    [AvaloniaFact]
    public void Up_Right_and_Left_move_as_vb6_exe_s_do_and_wrap_in_a_Frame()
    {
        using var run = OptionTabStopTests.Start();
        run.SetFocus("Text1");

        OptionTabStopTests.Replay(run,
            (() => run.SetFocus("Text3"), ["  Text3_GotFocus"]),
            (() => run.Tab(shift: true), ["  Option5_GotFocus -2- -5", "  Option5_Click -2- -5"]),
            (() => run.Tab(shift: true), ["  Text2_GotFocus"]),
            (() => run.Tab(shift: true), ["  Option2_GotFocus -2- -5"]),
            (() => run.Press(PhysicalKey.ArrowUp), ["  Option1_GotFocus 1-- -5", "  Option1_Click 1-- -5"]),
            (() => run.Press(PhysicalKey.ArrowUp), ["  Text1_GotFocus"]),
            (() => run.SetFocus("Option5"), ["  Option5_GotFocus 1-- -5"]),
            (() => run.Press(PhysicalKey.ArrowUp), ["  Option4_GotFocus 1-- 4-", "  Option4_Click 1-- 4-"]),
            (() => run.Press(PhysicalKey.ArrowUp), ["  Option5_GotFocus 1-- -5", "  Option5_Click 1-- -5"]),
            (() => run.SetFocus("Option3"), ["  Option3_GotFocus --3 -5", "  Option3_Click --3 -5"]),
            (() => run.Press(PhysicalKey.ArrowRight), ["  Text2_GotFocus"]),
            (() => run.SetFocus("Option1"), ["  Option1_GotFocus 1-- -5", "  Option1_Click 1-- -5"]),
            (() => run.Press(PhysicalKey.ArrowLeft), ["  Text1_GotFocus"]),
            (() => run.SetFocus("Option2"), ["  Option2_GotFocus -2- -5", "  Option2_Click -2- -5"]),
            (() => run.Press(PhysicalKey.Space), []));
    }

    // #1625's first probe: a Frame for each case, each with its controls in TabIndex order.
    //   A  optA1, optA2 (selected), txtA1 with TabStop False, txtA2
    //   B  optB1, optB2 (selected), txtB1 disabled, txtB2
    //   C  optC1, optC2 (selected), lblC, txtC2
    //   D  txtD1, optD1, optD2 (selected)
    //   E  optE1, optE2 (selected), picE, txtE2
    //   F  chkF1, chkF2, txtF
    //   G  optG1, optG2 (selected), FrameG2 holding txtG, txtG2
    //   H  cmdH1, cmdH2, txtH
    private static string Cases()
    {
        var tab = 0;
        var focusable = new StringBuilder();
        string Control(string type, string name, int top, string extra)
        {
            focusable.Append($"Public Sub Focus_{name}(): {name}.SetFocus: End Sub\r\n");
            return $"      Begin VB.{type} {name} \r\n{extra}         Height          =   300\r\n         Left            =   120\r\n"
                 + $"         TabIndex        =   {tab++}\r\n         Top             =   {top}\r\n         Width           =   1500\r\n      End\r\n";
        }
        string Option(string name, int top, bool selected = false) => Control("OptionButton", name, top,
            $"         Caption         =   \"{name}\"\r\n" + (selected ? "         Value           =   -1  'True\r\n" : ""));
        string Text(string name, int top, string extra = "") => Control("TextBox", name, top, $"         Text            =   \"{name}\"\r\n" + extra);
        string Captioned(string type, string name, int top) => Control(type, name, top, $"         Caption         =   \"{name}\"\r\n");
        string Frame(string name, int left, int top, params string[] inside) =>
            $"   Begin VB.Frame {name} \r\n      Caption         =   \"{name}\"\r\n      Height          =   1700\r\n      Left            =   {left}\r\n"
          + $"      TabIndex        =   {tab++}\r\n      Top             =   {top}\r\n      Width           =   1800\r\n" + string.Concat(inside) + "   End\r\n";

        var frames = Frame("FrameA", 120, 120, Option("optA1", 240), Option("optA2", 540, true),
                         Text("txtA1", 840, "         TabStop         =   0   'False\r\n"), Text("txtA2", 1140))
                   + Frame("FrameB", 2040, 120, Option("optB1", 240), Option("optB2", 540, true),
                         Text("txtB1", 840, "         Enabled         =   0   'False\r\n"), Text("txtB2", 1140))
                   + Frame("FrameC", 3960, 120, Option("optC1", 240), Option("optC2", 540, true), Captioned("Label", "lblC", 840), Text("txtC2", 1140))
                   + Frame("FrameD", 120, 1920, Text("txtD1", 240), Option("optD1", 540), Option("optD2", 840, true))
                   + Frame("FrameE", 2040, 1920, Option("optE1", 240), Option("optE2", 540, true), Control("PictureBox", "picE", 840, ""), Text("txtE2", 1140))
                   + Frame("FrameF", 3960, 1920, Captioned("CheckBox", "chkF1", 240), Captioned("CheckBox", "chkF2", 540), Text("txtF", 840));
        var g1 = Option("optG1", 240);
        var g2 = Option("optG2", 540, true);
        var inner = Text("txtG", 120);
        var nested = "      Begin VB.Frame FrameG2 \r\n         Caption         =   \"FrameG2\"\r\n         Height          =   500\r\n         Left            =   120\r\n"
                   + $"         TabIndex        =   {tab++}\r\n         Top             =   840\r\n         Width           =   1600\r\n" + inner + "      End\r\n";
        frames += Frame("FrameG", 120, 3720, g1, g2, nested, Text("txtG2", 1380))
                + Frame("FrameH", 2040, 3720, Captioned("CommandButton", "cmdH1", 240), Captioned("CommandButton", "cmdH2", 540), Text("txtH", 840));
        return "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"ArrowProbe\"\r\n   ClientHeight    =   5600\r\n   ClientLeft      =   60\r\n"
             + "   ClientTop       =   450\r\n   ClientWidth     =   6000\r\n   ScaleHeight     =   5600\r\n   ScaleWidth      =   6000\r\n"
             + frames + "End\r\nAttribute VB_Name = \"Form1\"\r\n" + focusable;
    }

    private static T Named<T>(OptionTabStopTests.Run run, string name) where T : Control =>
        run.Window.GetVisualDescendants().OfType<T>().Single(c => VBProps.GetName(c) == name);

    // What the probe logged after each step: Screen.ActiveControl, and the options selected and the check boxes' Values.
    private static string Logged(OptionTabStopTests.Run run, params string[] groups)
    {
        Control? focused = TopLevel.GetTopLevel(run.Window)!.FocusManager!.GetFocusedElement() as Control;
        while (focused is not null && VBProps.GetName(focused) is null)
            focused = focused.GetVisualParent() as Control;
        string Selected(string pair) =>
            string.Concat(pair.Split(' ').Where(o => Named<VBOptionButton>(run, "opt" + o).Value));
        var values = groups.Select(g => g.StartsWith("chk")
            ? "chk" + string.Concat(g[3..].Split(' ').Select(c => (int)Named<VBCheckBox>(run, "chk" + c).Value))
            : Selected(g));
        return $"active {(focused is null ? "(none)" : VBProps.GetName(focused))} values {string.Join(" ", values)}";
    }

    private static void Replay(OptionTabStopTests.Run run, string[] groups, params (System.Action Step, string Logged)[] steps)
    {
        var n = 0;
        foreach (var (step, logged) in steps)
        {
            step();
            run.Errors.Should().BeEmpty();
            Logged(run, groups).Should().Be(logged, $"that is what vb6.exe logged after step {++n}");
        }
    }

    private static readonly string[] CaseGroups = ["A1 A2", "B1 B2", "C1 C2", "D1 D2", "E1 E2", "chkF1 F2", "G1 G2"];

    // #1625's first probe, every step. An arrow reaches a TextBox whose TabStop is False, passes over a disabled TextBox, a
    // Label and a nested Frame without going into it, stops on a PictureBox, which keeps the arrows, wraps within its
    // Frame in both directions, and moves the same way from a CheckBox, changing no Value, and from a CommandButton.
    [AvaloniaFact]
    public void An_arrow_reaches_the_next_control_that_can_take_the_focus_whatever_its_TabStop()
    {
        using var run = OptionTabStopTests.Start(Cases());

        Replay(run, CaseGroups,
            (() => run.Call("Focus_optA2"), "active optA2 values A2 B2 C2 D2 E2 chk00 G2"),
            (() => run.Press(PhysicalKey.ArrowDown), "active txtA1 values A2 B2 C2 D2 E2 chk00 G2"),
            (() => run.Call("Focus_optA2"), "active optA2 values A2 B2 C2 D2 E2 chk00 G2"),
            (() => run.Press(PhysicalKey.ArrowUp), "active optA1 values A1 B2 C2 D2 E2 chk00 G2"),
            (() => run.Press(PhysicalKey.ArrowUp), "active txtA2 values A1 B2 C2 D2 E2 chk00 G2"),
            (() => run.Call("Focus_optB2"), "active optB2 values A1 B2 C2 D2 E2 chk00 G2"),
            (() => run.Press(PhysicalKey.ArrowDown), "active txtB2 values A1 B2 C2 D2 E2 chk00 G2"),
            (() => run.Call("Focus_optC2"), "active optC2 values A1 B2 C2 D2 E2 chk00 G2"),
            (() => run.Press(PhysicalKey.ArrowDown), "active txtC2 values A1 B2 C2 D2 E2 chk00 G2"),
            (() => run.Call("Focus_optD2"), "active optD2 values A1 B2 C2 D2 E2 chk00 G2"),
            (() => run.Press(PhysicalKey.ArrowDown), "active txtD1 values A1 B2 C2 D2 E2 chk00 G2"),
            (() => run.Call("Focus_optD1"), "active optD1 values A1 B2 C2 D1 E2 chk00 G2"),
            (() => run.Press(PhysicalKey.ArrowUp), "active txtD1 values A1 B2 C2 D1 E2 chk00 G2"),
            (() => run.Call("Focus_optE2"), "active optE2 values A1 B2 C2 D1 E2 chk00 G2"),
            (() => run.Press(PhysicalKey.ArrowDown), "active picE values A1 B2 C2 D1 E2 chk00 G2"),
            (() => run.Press(PhysicalKey.ArrowDown), "active picE values A1 B2 C2 D1 E2 chk00 G2"),
            (() => run.Call("Focus_chkF1"), "active chkF1 values A1 B2 C2 D1 E2 chk00 G2"),
            (() => run.Press(PhysicalKey.ArrowDown), "active chkF2 values A1 B2 C2 D1 E2 chk00 G2"),
            (() => run.Press(PhysicalKey.ArrowDown), "active txtF values A1 B2 C2 D1 E2 chk00 G2"),
            (() => run.Call("Focus_chkF2"), "active chkF2 values A1 B2 C2 D1 E2 chk00 G2"),
            (() => run.Press(PhysicalKey.ArrowUp), "active chkF1 values A1 B2 C2 D1 E2 chk00 G2"),
            (() => run.Call("Focus_optG2"), "active optG2 values A1 B2 C2 D1 E2 chk00 G2"),
            (() => run.Press(PhysicalKey.ArrowDown), "active txtG2 values A1 B2 C2 D1 E2 chk00 G2"),
            (() => run.Call("Focus_optG1"), "active optG1 values A1 B2 C2 D1 E2 chk00 G1"),
            (() => run.Press(PhysicalKey.ArrowUp), "active txtG2 values A1 B2 C2 D1 E2 chk00 G1"),
            (() => run.Call("Focus_cmdH1"), "active cmdH1 values A1 B2 C2 D1 E2 chk00 G1"),
            (() => run.Press(PhysicalKey.ArrowDown), "active cmdH2 values A1 B2 C2 D1 E2 chk00 G1"),
            (() => run.Call("Focus_cmdH2"), "active cmdH2 values A1 B2 C2 D1 E2 chk00 G1"),
            (() => run.Press(PhysicalKey.ArrowRight), "active txtH values A1 B2 C2 D1 E2 chk00 G1"),
            (() => run.Call("Focus_optA2"), "active optA2 values A2 B2 C2 D1 E2 chk00 G1"),
            (() => run.Press(PhysicalKey.ArrowRight), "active txtA1 values A2 B2 C2 D1 E2 chk00 G1"));
    }

    // #1625's second probe: FrameI holds, in the file's order, optI1 (TabIndex 0), txtI (TabIndex 2) and optI2 (TabIndex 1).
    // An arrow follows TabIndex, not that order, and ZOrder, which changes the order they are drawn in, changes nothing.
    private const string Order = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "ArrowOrder"
           ClientHeight    =   2400
           ClientLeft      =   60
           ClientTop       =   450
           ClientWidth     =   3000
           ScaleHeight     =   2400
           ScaleWidth      =   3000
           Begin VB.Frame FrameI
              Caption         =   "FrameI"
              Height          =   1700
              Left            =   120
              TabIndex        =   3
              Top             =   120
              Width           =   1800
              Begin VB.OptionButton optI1
                 Caption         =   "optI1"
                 Height          =   300
                 Left            =   120
                 TabIndex        =   0
                 Top             =   240
                 Width           =   1500
              End
              Begin VB.TextBox txtI
                 Height          =   300
                 Left            =   120
                 TabIndex        =   2
                 Text            =   "txtI"
                 Top             =   540
                 Width           =   1500
              End
              Begin VB.OptionButton optI2
                 Caption         =   "optI2"
                 Height          =   300
                 Left            =   120
                 TabIndex        =   1
                 Top             =   840
                 Width           =   1500
              End
           End
        End
        Attribute VB_Name = "Form1"
        Public Sub Focus_optI1(): optI1.SetFocus: End Sub
        Public Sub Front_txtI(): txtI.ZOrder 0: optI1.SetFocus: End Sub
        Public Sub Front_optI2(): optI2.ZOrder 0: optI1.SetFocus: End Sub

        """;

    [AvaloniaFact]
    public void An_arrow_follows_TabIndex_not_the_order_the_controls_are_in()
    {
        using var run = OptionTabStopTests.Start(Order);

        Replay(run, ["I1 I2"],
            (() => run.Call("Focus_optI1"), "active optI1 values I1"),
            (() => run.Press(PhysicalKey.ArrowDown), "active optI2 values I2"),
            (() => run.Call("Focus_optI1"), "active optI1 values I1"),
            (() => run.Press(PhysicalKey.ArrowUp), "active txtI values I1"),
            (() => run.Call("Front_txtI"), "active optI1 values I1"),
            (() => run.Press(PhysicalKey.ArrowDown), "active optI2 values I2"),
            (() => run.Call("Front_optI2"), "active optI1 values I1"),
            (() => run.Press(PhysicalKey.ArrowDown), "active optI2 values I2"),
            (() => run.Call("Focus_optI1"), "active optI1 values I1"),
            (() => run.Press(PhysicalKey.ArrowUp), "active txtI values I1"));
    }
}
