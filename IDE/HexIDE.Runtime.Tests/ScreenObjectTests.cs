using HexIDE.IDE;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The global <c>Screen</c> object (#2431). Every expectation was measured on vb6.exe, native and p-code agreeing, on a 1024 by
/// 768 desktop: the oracle's "The Screen object". These programs run with no form, so the members a form answers are read
/// here only as Nothing; the Integration tests read them with forms shown.
/// </summary>
public class ScreenObjectTests : BaseVBTestFixture
{
    private void Printed(params string[] lines) => AssertDebugLog([.. lines.Select(line => new Vb6Value(line))]);

    [Fact]
    public async Task TypeName_of_Screen_is_Screen()
    {
        await Run("""Debug.Print TypeName(Screen)""");
        Printed("Screen");
    }

    [Fact]
    public async Task Width_and_Height_are_the_screen_in_twips_as_Singles()
    {
        await Run("""
            Debug.Print TypeName(Screen.Width) & " " & Screen.Width
            Debug.Print TypeName(Screen.Height) & " " & Screen.Height
            """);
        Printed("Single 15360", "Single 11520");
    }

    [Fact]
    public async Task TwipsPerPixel_is_a_Single_15_both_ways()
    {
        await Run("""
            Debug.Print TypeName(Screen.TwipsPerPixelX) & " " & Screen.TwipsPerPixelX
            Debug.Print TypeName(Screen.TwipsPerPixelY) & " " & Screen.TwipsPerPixelY
            """);
        Printed("Single 15", "Single 15");
    }

    [Fact]
    public async Task A_form_centred_with_Screen_Width_gets_the_measured_position()
    {
        // The use that made Screen an F5 cliff: centring a 4800-twip form on the screen. A Single less an Integer, halved,
        // stays a Single.
        await Run("""Debug.Print (Screen.Width - 4800) / 2, (Screen.Height - 3600) / 2""");
        AssertDebugLog([new Vb6Value(5280f), new Vb6Value(3960f)]);
    }

    [Fact]
    public async Task MousePointer_is_an_Integer_0_until_set()
    {
        await Run("""Debug.Print TypeName(Screen.MousePointer) & " " & Screen.MousePointer""");
        Printed("Integer 0");
    }

    [Theory]
    [InlineData("11", "0 11")]
    [InlineData("0", "0 0")]
    [InlineData("1", "0 1")]
    [InlineData("15", "0 15")]
    [InlineData("99", "0 99")]
    [InlineData("vbHourglass", "0 11")]
    [InlineData("16", "380 0")]
    [InlineData("-1", "380 0")]
    [InlineData("100", "380 0")]
    [InlineData("True", "380 0")]
    [InlineData("1.6", "0 2")]
    [InlineData("2.5", "0 2")]
    [InlineData("3.5", "0 4")]
    [InlineData("\"11\"", "0 11")]
    [InlineData("\"abc\"", "13 0")]
    [InlineData("32768", "6 0")]
    [InlineData("1E+100", "6 0")]
    public async Task MousePointer_takes_what_a_controls_takes_and_keeps_its_value_on_a_refusal(string written, string expected)
    {
        await Run($"""
            On Error Resume Next
            Screen.MousePointer = {written}
            Debug.Print Err.Number & " " & Screen.MousePointer
            """);
        Printed(expected);
    }

    [Fact]
    public async Task ActiveForm_and_ActiveControl_are_Nothing_with_no_form_and_no_error()
    {
        await Run("""
            Debug.Print TypeName(Screen.ActiveForm) & " " & (Screen.ActiveForm Is Nothing)
            Debug.Print TypeName(Screen.ActiveControl) & " " & (Screen.ActiveControl Is Nothing)
            """);
        Printed("Nothing True", "Nothing True");
    }

    [Fact]
    public async Task A_member_of_ActiveForm_with_no_form_active_is_91()
    {
        await Run("""
            On Error Resume Next
            Dim v As Variant
            v = Screen.ActiveForm.Caption
            Debug.Print CStr(Err.Number)
            """);
        Printed("91");
    }

    [Fact]
    public async Task FontCount_is_an_Integer_and_Fonts_are_Strings_in_the_hosts_order()
    {
        await Run("""
            Debug.Print TypeName(Screen.FontCount) & " " & Screen.FontCount
            Debug.Print TypeName(Screen.Fonts(0)) & " " & Screen.Fonts(0)
            Debug.Print Screen.Fonts(Screen.FontCount - 1)
            """);
        Printed("Integer 4", "String System", "Times New Roman");
    }

    [Fact]
    public async Task A_Fonts_index_past_either_end_is_an_empty_String_with_no_error()
    {
        await Run("""
            Debug.Print "[" & Screen.Fonts(Screen.FontCount) & "]"
            Debug.Print "[" & Screen.Fonts(-1) & "]"
            """);
        Printed("[]", "[]");
    }

    [Theory]
    [InlineData("1.5", "0 Courier New")]
    [InlineData("2.5", "0 Courier New")]
    [InlineData("\"1\"", "0 Arial")]
    [InlineData("40000", "6 ")]
    [InlineData("\"abc\"", "13 ")]
    [InlineData("Null", "94 ")]
    public async Task A_Fonts_index_is_converted_as_an_Integer(string index, string expected)
    {
        await Run($"""
            On Error Resume Next
            Dim v As Variant
            v = Screen.Fonts({index})
            Debug.Print Err.Number & " " & v
            """);
        Printed(expected);
    }

    [Theory]
    [InlineData("s.Width = 100", "382 'Width' property cannot be set at run time")]
    [InlineData("s.Height = 100", "382 'Height' property cannot be set at run time")]
    [InlineData("s.FontCount = 1", "382 'FontCount' property cannot be set at run time")]
    [InlineData("Set s.ActiveForm = Nothing", "382 'ActiveForm' property cannot be set at run time")]
    [InlineData("s.TwipsPerPixelX = 1", "383 'TwipsPerPixelX' property is read-only")]
    [InlineData("s.TwipsPerPixelY = 1", "383 'TwipsPerPixelY' property is read-only")]
    [InlineData("s.NoSuch = 1", "438 Object doesn't support this property or method")]
    [InlineData("v = s.NoSuch", "438 Object doesn't support this property or method")]
    public async Task A_write_through_an_Object_to_a_member_a_program_cannot_write_is_refused_by_name(string statement, string expected)
    {
        await Run($"""
            On Error Resume Next
            Dim s As Object, v As Variant
            Set s = Screen
            {statement}
            Debug.Print Err.Number & " " & Err.Description
            """);
        Printed(expected);
    }

    [Fact]
    public async Task A_variable_declared_As_Screen_holds_it()
    {
        await Run("""
            Dim s As Screen
            Set s = Screen
            Debug.Print TypeName(s) & " " & s.Width
            """);
        Printed("Screen 15360");
    }

    [Fact]
    public async Task MouseIcon_is_refused_rather_than_guessed()
    {
        var run = () => Run("""Debug.Print TypeName(Screen.MouseIcon)""");
        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*MouseIcon*#2431*");
    }

    private sealed class NoScreen(List<Vb6Value> log) : IBasicStandardLibrary
    {
        public Task<MessageBoxResult> MsgBox(string t, string? c, MessageBoxButtons b, MessageBoxIcon i) =>
            Task.FromResult(MessageBoxResult.Ok);
        public Task<string?> InputBox(string p, string? t, string d) => Task.FromResult<string?>("");
        public void DebugPrint(Vb6Value value) => log.Add(value);
    }

    [Fact]
    public async Task A_host_with_no_screen_refuses_its_size_and_still_keeps_the_pointer()
    {
        var log = new List<Vb6Value>();
        var vb = new BasicInterpreter(new NoScreen(log), new ModuleExecutionContext(), new ExecutionEnvironment(),
            "Screen.MousePointer = 11\nDebug.Print Screen.MousePointer\nDebug.Print Screen.Width");
        var run = () => vb.Execute();
        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*no screen*#2431*");
        log.Should().Equal(new Vb6Value((short)11));
    }
}
