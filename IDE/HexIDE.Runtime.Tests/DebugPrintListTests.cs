using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// <c>Debug.Print</c> with several items, separators and <c>Spc</c> and <c>Tab</c> (#1380). Every line is what VB6's
/// Immediate window showed for the statement, copied out of the IDE, and the same as <c>Print #</c> wrote to a file in a
/// compiled probe, native and p-code (the oracle's "Sequential files", under "Debug.Print is the same Print").
/// </summary>
public class DebugPrintListTests : BaseVBTestFixture
{
    private string[] Lines()
    {
        var text = written.ToString();
        text.Should().EndWith("\r\n", "every case here ends its line");
        return text[..^2].Split("\r\n");
    }

    [Theory]
    [InlineData("Debug.Print 1; \"a\"; 2", " 1 a 2 ")]
    [InlineData("Debug.Print 1, 2", " 1             2 ")]
    [InlineData("Debug.Print \"x\";\nDebug.Print \"y\"", "xy")]
    [InlineData("Debug.Print \"p\",\nDebug.Print \"q\"", "p             q")]
    [InlineData("Debug.Print -1; -2.5; 3", "-1 -2.5  3 ")]
    [InlineData("Debug.Print True; False", "TrueFalse")]
    [InlineData("Debug.Print Null; Empty; \"e\"", "Nulle")]
    [InlineData("Debug.Print \"s\"; \"t\"", "st")]
    [InlineData("Debug.Print \"a\", \"b\", \"c\"", "a             b             c")]
    [InlineData("Debug.Print \"123456789012345\", \"z\"", "123456789012345             z")]
    [InlineData("Debug.Print Spc(3); \"x\"; Tab(10); \"y\"", "   x     y")]
    [InlineData("Debug.Print Tab(5); \"x\"", "    x")]
    [InlineData("Debug.Print , \"lead\"", "              lead")]
    [InlineData("Debug.Print CCur(1.5); CSng(0.1); CDbl(1E+20)", " 1.5  0.1  1E+20 ")]
    [InlineData("Debug.Print", "")]
    [InlineData("Debug.Print 1.5E-10", " 0.00000000015 ")]
    [InlineData("Debug.Print \"a\"; Tab; \"b\"", "a             b")]
    [InlineData("Debug.Print \"a\"; Spc(0); \"b\"", "ab")]
    [InlineData("Debug.Print 1;\nDebug.Print 2", " 1  2 ")]
    [InlineData("Debug.Print CByte(5); CLng(-7); CInt(0)", " 5 -7  0 ")]
    [InlineData("Debug.Print \"a\",\nDebug.Print ,\nDebug.Print \"b\"", "a                           b")]
    // Separators alone (#1409).
    [InlineData("Debug.Print \"a\";\nDebug.Print ;\nDebug.Print \"b\"", "ab")]
    [InlineData("Debug.Print ;\nDebug.Print \"x\"", "x")]
    [InlineData("Debug.Print , ,\nDebug.Print \"y\"", "                            y")]
    [InlineData("Debug.Print \"a\";\nDebug.Print ;;\nDebug.Print \"b\"", "ab")]
    [InlineData("Debug.Print ,;\nDebug.Print \"z\"", "              z")]
    [InlineData("Debug.Print ;,\nDebug.Print \"w\"", "              w")]
    [InlineData("Debug.Print \"abc\";\nDebug.Print ,\nDebug.Print \"v\"", "abc           v")]
    [InlineData("Debug.Print ;\"u\"", "u")]
    [InlineData("Debug.Print ; \"t\";\nDebug.Print \"--10\"", "t--10")]
    public async Task A_line_is_what_the_Immediate_window_showed(string code, string expected)
    {
        await Run(code + "\n");

        Lines().Should().Equal(expected);
    }

    [Theory]
    [InlineData("Debug.Print \"abc\"; Tab(2); \"d\"", "abc", " d")]
    [InlineData("Debug.Print \"x\"; Tab(0); \"y\"", "x", "y")]
    public async Task A_Tab_to_a_column_the_line_has_passed_goes_to_the_next_line(string code, string first, string second)
    {
        await Run(code + "\n");

        Lines().Should().Equal(first, second);
    }

    [Fact]
    public async Task A_line_a_run_left_open_stays_open_for_the_next_run_with_its_column()
    {
        // Measured in the VB6 IDE: two F5 runs of one Sub Main. The second run's "abc" continues the line, and its Tab(10)
        // finds the column past 10, so goes to the next line.
        const string code = "Debug.Print \"abc\"; Tab(10); \"x\";\n";
        await Run(code);
        await Run(code);

        written.ToString().Should().Be("abc      xabc\r\n         x");
    }

    [Fact]
    public async Task Every_module_prints_on_one_line_with_one_column()
    {
        await RunModules("Debug.Print \"abc\";\nMore\n", ("Module2", "Sub More()\n    Debug.Print , \"x\"\nEnd Sub\n"));

        Lines().Should().Equal("abc           x");
    }

    [Theory]
    [InlineData("Foo ,")]
    [InlineData("Foo , ,")]
    [InlineData("Foo ;")]
    public async Task Another_call_of_separators_alone_is_a_syntax_error(string statement)
    {
        // vb6.exe's "Syntax error", native and p-code, where `Foo , 2` builds (#1409).
        var act = () => Run("Sub Foo(Optional a, Optional b, Optional c)\nEnd Sub\nSub Main()\n    " + statement + "\nEnd Sub\n");

        await act.Should().ThrowAsync<VBCompileErrorException>();
    }

    [Fact]
    public async Task A_date_prints_as_it_reads_then_a_space()
    {
        await Run("Debug.Print #1/2/2003#; \"d\"\n");

        Lines().Should().Equal(VbDisplay.ToText(new Vb6Value(VbDate.FromDateTime(new System.DateTime(2003, 1, 2)))) + " d");
    }

    [Fact]
    public async Task Each_item_reaches_the_typed_channel_as_its_value()
    {
        // The test suites assert on these, so a line of several items gives each one, and a bare Debug.Print an empty
        // String, as one item always did.
        await Run("Debug.Print 1; \"a\", True\nDebug.Print\nDebug.Print , \"z\";\n");

        AssertDebugLog([new Vb6Value(1), new Vb6Value("a"), new Vb6Value(true), new Vb6Value(""), new Vb6Value("z")]);
    }

    [Fact]
    public async Task An_object_prints_its_default_member_and_a_class_with_none_is_438()
    {
        var act = () => RunClasses("Dim p As Plain\nSet p = New Plain\nDebug.Print \"x\"; p\n", ("Plain", "Public N As Integer\n"));

        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(438);
    }
}
