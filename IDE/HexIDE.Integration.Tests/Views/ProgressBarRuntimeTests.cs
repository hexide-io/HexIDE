using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A running ProgressBar's members (#1082). Every row is vb6.exe's, native and p-code agreeing, from a form holding one
/// ProgressBar with only its geometry, read late-bound under On Error Resume Next (the oracle's "In-box controls: the
/// ProgressBar").
/// </summary>
public class ProgressBarRuntimeTests
{
    private static string Form(string library, string file) => $$"""
        VERSION 5.00
        Object = "{{file}}"
        Begin VB.Form Form1
           ClientHeight    =   3015
           ClientWidth     =   4560
           Begin {{library}}.ProgressBar ProgressBar1
              Height          =   375
              Left            =   240
              TabIndex        =   0
              Top             =   240
              Width           =   2655
              _ExtentX        =   4683
              _ExtentY        =   661
              Appearance      =   1
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static readonly string Form6 = Form("MSComctlLib", "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCTL.OCX");
    private static readonly string Form5 = Form("ComctlLib", "{6B7E6392-850A-101B-AFC0-4210102A8DA7}#1.3#0\"; \"COMCTL32.OCX");

    private static async Task<string[]> Run(string code, string? form = null)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(form ?? Form6);
        using (VbLocale.Use(CultureInfo.GetCultureInfo("en-GB")))
        {
            var printed = await ContainerRuntimeHarness.Run(ctx, env, "On Error Resume Next\r\n" + code + "\r\n");
            return printed.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "").ToArray();
        }
    }

    // A set from the defaults (Min 0, Max 100, Value 0), then the error, and Value, Min and Max as they read after it.
    private static Task<string[]> Set(string assignment) =>
        Run($"Err.Clear\r\n{assignment}\r\nDebug.Print Err.Number, ProgressBar1.Value, ProgressBar1.Min, ProgressBar1.Max");

    [AvaloniaFact]
    public async Task Its_defaults_read_as_VB6s_with_their_types()
    {
        var printed = await Run("""
            Debug.Print ProgressBar1.Value, TypeName(ProgressBar1.Value), ProgressBar1.Min, ProgressBar1.Max, TypeName(ProgressBar1.Max)
            Debug.Print ProgressBar1.Orientation, TypeName(ProgressBar1.Orientation), ProgressBar1.Scrolling, ProgressBar1.Appearance
            Debug.Print ProgressBar1.BorderStyle, ProgressBar1.OLEDropMode, ProgressBar1.MousePointer, TypeName(ProgressBar1.MousePointer)
            Debug.Print ProgressBar1.Enabled, TypeName(ProgressBar1), ProgressBar1.TabIndex, TypeName(ProgressBar1.TabIndex)
            """);

        printed.Should().Equal("0", "Single", "0", "100", "Single", "0", "Long", "0", "1", "0", "0", "0", "Long",
            "True", "ProgressBar", "0", "Integer");
    }

    [AvaloniaTheory]
    [InlineData("ProgressBar1.Value = 50", "0", "50")]
    [InlineData("ProgressBar1.Value = 100", "0", "100")]
    [InlineData("ProgressBar1.Value = 150", "380", "0")]
    [InlineData("ProgressBar1.Value = -1", "380", "0")]
    [InlineData("ProgressBar1.Value = 100.5", "380", "0")]
    [InlineData("ProgressBar1.Value = 50.25", "0", "50.25")]
    [InlineData("ProgressBar1.Value = \"40\"", "0", "40")]
    [InlineData("ProgressBar1.Value = \"1e1\"", "0", "10")]
    [InlineData("ProgressBar1.Value = \"abc\"", "13", "0")]
    [InlineData("ProgressBar1.Value = True", "380", "0")]
    [InlineData("ProgressBar1.Value = Null", "13", "0")]
    [InlineData("ProgressBar1.Value = Empty", "0", "0")]
    [InlineData("ProgressBar1.Value = 1E+39", "6", "0")]
    public async Task Value_takes_a_Single_inside_Min_to_Max(string assignment, string error, string value)
    {
        var printed = await Set(assignment);

        printed[..2].Should().Equal(error, value);
    }

    [AvaloniaTheory]
    [InlineData("ProgressBar1.Min = 100", "380", "0", "0", "100")]
    [InlineData("ProgressBar1.Min = 200", "380", "0", "0", "100")]
    [InlineData("ProgressBar1.Max = 0", "380", "0", "0", "100")]
    [InlineData("ProgressBar1.Max = -5", "380", "0", "0", "100")]
    [InlineData("ProgressBar1.Min = -100", "380", "0", "0", "100")]
    [InlineData("ProgressBar1.Min = -0.001", "380", "0", "0", "100")]
    [InlineData("ProgressBar1.Min = 0.5", "0", "0.5", "0.5", "100")]
    [InlineData("ProgressBar1.Max = 40000", "0", "0", "0", "40000")]
    [InlineData("ProgressBar1.Max = Empty", "380", "0", "0", "100")]
    [InlineData("ProgressBar1.Max = Null", "13", "0", "0", "100")]
    [InlineData("ProgressBar1.Min = \"abc\"", "13", "0", "0", "100")]
    [InlineData("ProgressBar1.Value = 50: ProgressBar1.Max = 40", "0", "40", "0", "40")]
    [InlineData("ProgressBar1.Value = 50: ProgressBar1.Min = 60", "0", "60", "60", "100")]
    [InlineData("ProgressBar1.Max = 200: ProgressBar1.Min = 150: ProgressBar1.Max = 100", "380", "150", "150", "200")]
    public async Task Min_and_Max_keep_Min_from_0_below_Max_and_carry_Value_with_them(
        string assignment, string error, string value, string min, string max)
    {
        var printed = await Set(assignment);

        printed.Should().Equal(error, value, min, max);
    }

    [AvaloniaTheory]
    [InlineData("Orientation", "1", "0", "1")]
    [InlineData("Orientation", "2", "380", "0")]
    [InlineData("Orientation", "1.5", "380", "0")]
    [InlineData("Orientation", "0.5", "0", "0")]
    [InlineData("Orientation", "\"1\"", "0", "1")]
    [InlineData("Orientation", "True", "380", "0")]
    [InlineData("Orientation", "Null", "13", "0")]
    [InlineData("Orientation", "\"abc\"", "13", "0")]
    [InlineData("Orientation", "1E+10", "6", "0")]
    [InlineData("Scrolling", "1", "0", "1")]
    [InlineData("Scrolling", "5", "380", "0")]
    [InlineData("Appearance", "0", "0", "0")]
    [InlineData("Appearance", "2", "380", "1")]
    [InlineData("Appearance", "-1", "380", "1")]
    [InlineData("BorderStyle", "1", "0", "1")]
    [InlineData("BorderStyle", "2", "380", "0")]
    [InlineData("BorderStyle", "Null", "13", "0")]
    [InlineData("OLEDropMode", "2", "380", "0")]
    [InlineData("MousePointer", "15", "0", "15")]
    [InlineData("MousePointer", "16", "380", "0")]
    [InlineData("MousePointer", "98", "380", "0")]
    [InlineData("MousePointer", "99", "0", "99")]
    [InlineData("MousePointer", "100", "380", "0")]
    [InlineData("MousePointer", "-1", "380", "0")]
    [InlineData("MousePointer", "Null", "13", "0")]
    public async Task Its_enum_properties_take_only_their_constants(string property, string assigned, string error, string reads)
    {
        var printed = await Run($"Err.Clear\r\nProgressBar1.{property} = {assigned}\r\nDebug.Print Err.Number, ProgressBar1.{property}");

        printed.Should().Equal(error, reads);
    }

    [AvaloniaTheory]
    [InlineData("False", "0", "False")]
    [InlineData("5", "0", "True")]
    [InlineData("Null", "13", "True")]
    [InlineData("\"abc\"", "13", "True")]
    public async Task Enabled_is_the_controls_own_and_takes_Null_as_13(string assigned, string error, string reads)
    {
        var printed = await Run($"Err.Clear\r\nProgressBar1.Enabled = {assigned}\r\nDebug.Print Err.Number, ProgressBar1.Enabled");

        printed.Should().Equal(error, reads);
    }

    [AvaloniaFact]
    public async Task Refresh_repaints_and_keeps_Value()
    {
        var printed = await Run("ProgressBar1.Value = 25\r\nProgressBar1.Refresh\r\nDebug.Print Err.Number, ProgressBar1.Value");

        printed.Should().Equal("0", "25");
    }

    [AvaloniaFact]
    public async Task Its_librarys_name_is_part_of_its_type()
    {
        var printed = await Run("Debug.Print TypeOf ProgressBar1 Is MSComctlLib.ProgressBar, TypeOf ProgressBar1 Is ProgressBar");

        printed.Should().Equal("True", "True");
    }

    [AvaloniaFact]
    public async Task A_50_ProgressBar_reads_as_one()
    {
        var printed = await Run("""
            Debug.Print TypeName(ProgressBar1), ProgressBar1.Max, TypeName(ProgressBar1.Value), ProgressBar1.Appearance
            ProgressBar1.Value = 150
            Debug.Print Err.Number
            """, Form5);

        printed.Should().Equal("ProgressBar", "100", "Single", "1", "380");
    }

    // A 5.0 ProgressBar has no Orientation, Scrolling or Refresh (measured, 438 late-bound). HexIDE raises what it raises for
    // any member a control lacks.
    [AvaloniaTheory]
    [InlineData("Debug.Print ProgressBar1.Orientation")]
    [InlineData("Debug.Print ProgressBar1.Scrolling")]
    public async Task A_50_ProgressBar_has_no_Orientation_or_Scrolling(string code)
    {
        var act = () => Run(code, Form5);

        await act.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*Method or data member not found*");
    }

    [AvaloniaFact]
    public async Task A_50_ProgressBar_has_no_Refresh()
    {
        var act = () => Run("ProgressBar1.Refresh", Form5);

        await act.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*Method or data member not found*");
    }

    // VB6 refuses these early-bound as "Method or data member not found" (measured for TabStop, HelpContextID and
    // CausesValidation), and late-bound, Font, BackColor, ForeColor and Text as 438.
    [AvaloniaTheory]
    [InlineData("TabStop")]
    [InlineData("HelpContextID")]
    [InlineData("CausesValidation")]
    [InlineData("Font")]
    [InlineData("BackColor")]
    [InlineData("ForeColor")]
    [InlineData("Text")]
    public async Task Members_a_ProgressBar_lacks_are_not_found(string member)
    {
        var act = () => Run($"Debug.Print ProgressBar1.{member}");

        await act.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*Method or data member not found*");
    }

    // VB6 has no SetFocus on a ProgressBar: 438 late-bound (measured).
    [AvaloniaFact]
    public async Task A_ProgressBar_has_no_SetFocus()
    {
        var act = () => Run("ProgressBar1.SetFocus");

        await act.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*Method or data member not found*");
    }

    [AvaloniaFact]
    public async Task Negotiate_cannot_be_read_at_run_time()
    {
        var printed = await Run("Err.Clear\r\nDebug.Print ProgressBar1.Negotiate\r\nDebug.Print Err.Number");

        printed.Should().Equal("393");
    }

    // Members VB6's ProgressBar has and HexIDE doesn't: refused as not implemented, not as members it lacks.
    [AvaloniaTheory]
    [InlineData("Debug.Print ProgressBar1.hWnd")]
    [InlineData("Debug.Print ProgressBar1.MouseIcon Is Nothing")]
    [InlineData("ProgressBar1.OLEDrag")]
    [InlineData("Debug.Print ProgressBar1.Align")]
    [InlineData("ProgressBar1.WhatsThisHelpID = 5")]
    [InlineData("ProgressBar1.DragMode = 1")]
    public async Task What_HexIDE_does_not_implement_is_refused_as_such(string code)
    {
        var act = () => Run(code);

        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*ProgressBar*not implemented*");
    }

    [AvaloniaFact]
    public async Task The_running_bar_is_the_one_the_form_declared()
    {
        var (_, canvas, _, _) = ContainerRuntimeHarness.Spawn(Form6);

        var bar = ContainerRuntimeHarness.Child(canvas, "ProgressBar1").Should().BeOfType<VBProgressBar>().Subject;

        bar.Focusable.Should().BeFalse("a Windows progress bar takes no focus");
        bar.ShowFull.Should().BeFalse();
        bar.Library.Name.Should().Be("MSComctlLib");
    }
}
