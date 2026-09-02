using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Numeric line labels — `10 Debug.Print 1`. Every expectation is oracle-pinned against vb6.exe.
///
/// This was one of only THREE constructs in the whole VB6 language that failed to PARSE (see
/// docs/MISSING_LANGUAGE.md), which made it the highest-value kind of gap to close: a parse failure takes
/// the entire module down, so nothing in the file runs and the editor cannot open it usefully.
///
/// It was also a disagreement between HexIDE's two halves. The LSP server's grammar already had a
/// <c>lineNumber</c> rule, so the editor accepted code the interpreter could not parse — a file that
/// showed no syntax error and then refused to run.
///
/// A numeric label takes no colon and prefixes a statement on the SAME line, so it cannot be a statement
/// of its own; the grammar carries it as an optional prefix on the block. Everything downstream is shared
/// with named labels, because the label table is keyed by text.
/// </summary>
public class NumericLineLabelTests : BaseVBTestFixture
{
    [Fact]
    public async Task ANumericLabelParsesAndTheStatementRuns()
    {
        // The parse-level fix on its own: before this, the whole module failed to load.
        await Run("10 Debug.Print 1\n");
        AssertDebugLog([new Vb6Value(1)]);
    }

    [Fact]
    public async Task GoTo_JumpsToANumericLabel_InAnyOrder()
    {
        // Measured "a-twenty-ten": labels are jump targets, not BASIC line numbers, so they need not be
        // in ascending order and control flows wherever it is sent.
        await Run(
            "Dim s\n" +
            "s = \"a\"\n" +
            "GoTo 20\n" +
            "10  s = s & \"-ten\"\n" +
            "    GoTo 99\n" +
            "20  s = s & \"-twenty\"\n" +
            "    GoTo 10\n" +
            "99  Debug.Print s\n");
        AssertDebugLog([new Vb6Value("a-twenty-ten")]);
    }

    [Fact]
    public async Task OnErrorGoTo_TakesANumericLabel()
    {
        await Run(
            "On Error GoTo 50\n" +
            "Err.Raise 5\n" +
            "Debug.Print \"not reached\"\n" +
            "GoTo 99\n" +
            "50 Debug.Print \"handled\"\n" +
            "99 Debug.Print \"done\"\n");
        AssertDebugLog([new Vb6Value("handled"), new Vb6Value("done")]);
    }

    [Fact]
    public async Task Resume_TakesANumericLabel()
    {
        // Measured "resumed to 60". Resume already accepted an identifier label; a numeric one is the same
        // thing spelled differently, and without the explicit token check it would have parsed and then
        // silently retried the faulting statement instead of jumping — a wrong answer with no error.
        await Run(
            "On Error GoTo 50\n" +
            "Err.Raise 5\n" +
            "Debug.Print \"not reached\"\n" +
            "50 Resume 60\n" +
            "60 Debug.Print \"resumed to 60\"\n");
        AssertDebugLog([new Vb6Value("resumed to 60")]);
    }

    [Fact]
    public async Task NumericAndNamedLabelsCoexist()
    {
        // Measured. They share one table, so nothing distinguishes them at the jump.
        await Run(
            "GoTo 10\n" +
            "Skip:\n" +
            "Debug.Print \"wrong\"\n" +
            "GoTo 99\n" +
            "10 Debug.Print \"numeric and named coexist\"\n" +
            "99 Debug.Print \"end\"\n");
        AssertDebugLog([new Vb6Value("numeric and named coexist"), new Vb6Value("end")]);
    }

    [Fact]
    public async Task ALabelledStatementStillExecutesWhenFallenIntoRatherThanJumpedTo()
    {
        // A label is a marker, not a barrier: reaching it in sequence runs the statement normally.
        await Run("Debug.Print 1\n10 Debug.Print 2\nDebug.Print 3\n");
        AssertDebugLog([new Vb6Value(1), new Vb6Value(2), new Vb6Value(3)]);
    }

    [Fact]
    public async Task ANumericLabelInsideAProcedureWorks()
    {
        // The label table is built per procedure body, so this is a distinct path from module-level code.
        await Run("Sub Go()\n    GoTo 10\n    Debug.Print \"skipped\"\n10  Debug.Print \"jumped\"\nEnd Sub\n\nGo\n");
        AssertDebugLog([new Vb6Value("jumped")]);
    }

    [Fact]
    public async Task AnUndefinedNumericLabel_IsACompileError()
    {
        // Same treatment as an undefined named label — loud, not a silent no-jump.
        var act = async () => await Run("GoTo 42\nDebug.Print 1\n");
        (await act.Should().ThrowAsync<VBCompileErrorException>())
            .Which.Message.Should().Contain("42");
    }
}
