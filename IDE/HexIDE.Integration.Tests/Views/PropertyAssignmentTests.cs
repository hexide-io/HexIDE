using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Assigning to a control property converts the right-hand side to the property's type, as VB6 does (#768)
/// (`Label1.Caption = 5` stores "5"). Before, anything but the property's exact CLR type threw a plain
/// exception, which the event dispatch swallowed, so the handler stopped there without a word.
///
/// <para>
/// The expectations are <c>vb6.exe</c> output, recorded under <i>Assigning to a control property</i> in
/// <c>docs/vb6-fidelity-oracle/assigning-to-a-control-property.md</c>: a real form, each assignment under <c>On Error Resume Next</c>, the value
/// read back and <c>Err.Number</c> logged. Each case resets the property first, so a refused assignment shows as
/// the reset value unchanged. The oracle machine is en-GB, and every case runs in that locale.
/// </para>
///
/// <para>
/// Not every case runs on the control it was measured on:
/// <list type="bullet">
/// <item>the OLE colour range was measured on <c>Label1.BackColor</c>, and pinned on <c>Text1</c>; the system colours
/// past <c>&amp;H18</c> and the hex strings were measured on <c>Text1</c> itself;</item>
/// <item>a colour's read-back as a Long was measured on <c>Text1</c>, and the copies here read it from a
/// PictureBox, a Label and a TextBox;</item>
/// <item><c>ListIndex</c> was measured on a ListBox and a ComboBox, as here.</item>
/// </list>
/// Pinned at HexIDE's behaviour rather than at a measurement: the refusals that name an issue, and the two
/// <c>Tag</c> idioms, which hold until #211 makes <c>Tag</c> a String.
/// </para>
///
/// <para>
/// Left out on purpose, because HexIDE does not match VB6 there yet:
/// <list type="bullet">
/// <item>a string with a comma (<c>Left = "1,5"</c> is 15 in VB6, the comma read as a thousands separator, #770);</item>
/// <item>a hex string past 16 bits (<c>BackColor = "&amp;H8000000F"</c> is a system colour in VB6, an Overflow here, #788);</item>
/// <item><c>Tag</c>, which VB6 holds as a String (<c>Tag = 5</c> reads back "5") and HexIDE as given, until #211;</item>
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
           Begin VB.HScrollBar HScroll2
              Left            =   120
              Top             =   2700
              Width           =   2000
              Height          =   255
              Min             =   100
              Max             =   0
              Value           =   100
           End
           Begin VB.HScrollBar HScroll1
              Left            =   120
              Top             =   2400
              Width           =   2000
              Height          =   255
              Max             =   100
           End
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
           Begin VB.ComboBox Combo1
              Left            =   2400
              Top             =   2300
              Width           =   1815
              Height          =   315
           End
           Begin VB.ListBox List2
              Left            =   4000
              Top             =   1200
              Width           =   1815
              Height          =   645
           End
           Begin VB.ListBox List1
              Left            =   4000
              Top             =   120
              Width           =   1815
              Height          =   645
           End
           Begin VB.PictureBox Picture2
              BackColor       =   &H8000000F&
              Left            =   2400
              Top             =   1200
              Width           =   1215
              Height          =   975
           End
           Begin VB.PictureBox Picture1
              BackColor       =   &H000000FF&
              ForeColor       =   &H00FF0000&
              Left            =   2400
              Top             =   120
              Width           =   1215
              Height          =   975
           End
           Begin VB.Label Label1
              BackColor       =   &H0000FF00&
              Caption         =   "Label1"
              Left            =   120
              Top             =   120
              Width           =   1215
              Height          =   255
           End
        End
        Attribute VB_Name = "Form1"
        """;

    /// <summary>
    /// Runs code in the oracle's locale, en-GB, as the program's explicit locale, and returns what it printed.
    /// Literal parsing is invariant (#1610); changing the dispatcher's culture across an await risks a leak (#1612).
    /// </summary>
    private static async Task<System.Collections.Generic.List<Vb6Value>> RunInOracleLocale(
        ModuleExecutionContext ctx, ExecutionEnvironment env, string code)
    {
        using (VbLocale.Use(CultureInfo.GetCultureInfo("en-GB")))
            return await ContainerRuntimeHarness.Run(ctx, env, code);
    }

    /// <summary>Resets, assigns under On Error Resume Next, and returns what reads back and the error number.</summary>
    private static async Task<(string Value, int Err)> Assign(string reset, string assignment, string readBack)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var debug = await RunInOracleLocale(ctx, env,
            "On Error Resume Next\r\n"
          + reset + "\r\n"
          + "Err.Clear\r\n"
          + assignment + "\r\n"
          + "Debug.Print Err.Number\r\n"
          + "Debug.Print " + readBack + "\r\n");
        return (Convert.ToString(debug[1].Value, CultureInfo.InvariantCulture) ?? "", Convert.ToInt32(debug[0].Value));
    }

    /// <summary>Runs code that should stop at something HexIDE does not implement, and returns what it said.</summary>
    private static async Task<string> Refusal(string code)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);

        var act = () => RunInOracleLocale(ctx, env, "On Error Resume Next\r\n" + code + "\r\n");

        return (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message;
    }

    /// <summary>The colour a control holds, read from the control rather than through VB6 code.</summary>
    private static VBColor ColourOf(Avalonia.Controls.Canvas canvas, string control, PropertyClass? property = null)
    {
        // A program reads a colour as its Long (#789), so this turns the Long back into the colour it names.
        AvaloniaInteroperability.TryGet(ContainerRuntimeHarness.Child(canvas, control), property ?? VBProperties.BackColorProperty, out var read)
            .Should().BeTrue();
        return VBColor.FromOle(read.Value.Should().BeOfType<long>().Subject);
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
    [InlineData("Label1.Caption = 1.5", "1.5")]
    [InlineData("Label1.Caption = 1 / 3", "0.333333333333333")]
    [InlineData("Label1.Caption = CSng(1) / 3", "0.3333333")]
    [InlineData("Label1.Caption = 0.1 + 0.2", "0.3")]
    [InlineData("Label1.Caption = CCur(2.5)", "2.5")]
    [InlineData("Label1.Caption = CDec(2.5)", "2.5")]
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

    [AvaloniaTheory]
    [InlineData("Label1.Caption", "#1/2/2003#", "02/01/2003")]
    [InlineData("Label1.Caption", "#10:30:00 AM#", "10:30:00")]
    [InlineData("Label1.Caption", "#1/2/2003 10:30:00 AM#", "02/01/2003 10:30:00")]
    [InlineData("Text1.Text", "#1/2/2003#", "02/01/2003")]
    public async Task A_Date_into_a_String_property_is_written_as_CStr_writes_it_in_the_users_locale(
        string target, string literal, string expected)
    {
        // VB6 writes a Date in the system's regional format, as CStr does: day first under en-GB (measured, #777).
        var (value, err) = await Assign($"{target} = \"(unset)\"", $"{target} = {literal}", target);

        err.Should().Be(0);
        value.Should().Be(expected);
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

    // ── Tag keeps what it is given until #211, so the idioms that compare and count with it work ────

    [AvaloniaFact]
    public async Task A_Tag_set_to_a_number_compares_equal_to_that_number()
    {
        var (value, err) = await Assign("Command1.Tag = 1",
            "If Command1.Tag = 1 Then Label1.Caption = \"equal\" Else Label1.Caption = \"different\"", "Label1.Caption");

        err.Should().Be(0);
        value.Should().Be("equal");
    }

    [AvaloniaFact]
    public async Task A_Tag_can_count()
    {
        var (value, err) = await Assign("Label1.Tag = 5", "Label1.Tag = Label1.Tag + 1", "Label1.Tag");

        err.Should().Be(0);
        value.Should().Be("6");
    }

    [AvaloniaTheory]
    [InlineData("Label1.Tag = Empty")]
    [InlineData("Dim never\r\nLabel1.Tag = never")]
    public async Task Empty_into_Tag_is_an_empty_String(string assignment)
    {
        // Empty is "" to a String, which Tag is in VB6 (CStr(Empty) is "", measured). It used to end the run as
        // Internal error 51.
        var (value, err) = await Assign("Label1.Tag = \"(unset)\"", assignment,
            "TypeName(Label1.Tag) & \"=[\" & Label1.Tag & \"]\"");

        err.Should().Be(0);
        value.Should().Be("String=[]");
    }

    [AvaloniaFact]
    public async Task An_unset_Tag_is_an_empty_String_and_copies_without_error()
    {
        var (value, err) = await Assign("Text1.Tag = \"(unset)\"", "Text1.Tag = Label1.Tag",
            "TypeName(Text1.Tag) & \"=[\" & Text1.Tag & \"]\"");

        err.Should().Be(0);
        value.Should().Be("String=[]");
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
    [InlineData("Label1.Left = \"NaN\"", "120", 13)]
    [InlineData("Label1.Left = \"Infinity\"", "120", 13)]
    [InlineData("Label1.Left = \"-Infinity\"", "120", 13)]
    [InlineData("Label1.Left = \"1.#INF\"", "120", 13)]
    [InlineData("Label1.Left = \"1.#QNAN\"", "120", 13)]
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

    [AvaloniaTheory]
    [InlineData("HScroll1.Value = 50", "50", 0)]
    [InlineData("HScroll1.Value = \"50\"", "50", 0)]
    [InlineData("HScroll1.Value = 100", "100", 0)]
    [InlineData("HScroll1.Value = \"100\"", "100", 0)]
    [InlineData("HScroll1.Value = 2.5", "2", 0)]
    [InlineData("HScroll1.Value = 99.5", "100", 0)]
    [InlineData("HScroll1.Value = Empty", "0", 0)]
    [InlineData("HScroll1.Value = 101", "10", 380)]
    [InlineData("HScroll1.Value = 150", "10", 380)]
    [InlineData("HScroll1.Value = \"150\"", "10", 380)]
    [InlineData("HScroll1.Value = -1", "10", 380)]
    [InlineData("HScroll1.Value = \"-1\"", "10", 380)]
    [InlineData("HScroll1.Value = True", "10", 380)]
    [InlineData("HScroll1.Value = \"abc\"", "10", 13)]
    public async Task A_scroll_bar_Value_converts_and_refuses_what_is_outside_Min_to_Max(string assignment, string expected, int expectedErr)
    {
        // Avalonia would clamp 150 to 100. VB6 refuses it with 380, a String included.
        var (value, err) = await Assign("HScroll1.Value = 10", assignment, "HScroll1.Value");

        err.Should().Be(expectedErr);
        value.Should().Be(expected);
    }

    [AvaloniaTheory]
    [InlineData("50", "50", 0)]
    [InlineData("0", "0", 0)]
    [InlineData("100", "100", 0)]
    [InlineData("150", "100", 380)]
    [InlineData("-1", "100", 380)]
    [InlineData("\"50\"", "50", 0)]
    [InlineData("\"abc\"", "100", 13)]
    [InlineData("Empty", "0", 0)]
    [InlineData("True", "100", 380)]
    [InlineData("2.5", "2", 0)]
    [InlineData("99.5", "100", 0)]
    public async Task A_reversed_scroll_bar_converts_and_checks_Value(string expression, string expected, int expectedErr)
    {
        var (value, err) = await Assign("HScroll2.Value = 100", $"HScroll2.Value = {expression}", "HScroll2.Value");

        err.Should().Be(expectedErr);
        value.Should().Be(expected);
    }

    [AvaloniaTheory]
    [InlineData("HScrollBar")]
    [InlineData("VScrollBar")]
    public void A_reversed_scroll_bar_loads_and_maps_thumb_positions(string controlType)
    {
        var (_, canvas, _, _) = ContainerRuntimeHarness.Spawn(Form.Replace("VB.HScrollBar", "VB." + controlType));
        var bar = (HexIDE.Runtime.BuiltinControls.VBScrollBar)canvas.Children[0];

        bar.VbMin.Should().Be(100);
        bar.VbMax.Should().Be(0);
        bar.Minimum.Should().Be(0);
        bar.Maximum.Should().Be(100);
        bar.Value.Should().Be(0);
        AvaloniaInteroperability.TryGet(bar, VBProperties.ValueProperty, out var loaded).Should().BeTrue();
        loaded.Value.Should().Be(100);
        bar.Value = 100;
        AvaloniaInteroperability.TryGet(bar, VBProperties.ValueProperty, out var moved).Should().BeTrue();
        moved.Value.Should().Be(0);
    }

    private const string ThreeItems = "List1.AddItem \"a\"\r\nList1.AddItem \"b\"\r\nList1.AddItem \"c\"\r\n"
                                      + "Combo1.AddItem \"a\"\r\nCombo1.AddItem \"b\"\r\nCombo1.AddItem \"c\"\r\n";

    [AvaloniaTheory]
    [InlineData("List1.ListIndex = 2", "2", 0)]
    [InlineData("List1.ListIndex = -1", "-1", 0)]                // nothing selected
    [InlineData("List1.ListIndex = \"1\"", "1", 0)]
    [InlineData("List1.ListIndex = True", "-1", 0)]              // True is -1, which is in range
    [InlineData("List1.ListIndex = Empty", "0", 0)]
    [InlineData("List1.ListIndex = 1.5", "2", 0)]
    [InlineData("List1.ListIndex = 2.5", "2", 0)]
    [InlineData("List1.ListIndex = 3", "0", 380)]
    [InlineData("List1.ListIndex = \"3\"", "0", 380)]
    [InlineData("List1.ListIndex = -2", "0", 380)]
    [InlineData("List1.ListIndex = 40000", "0", 6)]              // an Integer, so Overflow before the range
    [InlineData("List1.ListIndex = Null", "0", 94)]
    [InlineData("List1.ListIndex = \"abc\"", "0", 13)]
    [InlineData("Combo1.ListIndex = 2", "2", 0)]
    [InlineData("Combo1.ListIndex = 3", "0", 380)]
    [InlineData("Combo1.ListIndex = -2", "0", 380)]
    [InlineData("Combo1.ListIndex = \"1\"", "1", 0)]
    public async Task ListIndex_converts_as_an_Integer_and_refuses_what_is_not_an_item(string assignment, string expected, int expectedErr)
    {
        // Three items in each, reset to the first. Avalonia would drop an index past the end rather than refuse it.
        var target = assignment[..assignment.IndexOf(" =", StringComparison.Ordinal)];

        var (value, err) = await Assign(ThreeItems + target + " = 0", assignment, target);

        err.Should().Be(expectedErr);
        value.Should().Be(expected);
    }

    [AvaloniaTheory]
    [InlineData("0", 380)]
    [InlineData("-1", 0)]
    [InlineData("-2", 380)]
    public async Task An_empty_list_takes_only_minus_one(string index, int expectedErr)
    {
        var (value, err) = await Assign("Err.Clear", $"List2.ListIndex = {index}", "List2.ListIndex");

        err.Should().Be(expectedErr);
        value.Should().Be("-1");
    }

    // ── a Boolean property converts as CBool does ─────────────────────────────────────────────────

    [AvaloniaTheory]
    [InlineData("Command1.Enabled = Empty", "False", 0)]
    [InlineData("Command1.Enabled = 0", "False", 0)]
    [InlineData("Command1.Enabled = Null", "True", 94)]
    [InlineData("Command1.Enabled = \"\"", "True", 13)]
    [InlineData("Command1.Enabled = \"NaN\"", "True", 13)]      // CBool("NaN") is 13, measured; it used to be True
    public async Task Enabled_converts_as_CBool(string assignment, string expected, int expectedErr)
    {
        var (value, err) = await Assign("Command1.Enabled = True", assignment, "Command1.Enabled");

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
    [InlineData("\"2\"", "2", 0)]
    [InlineData("1", "1", 0)]
    [InlineData("2", "2", 0)]
    [InlineData("3", "3", 0)]
    [InlineData("4", "4", 0)]
    [InlineData("5", "5", 0)]
    [InlineData("6", "6", 0)]
    [InlineData("7", "7", 0)]
    [InlineData("8", "8", 0)]
    [InlineData("9", "9", 0)]
    [InlineData("10", "10", 0)]
    [InlineData("11", "11", 0)]
    [InlineData("12", "12", 0)]
    [InlineData("13", "13", 0)]
    [InlineData("14", "14", 0)]
    [InlineData("15", "15", 0)]
    [InlineData("99", "99", 0)]
    [InlineData("16", "2", 380)]
    [InlineData("17", "2", 380)]
    [InlineData("98", "2", 380)]
    [InlineData("-1", "2", 380)]
    [InlineData("True", "2", 380)]
    [InlineData("40000", "2", 6)]
    public async Task MousePointer_reads_back_the_value_set(string value, string expected, int expectedErr)
    {
        var (actual, err) = await Assign("Label1.MousePointer = 2", $"Label1.MousePointer = {value}", "Label1.MousePointer");

        err.Should().Be(expectedErr);
        actual.Should().Be(expected);
    }

    [AvaloniaFact]
    public async Task MousePointer_keeps_the_loaded_value_and_can_reset_to_Default()
    {
        var form = Form.Replace("Begin VB.Label Label1", "Begin VB.Label Label1\r\n      MousePointer = 2");
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(form);
        var debug = await RunInOracleLocale(ctx, env,
            "Debug.Print Label1.MousePointer\r\nLabel1.MousePointer = 0\r\nDebug.Print Label1.MousePointer\r\n");

        Convert.ToInt32(debug[0].Value).Should().Be(2);
        Convert.ToInt32(debug[1].Value).Should().Be(0);
    }

    // ── a colour converts as a Long, then refuses what is not an OLE colour (380) ─────────────────

    [AvaloniaTheory]
    [InlineData("\"255\"", 0, 0xFF)]
    [InlineData("255.6", 0, 0x100)]
    [InlineData("\"&HFF\"", 0, 0xFF)]
    [InlineData("&HFFFFFF", 0, 0xFFFFFF)]
    [InlineData("&H80000005", 0, unchecked((int)0x80000005))]     // a system colour
    [InlineData("&H80000018", 0, unchecked((int)0x80000018))]     // the last one
    [InlineData("\"-2147483633\"", 0, unchecked((int)0x8000000F))]
    [InlineData("&H80000019", 380, 0)]
    [InlineData("&H8000001A", 380, 0)]
    [InlineData("&H80000020", 380, 0)]
    [InlineData("&H1010000", 380, 0)]      // past the last palette index
    [InlineData("&H3000000", 380, 0)]
    [InlineData("&HFF00", 380, 0)]         // an Integer literal, so -256
    [InlineData("-2", 380, 0)]
    [InlineData("True", 380, 0)]
    [InlineData("1E+20", 6, 0)]
    [InlineData("Null", 94, 0)]
    public async Task BackColor_accepts_what_OLE_accepts_as_a_colour(string value, int expectedErr, int expectedOle)
    {
        // The colour is read from the control: VB6 code reads it back as HexIDE's own colour value rather than
        // VB6's Long (#789). A refused value leaves the reset colour, 0, in place.
        var (_, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(Form);

        var debug = await RunInOracleLocale(ctx, env,
            $"On Error Resume Next\r\nText1.BackColor = 0\r\nErr.Clear\r\nText1.BackColor = {value}\r\nDebug.Print Err.Number\r\n");

        debug[0].Value.Should().Be(expectedErr);
        ColourOf(canvas, "Text1").Should().Be(VBColor.FromOle(expectedOle));
    }

    [AvaloniaTheory]
    [InlineData("&H1000000")]           // a palette index
    [InlineData("&H100FFFF")]           // the last one
    [InlineData("&H2FFFFFF")]           // a palette-relative RGB
    [InlineData("\"&H1000001\"")]       // as a String
    [InlineData("16777217#")]           // as a Double
    public async Task A_palette_colour_is_refused_rather_than_stored_as_another_colour(string value)
    {
        // VB6 keeps it as given (&H1000001 reads back 16777217). HexIDE has no colour that holds one, and used to
        // store the RGB of its low bytes, a different colour.
        (await Refusal($"Text1.BackColor = {value}")).Should().Contain("#786");
    }

    // The source colours below come from the .frm, or are the control's default, with nothing assigned at run time
    // first: a run-time assignment would hide a colour the control failed to load. A Label's and a TextBox's did,
    // until #795.

    [AvaloniaTheory]
    [InlineData("Text1.BackColor = Picture1.BackColor", "Text1", "BackColor", 0xFF)]
    [InlineData("Text1.BackColor = Picture2.BackColor", "Text1", "BackColor", unchecked((int)0x8000000F))]
    [InlineData("Text1.ForeColor = Picture1.ForeColor", "Text1", "ForeColor", 0xFF0000)]
    [InlineData("Dim saved As Variant\r\nsaved = Picture1.BackColor\r\nPicture1.BackColor = 0\r\nPicture1.BackColor = saved", "Picture1", "BackColor", 0xFF)]
    [InlineData("Picture1.BackColor = Label1.BackColor", "Picture1", "BackColor", 0xFF00)]
    [InlineData("Picture1.BackColor = Text1.BackColor", "Picture1", "BackColor", unchecked((int)0x80000005))]
    [InlineData("Text1.ForeColor = Label1.ForeColor", "Text1", "ForeColor", unchecked((int)0x80000012))]
    public async Task A_colour_from_the_form_can_be_copied_or_saved_and_restored(string code, string target, string property, int expectedOle)
    {
        var (_, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(Form);

        var debug = await RunInOracleLocale(ctx, env, "On Error Resume Next\r\n" + code + "\r\nDebug.Print Err.Number\r\n");

        debug[0].Value.Should().Be(0);
        ColourOf(canvas, target, property == "ForeColor" ? VBProperties.ForeColorProperty : null)
            .Should().Be(VBColor.FromOle(expectedOle));
    }

    [AvaloniaFact]
    public async Task A_colour_shown_as_text_is_its_Long()
    {
        // Measured: a system colour reads back as a negative Long, and CStr of it is that number.
        var (value, err) = await Assign("Label1.Caption = \"\"", "Label1.Caption = Picture2.BackColor", "Label1.Caption");

        err.Should().Be(0);
        value.Should().Be("-2147483633");
    }

    [AvaloniaTheory]
    [InlineData("Label1.Caption = Text1.BackColor", "-2147483643")]   // a TextBox's default, measured
    [InlineData("Label1.Caption = Text1.ForeColor", "-2147483640")]
    [InlineData("Label1.Caption = Label1.BackColor", "65280")]        // from the form
    [InlineData("Label1.Caption = Label1.ForeColor", "-2147483630")]  // a Label's default, measured
    [InlineData("Label1.Caption = List1.ForeColor", "-2147483640")]   // a list's: the window text, measured
    [InlineData("Label1.Caption = List1.BackColor", "-2147483643")]
    [InlineData("Label1.Caption = Combo1.ForeColor", "-2147483640")]
    [InlineData("Label1.Caption = Combo1.BackColor", "-2147483643")]
    public async Task A_Labels_or_TextBoxs_colour_from_the_form_reads_back_as_its_Long(string assignment, string expected)
    {
        // These were refused until #795, because neither control held its form colours where a read could find them.
        var (value, err) = await Assign("Label1.Caption = \"\"", assignment, "Label1.Caption");

        err.Should().Be(0);
        value.Should().Be(expected);
    }

    [AvaloniaTheory]
    [InlineData("Dim c\r\nc = HScroll1.ForeColor")]
    [InlineData("Dim c\r\nc = HScroll1.BackColor")]
    [InlineData("HScroll1.ForeColor = vbRed")]
    [InlineData("HScroll1.BackColor = vbRed")]
    public async Task A_colour_on_a_control_VB6_gives_none_is_refused_rather_than_read_as_black(string code)
    {
        (await Refusal(code)).Should().Contain("#801");
    }

    [AvaloniaTheory]
    [InlineData("Dim v\nv = Timer1.ForeColor")]
    [InlineData("Timer1.Parent = 5")]
    [InlineData("Dim p As Object\nSet p = Timer1.Parent")]
    [InlineData("Set Timer1.Parent = Nothing")]
    [InlineData("Dim o As Object\nSet o = Timer1\nSet o.Parent = Nothing")]
    public async Task Unmeasured_Timer_members_refuse_explicitly(string code)
    {
        (await Refusal(code)).Should().Contain("#801");
    }

    [AvaloniaFact]
    public async Task A_colour_the_program_has_set_reads_back_as_set()
    {
        var (value, err) = await Assign("Text1.BackColor = &HFF&", "Label1.Caption = Text1.BackColor", "Label1.Caption");

        err.Should().Be(0);
        value.Should().Be("255");
    }

    // ── what is not a value to convert fails clearly rather than storing something wrong ──────────

    [AvaloniaTheory]
    [InlineData("Label1.Caption = Text1", "Caption")]
    [InlineData("Command1.Enabled = Text1", "Enabled")]      // ahead of CBool, whose 13 Resume Next would step over
    [InlineData("Label1.Left = Text1", "Left")]
    [InlineData("Label1.Tag = Text1", "Tag")]                // Tag keeps what it is given, but not a reference
    [InlineData("Dim a(2) As Integer\r\nLabel1.Caption = a", "Caption")]
    public async Task A_control_an_array_or_an_object_on_the_right_is_not_converted(string code, string property)
    {
        // VB6 reads a control's default property here. HexIDE does not implement that, and must not store the
        // reference's .NET type name as the caption instead.
        (await Refusal(code)).Should().Contain(property);
    }

    [Fact]
    public void An_object_reference_is_not_converted()
    {
        // Asked of the conversion directly: in this harness VB code has no object to hand over. New makes only a
        // class module's instances, and `Label1.Caption = o` with o set to Nothing never reaches the conversion.
        var act = () => AvaloniaInteroperability.ConvertForAssignment(VBProperties.CaptionProperty, Vb6Value.Nothing);

        act.Should().Throw<NotImplementedException>().Which.Message.Should().Contain("Caption");
    }
}
