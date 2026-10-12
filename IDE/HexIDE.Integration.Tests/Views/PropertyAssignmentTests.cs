using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Assigning to a control property converts the right-hand side to the property's type, as VB6 does (#768)
/// (`Label1.Caption = 5` stores "5"). Before, anything but the property's exact CLR type threw a plain
/// exception, which the event dispatch swallowed, so the handler stopped there without a word.
///
/// <para>
/// Every expectation is <c>vb6.exe</c> output, recorded under <i>Assigning to a control property</i> in
/// <c>docs/vb6-fidelity-oracle.md</c>: a real form, each assignment under <c>On Error Resume Next</c>, the value
/// read back and <c>Err.Number</c> logged. Each case resets the property first, so a refused assignment shows as
/// the reset value unchanged.
/// </para>
///
/// <para>
/// Left out on purpose, because HexIDE does not match VB6 there yet:
/// <list type="bullet">
/// <item><c>CStr</c> of a Single or a Double (<c>Caption = CSng(1) / 3</c> is "0.3333333" in VB6, #772);</item>
/// <item><c>CStr</c> of a time or of a midnight date (#771);</item>
/// <item>a string with a comma (<c>Left = "1,5"</c> is 15 in VB6, the comma read as a thousands separator, #770);</item>
/// <item><c>Left</c> rounding to whole twips, which belongs to HexIDE's unit model.</item>
/// </list>
/// A case pinned here at HexIDE's answer would certify the divergence.
/// </para>
/// </summary>
public class PropertyAssignmentTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           Begin VB.Timer Timer1
              Enabled         =   0   'False
              Interval        =   500
              Left            =   3600
              Top             =   2400
           End
           Begin VB.CheckBox Check1
              Caption         =   "Check1"
              Left            =   120
              Top             =   1800
              Width           =   1215
              Height          =   375
           End
           Begin VB.CommandButton Command1
              Caption         =   "Command1"
              Left            =   120
              Top             =   1200
              Width           =   1215
              Height          =   495
           End
           Begin VB.TextBox Text1
              Text            =   "Text1"
              Left            =   120
              Top             =   600
              Width           =   1215
              Height          =   375
           End
           Begin VB.Label Label1
              Caption         =   "Label1"
              Left            =   120
              Top             =   120
              Width           =   1215
              Height          =   255
           End
        End
        Attribute VB_Name = "Form1"
        """;

    /// <summary>Resets, assigns under On Error Resume Next, and returns what reads back and the error number.</summary>
    private static async Task<(string Value, int Err)> Assign(string reset, string assignment, string readBack)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var debug = await ContainerRuntimeHarness.Run(ctx, env,
            "On Error Resume Next\r\n"
          + reset + "\r\n"
          + "Err.Clear\r\n"
          + assignment + "\r\n"
          + "Debug.Print Err.Number\r\n"
          + "Debug.Print " + readBack + "\r\n");
        return (Convert.ToString(debug[1].Value, CultureInfo.CurrentCulture) ?? "", Convert.ToInt32(debug[0].Value));
    }

    // ── a String property takes CStr of anything; Null is 94 and changes nothing ─────────────────

    [AvaloniaTheory]
    [InlineData("Label1.Caption = 5", "5")]
    [InlineData("Label1.Caption = -7", "-7")]
    [InlineData("Label1.Caption = 1E+20", "1E+20")]
    [InlineData("Label1.Caption = 12345678901234#", "12345678901234")]
    [InlineData("Label1.Caption = True", "True")]
    [InlineData("Label1.Caption = False", "False")]
    [InlineData("Label1.Caption = Empty", "")]
    [InlineData("Text1.Text = 5", "5")]
    [InlineData("Text1.Text = True", "True")]
    [InlineData("Command1.Caption = 5", "5")]
    [InlineData("Check1.Caption = 5", "5")]
    public async Task A_String_property_stores_the_value_as_a_string(string assignment, string expected)
    {
        var target = assignment[..assignment.IndexOf(" =", StringComparison.Ordinal)];

        var (value, err) = await Assign($"{target} = \"(unset)\"", assignment, target);

        err.Should().Be(0);
        value.Should().Be(expected);
    }

    [AvaloniaFact]
    public async Task A_fractional_number_is_written_in_the_locale_as_VB6_writes_it()
    {
        // VB6 uses the system locale here too: the oracle machine is en-GB and gave "1.5".
        var (value, err) = await Assign("Label1.Caption = \"(unset)\"", "Label1.Caption = 1.5", "Label1.Caption");

        err.Should().Be(0);
        value.Should().Be(1.5.ToString(CultureInfo.CurrentCulture));
    }

    [AvaloniaTheory]
    [InlineData("Label1.Caption")]
    [InlineData("Text1.Text")]
    [InlineData("Label1.Tag")]
    public async Task Null_into_a_String_property_is_error_94_and_leaves_it_unchanged(string target)
    {
        var (value, err) = await Assign($"{target} = \"(unset)\"", $"{target} = Null", target);

        err.Should().Be(94);
        value.Should().Be("(unset)");
    }

    [AvaloniaFact]
    public async Task Tag_is_a_String_whatever_is_put_in_it()
    {
        var (value, err) = await Assign("Label1.Tag = \"\"", "Label1.Tag = 5", "TypeName(Label1.Tag) & \"=\" & Label1.Tag");

        err.Should().Be(0);
        value.Should().Be("String=5");
    }

    // ── a numeric property parses a String, rounds half to even, and raises 6, 13 or 94 ──────────

    [AvaloniaTheory]
    [InlineData("Label1.Left = \"300\"", "300", 0)]
    [InlineData("Label1.Left = \"abc\"", "120", 13)]
    [InlineData("Label1.Left = \"\"", "120", 13)]
    [InlineData("Label1.Left = Null", "120", 94)]
    [InlineData("Label1.Left = Empty", "0", 0)]
    [InlineData("Label1.Left = True", "-1", 0)]
    [InlineData("Label1.Left = 1E+20", "120", 6)]
    public async Task A_position_converts_as_a_Single(string assignment, string expected, int expectedErr)
    {
        var (value, err) = await Assign("Label1.Left = 120", assignment, "Label1.Left");

        err.Should().Be(expectedErr);
        value.Should().Be(expected);
    }

    [AvaloniaTheory]
    [InlineData("Timer1.Interval = \"1000\"", "1000", 0)]
    [InlineData("Timer1.Interval = \"abc\"", "500", 13)]
    [InlineData("Timer1.Interval = 2.5", "2", 0)]
    [InlineData("Timer1.Interval = 3.5", "4", 0)]
    [InlineData("Timer1.Interval = Null", "500", 94)]
    [InlineData("Timer1.Interval = 65535", "65535", 0)]
    [InlineData("Timer1.Interval = 65536", "500", 380)]
    [InlineData("Timer1.Interval = True", "500", 380)]
    [InlineData("Timer1.Interval = 3000000000#", "500", 6)]
    public async Task Interval_converts_as_a_Long_and_refuses_what_is_out_of_range(string assignment, string expected, int expectedErr)
    {
        var (value, err) = await Assign("Timer1.Interval = 500", assignment, "Timer1.Interval");

        err.Should().Be(expectedErr);
        value.Should().Be(expected);
    }

    // ── an enum property converts as an Integer, then refuses a value it does not have (380) ───────

    [AvaloniaTheory]
    [InlineData("Check1.Value = 1", "1", 0)]
    [InlineData("Check1.Value = \"1\"", "1", 0)]
    [InlineData("Check1.Value = 2", "2", 0)]
    [InlineData("Check1.Value = 1.5", "2", 0)]
    [InlineData("Check1.Value = 1.6", "2", 0)]
    [InlineData("Check1.Value = 2.5", "2", 0)]
    [InlineData("Check1.Value = 2.6", "0", 380)]
    [InlineData("Check1.Value = 3", "0", 380)]
    [InlineData("Check1.Value = -1", "0", 380)]
    [InlineData("Check1.Value = True", "0", 380)]
    [InlineData("Check1.Value = \"abc\"", "0", 13)]
    [InlineData("Check1.Value = \"\"", "0", 13)]
    [InlineData("Check1.Value = Null", "0", 94)]
    [InlineData("Check1.Value = 40000", "0", 6)]
    public async Task CheckBox_Value_converts_as_an_Integer_from_0_to_2(string assignment, string expected, int expectedErr)
    {
        var (value, err) = await Assign("Check1.Value = 0", assignment, "Check1.Value");

        err.Should().Be(expectedErr);
        value.Should().Be(expected);
    }

    [AvaloniaTheory]
    [InlineData("\"2\"", 0)]
    [InlineData("4", 0)]       // valid in VB6, though VBCursorType has no member for it (#120)
    [InlineData("5", 0)]
    [InlineData("13", 0)]
    [InlineData("14", 0)]
    [InlineData("15", 0)]
    [InlineData("99", 0)]
    [InlineData("16", 380)]
    [InlineData("98", 380)]
    [InlineData("-1", 380)]
    [InlineData("True", 380)]
    [InlineData("40000", 6)]
    public async Task MousePointer_accepts_0_to_15_and_99(string value, int expectedErr)
    {
        // Only the error is asserted: MousePointer does not read back what was set in HexIDE yet (#769).
        var (_, err) = await Assign("Label1.MousePointer = 0", $"Label1.MousePointer = {value}", "0");

        err.Should().Be(expectedErr);
    }

    // ── a colour converts as a Long, then refuses what is not an OLE colour (380) ─────────────────

    [AvaloniaTheory]
    [InlineData("\"255\"", 0)]
    [InlineData("255.6", 0)]
    [InlineData("\"&HFF\"", 0)]
    [InlineData("&HFFFFFF", 0)]
    [InlineData("&H1000000", 0)]       // a palette index
    [InlineData("&H100FFFF", 0)]
    [InlineData("&H1010000", 380)]
    [InlineData("&H2FFFFFF", 0)]       // a palette-relative RGB
    [InlineData("&H3000000", 380)]
    [InlineData("&H80000005", 0)]      // a system colour
    [InlineData("&H80000018", 0)]
    [InlineData("&H80000019", 380)]
    [InlineData("-2", 380)]
    [InlineData("True", 380)]
    [InlineData("1E+20", 6)]
    [InlineData("Null", 94)]
    public async Task BackColor_accepts_what_OLE_accepts_as_a_colour(string value, int expectedErr)
    {
        var (_, err) = await Assign("Text1.BackColor = 0", $"Text1.BackColor = {value}", "0");

        err.Should().Be(expectedErr);
    }

    [AvaloniaFact]
    public async Task A_number_written_as_a_string_still_sets_the_colour()
    {
        // Read from the control rather than through VB6 code, which reads a colour back as HexIDE's own colour
        // value rather than VB6's Long: a separate gap.
        var (_, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(Form);

        var debug = await ContainerRuntimeHarness.Run(ctx, env,
            "On Error Resume Next\r\nText1.BackColor = 0\r\nText1.BackColor = \"255\"\r\nDebug.Print Err.Number\r\n");

        debug[0].Value.Should().Be(0);
        AvaloniaInteroperability.TryGet(ContainerRuntimeHarness.Child(canvas, "Text1"), VBProperties.BackColorProperty, out var read)
            .Should().BeTrue();
        read.Value.Should().Be(VBColor.FromOle(255));
    }

    // ── what is not a value to convert fails clearly rather than storing something wrong ──────────

    [AvaloniaFact]
    public async Task A_control_on_the_right_is_not_converted_into_a_type_name()
    {
        // VB6 reads Text1's default property here. HexIDE does not implement that, and must not store the
        // reference's .NET type name as the caption instead.
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);

        var act = () => ContainerRuntimeHarness.Run(ctx, env, "On Error Resume Next\r\nLabel1.Caption = Text1\r\n");

        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Contain("Caption");
    }
}
