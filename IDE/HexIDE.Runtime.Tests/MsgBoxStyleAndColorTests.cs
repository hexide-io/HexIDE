using HexIDE.IDE;
using HexIDE.Runtime.Interpreter;

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
    [InlineData("70000", MessageBoxButtons.Ok, MessageBoxIcon.None)]
    [InlineData("-1", MessageBoxButtons.Ok, MessageBoxIcon.None)]
    [InlineData("7", MessageBoxButtons.Ok, MessageBoxIcon.None)]
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

    [Fact]
    public async Task The_answer_is_a_Long_and_the_Yes_branch_is_reachable()
    {
        var lib = await RunMsgBox(
            "Dim r\nr = MsgBox(\"probe\", vbYesNo + vbQuestion, \"ProbeBox\")\nDebug.Print TypeName(r)\nIf r = vbYes Then Debug.Print \"yes\"\n",
            MessageBoxResult.Yes);

        lib.Printed.Should().Equal(new Vb6Value("Long"), new Vb6Value("yes"));
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
    public async Task QBColor_raises_what_VB6_raises(string expression, long error)
    {
        await Run($"On Error Resume Next\nDim c\nc = {expression}\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(error)]);
    }
}
