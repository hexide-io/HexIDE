using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A control's TabIndex and TabStop at run time (#1974). Every row is vb6.exe's, native and p-code agreeing, from the same
/// form: eight controls numbered 0 to 7, Option1 inside Frame1, a Timer, and a control array (the oracle's "TabIndex and
/// TabStop at run time").
/// </summary>
public class TabOrderTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           ClientHeight    =   4000
           ClientWidth     =   6000
           Begin VB.CommandButton Command1
              Caption         =   "Command1"
              Height          =   375
              Left            =   120
              TabIndex        =   0
              Top             =   120
              Width           =   1215
           End
           Begin VB.TextBox Text1
              Height          =   375
              Left            =   120
              TabIndex        =   1
              Text            =   "Text1"
              Top             =   600
              Width           =   1215
           End
           Begin VB.Label Label1
              Caption         =   "Label1"
              Height          =   375
              Left            =   120
              TabIndex        =   2
              Top             =   1080
              Width           =   1215
           End
           Begin VB.CheckBox Check1
              Caption         =   "Check1"
              Height          =   375
              Left            =   120
              TabIndex        =   3
              Top             =   1560
              Width           =   1215
           End
           Begin VB.Frame Frame1
              Caption         =   "Frame1"
              Height          =   1095
              Left            =   1800
              TabIndex        =   4
              Top             =   120
              Width           =   2055
              Begin VB.OptionButton Option1
                 Caption         =   "Option1"
                 Height          =   375
                 Left            =   120
                 TabIndex        =   5
                 Top             =   360
                 Width           =   1215
              End
           End
           Begin VB.Timer Timer1
              Left            =   4200
              Top             =   120
           End
           Begin VB.CommandButton cmdA
              Caption         =   "cmdA0"
              Height          =   375
              Index           =   0
              Left            =   1800
              TabIndex        =   6
              Top             =   1560
              Width           =   1215
           End
           Begin VB.CommandButton cmdA
              Caption         =   "cmdA1"
              Height          =   375
              Index           =   1
              Left            =   1800
              TabIndex        =   7
              Top             =   2040
              Width           =   1215
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private const string Order =
        "Debug.Print Command1.TabIndex & \" \" & Text1.TabIndex & \" \" & Label1.TabIndex & \" \" & Check1.TabIndex & \" \" & "
        + "Frame1.TabIndex & \" \" & Option1.TabIndex & \" \" & cmdA(0).TabIndex & \" \" & cmdA(1).TabIndex";

    private static async Task<string[]> Run(string code)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        using (VbLocale.Use(CultureInfo.GetCultureInfo("en-GB")))
        {
            var printed = await ContainerRuntimeHarness.Run(ctx, env, "On Error Resume Next\r\n" + code + "\r\n");
            return printed.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "").ToArray();
        }
    }

    [AvaloniaFact]
    public async Task TabIndex_reads_as_an_Integer()
    {
        var printed = await Run(Order + "\r\nDebug.Print TypeName(Text1.TabIndex)");

        printed.Should().Equal("0 1 2 3 4 5 6 7", "Integer");
    }

    // Each is the error, then Command1 to cmdA(1)'s TabIndex, as vb6.exe printed them.
    [AvaloniaTheory]
    [InlineData("Check1.TabIndex = 0", "0", "1 2 3 0 4 5 6 7")]
    [InlineData("Command1.TabIndex = 7", "0", "7 0 1 2 3 4 5 6")]
    [InlineData("Command1.TabIndex = 5", "0", "5 0 1 2 3 4 6 7")]
    [InlineData("Text1.TabIndex = 8", "0", "0 7 1 2 3 4 5 6")]
    [InlineData("Text1.TabIndex = 99", "0", "0 7 1 2 3 4 5 6")]
    [InlineData("Text1.TabIndex = 32767", "0", "0 7 1 2 3 4 5 6")]
    [InlineData("Text1.TabIndex = 40000", "6", "0 1 2 3 4 5 6 7")]
    [InlineData("Text1.TabIndex = -1", "380", "0 1 2 3 4 5 6 7")]
    [InlineData("Text1.TabIndex = 1.5", "0", "0 2 1 3 4 5 6 7")]
    [InlineData("Text1.TabIndex = 2.5", "0", "0 2 1 3 4 5 6 7")]
    [InlineData("Text1.TabIndex = \"3\"", "0", "0 3 1 2 4 5 6 7")]
    [InlineData("Text1.TabIndex = \"abc\"", "13", "0 1 2 3 4 5 6 7")]
    [InlineData("Text1.TabIndex = Null", "94", "0 1 2 3 4 5 6 7")]
    [InlineData("Text1.TabIndex = True", "380", "0 1 2 3 4 5 6 7")]
    [InlineData("Text1.TabIndex = Empty", "0", "1 0 2 3 4 5 6 7")]
    [InlineData("Text1.TabIndex = 1", "0", "0 1 2 3 4 5 6 7")]
    [InlineData("Option1.TabIndex = 0", "0", "1 2 3 4 5 0 6 7")]
    [InlineData("Frame1.TabIndex = 0", "0", "1 2 3 4 0 5 6 7")]
    [InlineData("cmdA(1).TabIndex = 0", "0", "1 2 3 4 5 6 7 0")]
    [InlineData("Label1.TabIndex = 7", "0", "0 1 7 2 3 4 5 6")]
    public async Task Setting_TabIndex_moves_the_control_in_one_order_across_the_form(string assignment, string error, string order)
    {
        var printed = await Run($"Err.Clear\r\n{assignment}\r\nDebug.Print Err.Number\r\n{Order}");

        printed.Should().Equal(error, order);
    }

    // The order a running form tabs in is the one the program set.
    [AvaloniaFact]
    public async Task The_form_tabs_in_the_order_the_program_set()
    {
        var (_, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        await ContainerRuntimeHarness.Run(ctx, env, "cmdA(1).TabIndex = 0\r\nCheck1.TabIndex = 1\r\n");

        KeyboardNavigation.GetTabIndex(ContainerRuntimeHarness.Child(canvas, "Check1")).Should().Be(1);
        KeyboardNavigation.GetTabIndex(ContainerRuntimeHarness.Child(canvas, "Command1")).Should().Be(2);
    }

    [AvaloniaTheory]
    [InlineData("", "True", "0")]
    [InlineData("Text1.TabStop = False", "False", "0")]
    [InlineData("Text1.TabStop = 5", "True", "0")]
    [InlineData("Text1.TabStop = 0", "False", "0")]
    [InlineData("Text1.TabStop = \"False\"", "False", "0")]
    [InlineData("Text1.TabStop = \"abc\"", "True", "13")]
    [InlineData("Text1.TabStop = Null", "True", "94")]
    [InlineData("Text1.TabStop = Empty", "False", "0")]
    public async Task TabStop_is_a_Boolean(string assignment, string reads, string error)
    {
        var printed = await Run($"Err.Clear\r\n{assignment}\r\nDebug.Print TypeName(Text1.TabStop), Text1.TabStop, Err.Number");

        printed.Should().Equal("Boolean", reads, error);
    }

    [AvaloniaFact]
    public async Task TabStop_leaves_the_order_alone()
    {
        var printed = await Run("Text1.TabStop = False\r\n" + Order);

        printed.Should().Equal("0 1 2 3 4 5 6 7");
    }

    // An option button reads the TabStop it was given, not the stop its group allows it.
    [AvaloniaFact]
    public async Task An_option_button_reads_its_own_TabStop()
    {
        var printed = await Run("Debug.Print Option1.TabStop\r\nOption1.TabStop = False\r\nDebug.Print Option1.TabStop, Check1.TabStop");

        printed.Should().Equal("True", "False", "True");
    }

    // 438 late-bound in VB6 (measured); refused as any member a control lacks is.
    [AvaloniaTheory]
    [InlineData("Debug.Print Timer1.TabIndex")]
    [InlineData("Debug.Print Label1.TabStop")]
    [InlineData("Debug.Print Frame1.TabStop")]
    public async Task A_Timer_has_no_TabIndex_and_a_Label_or_Frame_no_TabStop(string code)
    {
        var act = () => Run(code);

        await act.Should().ThrowAsync<VBCompileErrorException>();
    }
}
