using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A lone CR ends a line, as CRLF and a lone LF do (#1426). VB6 builds and runs a file with any of the three, and a form's
/// code section reaches the interpreter as it was read, lone CRs and all. Every expectation is vb6.exe's, from byte-exact
/// files under <c>/make</c> (the oracle's "A lone CR or LF in VB6 source", #1020): a CRLF file with <c>x = 0</c> first,
/// and only the line end named a lone CR or LF.
/// </summary>
public class LoneCrStatementTests : BaseVBTestFixture
{
    private static string Eol(string name) => name switch
    {
        "crlf" => "\r\n",
        "cr" => "\r",
        "lf" => "\n",
        _ => throw new System.ArgumentOutOfRangeException(nameof(name)),
    };

    private async Task<Vb6Value> X(string code)
    {
        await Run("x = 0\r\n" + code + "\r\nDebug.Print x\r\n");
        return debug.Should().ContainSingle().Subject;
    }

    [Theory]
    [InlineData("crlf")]
    [InlineData("cr")]
    [InlineData("lf")]
    public async Task A_line_end_ends_the_statement_before_it(string eol)
    {
        (await X("x = 1" + Eol(eol) + "x = x + 10")).Should().Be(new Vb6Value(11));
    }

    [Theory]
    [InlineData("crlf")]
    [InlineData("cr")]
    [InlineData("lf")]
    public async Task A_continuation_joins_its_line_to_the_next_whatever_ends_it(string eol)
    {
        (await X("x = 1 + _" + Eol(eol) + "10")).Should().Be(new Vb6Value(11));
    }

    [Theory]
    [InlineData("x = 1 ' note", "cr")]
    [InlineData("x = 1: Rem note", "cr")]
    [InlineData("x = 1 ' note", "lf")]
    public async Task A_comment_ends_at_its_line_end_and_the_next_line_runs(string line, string eol)
    {
        (await X(line + Eol(eol) + "x = x + 10")).Should().Be(new Vb6Value(11));
    }

    [Theory]
    [InlineData("crlf")]
    [InlineData("cr")]
    [InlineData("lf")]
    public async Task A_comment_ending_in_a_continuation_swallows_the_next_line_whatever_ends_it(string eol)
    {
        (await X("x = 1 ' note _" + Eol(eol) + "x = x + 10")).Should().Be(new Vb6Value(1));
    }

    // A line holding only a continuation joins nothing to the next, so it is one line boundary (measured with CRLF; a lone
    // CR or LF ends a continuation as CRLF does, measured). NEWLINE takes the whole line itself.
    [Theory]
    [InlineData("crlf")]
    [InlineData("cr")]
    [InlineData("lf")]
    public async Task A_line_holding_only_a_continuation_is_one_line_boundary_whatever_ends_it(string eol)
    {
        (await X("x = 1\r\n    _" + Eol(eol) + "x = x + 10")).Should().Be(new Vb6Value(11));
    }

    // A whole module with lone-CR line ends runs as its CRLF twin, a procedure and its call included.
    [Fact]
    public async Task A_module_with_lone_CR_line_ends_runs_as_its_CRLF_twin()
    {
        await Run("Dim x As Integer\rAdd 1\rAdd 10\rDebug.Print x\rSub Add(ByVal n As Integer)\r    x = x + n\rEnd Sub\r");

        debug.Should().ContainSingle().Which.Should().Be(new Vb6Value(11));
    }
}
