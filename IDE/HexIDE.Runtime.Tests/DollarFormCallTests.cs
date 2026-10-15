using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A call written with a <c>$</c>, <c>Trim$(s)</c> and <c>Left$(s, 1)</c>, the first slice of #742, which most programs
/// reach on their first line of string handling (#1071 did, in its setup dialog). The name is looked up without the
/// character, and the character is then checked against what was found. Every expectation was measured on vb6.exe, in
/// docs/vb6-fidelity-oracle.md: "A type character on a name the program also declares" for the rule, and "The library's
/// $ forms" for which functions have one and what it does with a Null.
/// </summary>
public class DollarFormCallTests : BaseVBTestFixture
{
    [Fact]
    public async Task A_dollar_form_answers_as_its_plain_form_does()
    {
        await Run("Debug.Print Trim$(\" a \")\nDebug.Print Left$(\"abc\", 1)\nDebug.Print Mid$(\"abc\", 2)\n"
                  + "Debug.Print UCase$(\"a\")\nDebug.Print Hex$(255)\nDebug.Print String$(3, \"x\")\n");

        AssertDebugLog([new Vb6Value("a"), new Vb6Value("a"), new Vb6Value("bc"), new Vb6Value("A"), new Vb6Value("FF"),
            new Vb6Value("xxx")]);
    }

    [Fact]
    public async Task Where_the_plain_form_passes_a_Null_through_the_dollar_form_raises_94()
    {
        await Run("Dim r\nOn Error Resume Next\nr = Trim(Null)\nDebug.Print IsNull(r), Err.Number\n"
                  + "r = Trim$(Null)\nDebug.Print Err.Number\nErr.Clear\nr = Left$(Null, 1)\nDebug.Print Err.Number\n");

        AssertDebugLog([new Vb6Value(true), new Vb6Value(0), new Vb6Value(94), new Vb6Value(94)]);
    }

    [Fact]
    public async Task A_dollar_on_a_library_function_without_a_dollar_form_is_ignored_or_refused_as_VB6_does()
    {
        // Ignored: Len$ is Len, a Long (measured).
        await Run("Debug.Print Len$(\"abc\")\nDebug.Print TypeName(Len$(\"abc\"))\n");
        AssertDebugLog([new Vb6Value(3), new Vb6Value("Long")]);

        // Refused: VB6's compile error, raised here when the statement runs (measured on Val$).
        var refused = () => Run("Dim r\nr = Val$(\"12\")\n");
        (await refused.Should().ThrowAsync<VBCompileErrorException>())
            .WithMessage("*Type-declaration character does not match declared data type*");
    }

    [Fact]
    public async Task The_programs_own_String_Function_answers_its_dollar_form()
    {
        // TU_01: a user Trim, declared As String, is what Trim$ calls.
        await Run("Debug.Print Trim$(\" a \")\n"
                  + "Private Function Trim(ByVal s As String) As String\n    Trim = \"user:\" & s & \"!\"\nEnd Function\n");

        AssertDebugLog([new Vb6Value("user: a !")]);
    }

    [Fact]
    public async Task The_programs_own_Function_without_an_As_clause_is_a_mismatch_for_a_dollar()
    {
        // TV_01: declared with no As clause, so a Variant, which a $ does not match; the search does not go on to the library.
        var run = () => Run("Dim r\nr = Left$(\"abc\", 1)\n"
                            + "Private Function Left(ByVal s As String, ByVal n As Long)\n    Left = \"var\"\nEnd Function\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>())
            .WithMessage("*Type-declaration character does not match declared data type*");
    }

    [Fact]
    public async Task A_dollar_whose_answer_is_not_measured_stops_the_run_naming_742()
    {
        var run = () => Run("Dim r\nr = Replace$(\"a\", \"a\", \"b\")\n");

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*Replace$*not measured*#742*");
    }
}
