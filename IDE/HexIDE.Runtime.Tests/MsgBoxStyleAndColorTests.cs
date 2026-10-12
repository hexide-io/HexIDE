using HexIDE.IDE;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// MsgBox's style read as the Long it is, and RGB and QBColor. Every expectation was measured on vb6.exe; the probe
/// and its output are in docs/vb6-fidelity-oracle.md, "RGB, QBColor and MsgBox's style".
/// </summary>
public class MsgBoxStyleAndColorTests : BaseVBTestFixture
{
    /// <summary>Records what MsgBox asked the host to show, and answers with a chosen button.</summary>
    private sealed class Recording(MessageBoxResult answer) : IBasicStandardLibrary
    {
        public List<(MessageBoxButtons Buttons, MessageBoxIcon Icon)> Shown { get; } = [];
        public List<Vb6Value> Printed { get; } = [];

        public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons b, MessageBoxIcon i)
        {
            Shown.Add((b, i));
            return Task.FromResult(answer);
        }

        public Task<string?> InputBox(string p, string? t, string d) => Task.FromResult<string?>("");
        public void DebugPrint(Vb6Value value) => Printed.Add(value);
    }

    private static async Task<Recording> RunMsgBox(string code, MessageBoxResult answer = MessageBoxResult.Ok)
    {
        var lib = new Recording(answer);
        await new BasicInterpreter(lib, new ModuleExecutionContext(), new ExecutionEnvironment(), code).Execute();
        return lib;
    }

    // ── MsgBox's style ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("vbYesNo + vbQuestion", MessageBoxButtons.YesNo, MessageBoxIcon.Question)]
    [InlineData("vbOKCancel", MessageBoxButtons.OkCancel, MessageBoxIcon.None)]
    [InlineData("4&", MessageBoxButtons.YesNo, MessageBoxIcon.None)]
    [InlineData("36", MessageBoxButtons.YesNo, MessageBoxIcon.Question)]
    [InlineData("\"4\"", MessageBoxButtons.YesNo, MessageBoxIcon.None)]
    [InlineData("4.5", MessageBoxButtons.YesNo, MessageBoxIcon.None)]                // banker's rounding: 4
    [InlineData("2.5", MessageBoxButtons.AbortRetryIgnore, MessageBoxIcon.None)]     // and 2
    [InlineData("Empty", MessageBoxButtons.Ok, MessageBoxIcon.None)]
    [InlineData("vbAbortRetryIgnore + vbCritical", MessageBoxButtons.AbortRetryIgnore, MessageBoxIcon.Error)]
    [InlineData("vbYesNoCancel + vbExclamation", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning)]
    [InlineData("vbRetryCancel + vbInformation", MessageBoxButtons.RetryCancel, MessageBoxIcon.Information)]
    [InlineData("16", MessageBoxButtons.Ok, MessageBoxIcon.Error)]
    [InlineData("17", MessageBoxButtons.OkCancel, MessageBoxIcon.Error)]
    // Any field out of range is a plain OK box with no icon, whatever the other fields say.
    [InlineData("70000", MessageBoxButtons.Ok, MessageBoxIcon.None)]      // icon field 112
    [InlineData("70004", MessageBoxButtons.Ok, MessageBoxIcon.None)]      // vbYesNo beside it
    [InlineData("-1", MessageBoxButtons.Ok, MessageBoxIcon.None)]
    [InlineData("6", MessageBoxButtons.Ok, MessageBoxIcon.None)]          // Windows' Cancel/Try Again/Continue
    [InlineData("7", MessageBoxButtons.Ok, MessageBoxIcon.None)]
    [InlineData("8", MessageBoxButtons.Ok, MessageBoxIcon.None)]
    [InlineData("9", MessageBoxButtons.Ok, MessageBoxIcon.None)]
    [InlineData("12", MessageBoxButtons.Ok, MessageBoxIcon.None)]         // the low nibble, not 4's Yes/No
    [InlineData("13", MessageBoxButtons.Ok, MessageBoxIcon.None)]
    [InlineData("15", MessageBoxButtons.Ok, MessageBoxIcon.None)]
    [InlineData("22", MessageBoxButtons.Ok, MessageBoxIcon.None)]         // 6 + vbCritical: no icon either
    [InlineData("38", MessageBoxButtons.Ok, MessageBoxIcon.None)]         // 6 + vbQuestion
    [InlineData("84", MessageBoxButtons.Ok, MessageBoxIcon.None)]         // vbYesNo with icon 80
    [InlineData("116", MessageBoxButtons.Ok, MessageBoxIcon.None)]        // vbYesNo with icon 112
    [InlineData("1028", MessageBoxButtons.Ok, MessageBoxIcon.None)]       // vbYesNo with default button 1024
    // Within range, the default button and the flags above it leave the buttons alone.
    [InlineData("260", MessageBoxButtons.YesNo, MessageBoxIcon.None)]     // vbDefaultButton2
    [InlineData("772", MessageBoxButtons.YesNo, MessageBoxIcon.None)]     // vbDefaultButton4
    [InlineData("4100", MessageBoxButtons.YesNo, MessageBoxIcon.None)]    // vbSystemModal
    [InlineData("65540", MessageBoxButtons.YesNo, MessageBoxIcon.None)]   // vbMsgBoxSetForeground
    [InlineData("524292", MessageBoxButtons.YesNo, MessageBoxIcon.None)]  // vbMsgBoxRight
    public async Task The_style_selects_the_buttons_and_icon_VB6_shows(string style, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        // A Long style used to be read as 0, so every in-box constant, all of which are Longs, showed OK alone.
        var lib = await RunMsgBox($"MsgBox \"probe\", {style}, \"ProbeBox\"\n");

        lib.Shown.Should().ContainSingle().Which.Should().Be((buttons, icon));
    }

    [Fact]
    public async Task An_omitted_style_is_OK_alone()
    {
        var lib = await RunMsgBox("MsgBox \"probe\", , \"ProbeBox\"\n");

        lib.Shown.Should().ContainSingle().Which.Should().Be((MessageBoxButtons.Ok, MessageBoxIcon.None));
    }

    [Theory]
    [InlineData("Null", 94)]
    [InlineData("\"abc\"", 13)]
    public async Task A_style_that_is_not_a_number_raises_before_any_dialog(string style, long error)
    {
        var lib = await RunMsgBox($"On Error Resume Next\nMsgBox \"probe\", {style}, \"ProbeBox\"\nDebug.Print Err.Number\n");

        lib.Shown.Should().BeEmpty();
        lib.Printed.Should().Equal(new Vb6Value(error));
    }

    [Theory]
    [InlineData(MessageBoxResult.Yes, 6L, "yes branch")]
    [InlineData(MessageBoxResult.No, 7L, "no branch")]
    public async Task The_answer_is_a_Long_and_chooses_the_branch(MessageBoxResult answer, long value, string branch)
    {
        var lib = await RunMsgBox(
            "Dim r\nr = MsgBox(\"probe\", vbYesNo + vbQuestion, \"ProbeBox\")\nDebug.Print r\nDebug.Print TypeName(r)\n"
            + "If r = vbYes Then\n    Debug.Print \"yes branch\"\nElse\n    Debug.Print \"no branch\"\nEnd If\n",
            answer);

        lib.Printed.Should().Equal(new Vb6Value(value), new Vb6Value("Long"), new Vb6Value(branch));
    }

    [Theory]
    [InlineData("vbOKOnly", 1L)]
    [InlineData("vbOKCancel", 2L)]
    [InlineData("vbYesNoCancel", 2L)]
    [InlineData("vbRetryCancel", 2L)]
    public async Task A_box_closed_without_a_button_answers_as_VB6s_does(string style, long expected)
    {
        // It answered vbOK for every kind, so closing an OK/Cancel box took the program's OK branch.
        var lib = await RunMsgBox($"Debug.Print MsgBox(\"probe\", {style}, \"ProbeBox\")\n", MessageBoxResult.None);

        lib.Printed.Should().Equal(new Vb6Value(expected));
    }

    [Theory]
    [InlineData("vbYesNo")]
    [InlineData("vbAbortRetryIgnore")]
    public async Task A_box_with_no_close_that_goes_away_unanswered_while_the_run_goes_on_is_a_fault(string style)
    {
        // VB6 offers no close here, the hosts refuse a person's, and a box hidden with its owner keeps waiting. A box
        // that goes away anyway, with the run still going, has no right answer, and carrying on or stopping in silence
        // would both be wrong. A fault ends the run and says so.
        var run = () => RunMsgBox($"Debug.Print MsgBox(\"probe\", {style}, \"ProbeBox\")\nDebug.Print \"after\"\n",
            MessageBoxResult.None);

        await run.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(VBMsgBoxStyle.vbYesNo)]
    [InlineData(VBMsgBoxStyle.vbOKCancel)]
    public async Task A_box_that_goes_away_unanswered_as_the_run_ends_abandons_the_statement(VBMsgBoxStyle style)
    {
        // Ending the run closes the box. No answer is right then, and the statement is abandoned, as the run is.
        var builtIns = new VB6BuiltIns(new Recording(MessageBoxResult.None)) { RunEnding = () => true };

        var call = () => builtIns.EvaluateBuiltInFunction("MsgBox", [new Vb6Value("probe"), new Vb6Value((long)style)]);

        await call.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(MessageBoxButtons.Ok, false)]
    [InlineData(MessageBoxButtons.OkCancel, false)]
    [InlineData(MessageBoxButtons.YesNoCancel, false)]
    [InlineData(MessageBoxButtons.RetryCancel, false)]
    [InlineData(MessageBoxButtons.YesNo, true)]
    [InlineData(MessageBoxButtons.AbortRetryIgnore, true)]
    public void A_person_cannot_close_a_box_that_has_no_close(MessageBoxButtons buttons, bool refused)
    {
        Dialogs.MessageBox.RefusesClose(buttons, isProgrammatic: false, Avalonia.Controls.WindowCloseReason.WindowClosing)
            .Should().Be(refused);
        Dialogs.MessageBox.RefusesClose(buttons, isProgrammatic: true, Avalonia.Controls.WindowCloseReason.WindowClosing)
            .Should().BeFalse("HexIDE's own close, ending the run, always goes through");
        Dialogs.MessageBox.RefusesClose(buttons, isProgrammatic: false, Avalonia.Controls.WindowCloseReason.ApplicationShutdown)
            .Should().BeFalse("nor does the session ending");
    }

    // ── An argument that is not a number ─────────────────────────────────────────────────────────

    private const string NonScalars =
        "Dim o As Class1\nDim a(2) As Integer\nDim va\nva = a\nDim k As Class1\nSet k = New Class1\nOn Error Resume Next\n";

    [Theory]
    [InlineData("CInt(o)", 91L)]
    [InlineData("CLng(o)", 91L)]
    [InlineData("RGB(o, 0, 0)", 91L)]
    [InlineData("QBColor(o)", 91L)]
    [InlineData("MsgBox(\"probe\", o)", 91L)]
    [InlineData("CInt(va)", 13L)]
    [InlineData("RGB(va, 0, 0)", 13L)]
    [InlineData("MsgBox(\"probe\", va)", 13L)]
    [InlineData("CInt(k)", 438L)]
    [InlineData("RGB(k, 0, 0)", 438L)]
    [InlineData("MsgBox(\"probe\", k)", 438L)]
    public async Task Nothing_an_array_or_an_object_raises_rather_than_reading_as_0(string expression, long error)
    {
        // Each read as 0 through the numeric narrowing, so RGB(o, 0, 0) was black and MsgBox "x", o an OK box.
        await RunClasses($"{NonScalars}Dim c\nc = {expression}\nDebug.Print Err.Number\nDebug.Print IsEmpty(c)\n",
            ("Class1", "Public Tag As Long\n"));
        AssertDebugLog([new Vb6Value(error), new Vb6Value(true)]);
    }

    [Theory]
    [InlineData("CInt(u)")]
    [InlineData("RGB(u, 0, 0)")]
    [InlineData("MsgBox(\"probe\", u)")]
    public async Task A_user_defined_type_raises_13_rather_than_reading_as_0(string expression)
    {
        // VB6 refuses to compile these ("Type mismatch", measured), so the error comes as the statement runs.
        await Run($"Type Pt\n    X As Integer\nEnd Type\nDim u As Pt\nOn Error Resume Next\nDim c\nc = {expression}\n"
                  + "Debug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(13L)]);
    }

    [Theory]
    [InlineData("CLng")]
    [InlineData("CInt")]
    public async Task An_omitted_Optional_argument_converts_to_448(string conversion)
    {
        // The error value VB6 passes for a missing argument, whose number is 448 (measured: CLng and CInt of one are
        // both 448, with no error). It read as 0, so this Area() was 0.
        await Run($"Function Area(Optional w) As Long\n    Area = {conversion}(w) * 10\nEnd Function\n"
                  + "Debug.Print Area()\nDebug.Print Area(3)\n");
        AssertDebugLog([new Vb6Value(4480L), new Vb6Value(30L)]);
    }

    [Fact]
    public async Task A_class_with_a_default_member_is_refused_until_class_default_members_exist()
    {
        // Measured: CInt(k) is 42 where the class's VB_UserMemId = 0 member returns 42. HexIDE does not call it yet
        // (#174), and a trappable 438 would let On Error Resume Next carry on without VB6's value.
        var run = () => RunClasses("Dim k As Answer\nSet k = New Answer\nOn Error Resume Next\nDim c\nc = CInt(k)\n",
            ("Answer", "Public Function Value() As Long\nAttribute Value.VB_UserMemId = 0\n    Value = 42\nEnd Function\n"));

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#174*");
    }

    [Theory]
    [InlineData("CInt(Err)")]
    [InlineData("RGB(Err, 0, 0)")]
    public async Task An_object_standing_for_its_default_member_is_refused_until_default_members_exist(string expression)
    {
        // VB6 reads the default member, CInt(Text1) as CInt(Text1.Text). HexIDE has none yet (#790), so it refuses
        // untrappably rather than read 0.
        var run = () => Run($"On Error Resume Next\nDim c\nc = {expression}\n");

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#790*");
    }

    // ── RGB ──────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("RGB(1, 0, 0)", 1L)]
    [InlineData("RGB(255, 255, 255)", 16777215L)]
    [InlineData("RGB(256, 0, 0)", 255L)]          // above 255 is 255
    [InlineData("RGB(1.5, 2.5, 0)", 514L)]        // banker's rounding: 2 and 2
    [InlineData("RGB(\"10\", 0, 0)", 10L)]
    [InlineData("RGB(Empty, 0, 0)", 0L)]
    public async Task RGB_is_red_plus_256_green_plus_65536_blue_as_a_Long(string expression, long expected)
    {
        await Run($"Debug.Print {expression}\nDebug.Print TypeName({expression})\n");
        AssertDebugLog([new Vb6Value(expected), new Vb6Value("Long")]);
    }

    [Theory]
    [InlineData("RGB(-1, 0, 0)", 5L)]
    [InlineData("RGB(0, -1, 0)", 5L)]
    [InlineData("RGB(40000, 0, 0)", 6L)]          // each component is an Integer first
    [InlineData("RGB(300, 1000, 70000)", 6L)]
    [InlineData("RGB(3000000000#, 0, 0)", 6L)]
    [InlineData("RGB(\"x\", 0, 0)", 13L)]
    [InlineData("RGB(Null, 0, 0)", 94L)]
    // All three are coerced before any is range-checked, the last first.
    [InlineData("RGB(-1, 40000, 0)", 6L)]
    [InlineData("RGB(40000, -1, 0)", 6L)]
    [InlineData("RGB(0, -1, 40000)", 6L)]
    [InlineData("RGB(\"x\", -1, 0)", 13L)]
    [InlineData("RGB(-1, \"x\", 0)", 13L)]
    [InlineData("RGB(Null, \"x\", 0)", 13L)]     // not 94: the second argument was coerced first
    [InlineData("RGB(300, -1, 0)", 5L)]
    public async Task RGB_raises_what_VB6_raises(string expression, long error)
    {
        await Run($"On Error Resume Next\nDim c\nc = {expression}\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(error)]);
    }

    // ── QBColor ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0L)]
    [InlineData(1, 8388608L)]
    [InlineData(2, 32768L)]
    [InlineData(3, 8421376L)]
    [InlineData(4, 128L)]
    [InlineData(5, 8388736L)]
    [InlineData(6, 32896L)]
    [InlineData(7, 12632256L)]
    [InlineData(8, 8421504L)]
    [InlineData(9, 16711680L)]
    [InlineData(10, 65280L)]
    [InlineData(11, 16776960L)]
    [InlineData(12, 255L)]
    [InlineData(13, 16711935L)]
    [InlineData(14, 65535L)]
    [InlineData(15, 16777215L)]
    public async Task QBColor_is_the_measured_colour_as_a_Long(int n, long expected)
    {
        await Run($"Debug.Print QBColor({n})\nDebug.Print TypeName(QBColor({n}))\n");
        AssertDebugLog([new Vb6Value(expected), new Vb6Value("Long")]);
    }

    [Theory]
    [InlineData("QBColor(7.5)", 8421504L)]        // banker's rounding: 8
    [InlineData("QBColor(8.5)", 8421504L)]        // and 8
    [InlineData("QBColor(\"3\")", 8421376L)]
    public async Task QBColor_coerces_its_argument_as_CInt_does(string expression, long expected)
    {
        await Run($"Debug.Print {expression}\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("QBColor(16)", 5L)]
    [InlineData("QBColor(-1)", 5L)]
    [InlineData("QBColor(Null)", 94L)]
    [InlineData("QBColor(40000)", 6L)]            // overflow as an Integer, before the range check
    public async Task QBColor_raises_what_VB6_raises(string expression, long error)
    {
        await Run($"On Error Resume Next\nDim c\nc = {expression}\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(error)]);
    }
}
