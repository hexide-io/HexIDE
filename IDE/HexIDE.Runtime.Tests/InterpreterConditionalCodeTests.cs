using HexIDE.Language;
using HexIDE.Runtime.Interpreter.ConditionalCompilation;

namespace HexIDE.Runtime.Tests;

// What the IDE gets back when it asks which lines an #If leaves out (#3559). The evaluation itself is ConditionalCompilerTests'.
public class InterpreterConditionalCodeTests
{
    private const string Win32Branches = "#If Win32 Then\nDebug.Print 1\n#Else\nDebug.Print 2\n#End If\n";

    private static readonly IConditionalCodeReader Reader = new InterpreterConditionalCode();

    private static ConditionalConstants NoConstants => new(ConditionalConstants.NoArguments);

    private static ConditionalConstants WithBuiltIns(params (string Name, object? Value)[] builtIns)
    {
        var table = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in builtIns) table[name] = value;
        return NoConstants with { BuiltIns = table };
    }

    [Fact]
    public void With_no_built_ins_given_the_interpreter_uses_VB6s_own() =>
        Reader.InactiveLines(Win32Branches, NoConstants).Should().Equal(new LineRange(4, 4));

    [Fact]
    public void A_project_argument_shadows_a_built_in()
    {
        var arguments = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase) { ["Win32"] = 0 };
        Reader.InactiveLines(Win32Branches, new ConditionalConstants(arguments)).Should().Equal(new LineRange(2, 2));
    }

    [Fact]
    public void Built_ins_given_as_numbers_and_strings_are_what_an_If_reads()
    {
        var source = "#If N = 7 And S = \"x\" Then\nDebug.Print 1\n#Else\nDebug.Print 2\n#End If\n";
        Reader.InactiveLines(source, WithBuiltIns(("N", 7), ("S", "x"))).Should().Equal(new LineRange(4, 4));
        Reader.InactiveLines(source, WithBuiltIns(("N", 8), ("S", "x"))).Should().Equal(new LineRange(2, 2));
    }

    [Fact]
    public void Given_built_ins_replace_the_interpreters_own() =>
        Reader.InactiveLines(Win32Branches, WithBuiltIns(("VBA7", 1))).Should().Equal(new LineRange(2, 2));

    [Fact]
    public void A_block_on_a_null_built_in_stays_ungreyed_and_the_others_do_not()
    {
        var source = "#If U Then\nDebug.Print 1\n#Else\nDebug.Print 2\n#End If\n#If 0 Then\nDebug.Print 3\n#End If\n";
        Reader.InactiveLines(source, WithBuiltIns(("U", null)) with { NullIsUndetermined = true })
            .Should().Equal(new LineRange(7, 7));
    }

    [Fact]
    public void A_function_call_reads_as_zero_only_when_asked_to()
    {
        var source = "#If CBool(1) Then\nDebug.Print 1\n#End If\n";
        Reader.InactiveLines(source, NoConstants with { CallsReadZero = true }).Should().Equal(new LineRange(2, 2));
        Reader.InactiveLines(source, NoConstants).Should().BeEmpty("vb6.exe calls CBool, and CBool(1) holds");
    }

    [Theory]
    [InlineData("#Else\n")]
    [InlineData("#If 0 Then\nDebug.Print 1\n")]
    [InlineData("#Const A = 1\n#Const A = 2\n#If 0 Then\nDebug.Print 1\n#End If\n")]
    public void A_directive_that_cannot_be_read_greys_nothing(string source) =>
        Reader.InactiveLines(source, NoConstants).Should().BeEmpty();
}
