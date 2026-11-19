using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured native and p-code rows recorded in #179's supervisor comment (oracle PR #4019).
public class FixedLengthStringTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Dim fx As String * 5", "")]
    [InlineData("", "Dim fx As String * 5")]
    [InlineData("", "Static fx As String * 5")]
    public async Task Unassigned_scalar_contains_NULs(string module, string local)
    {
        await Run($"{module}\nSub Main()\n{local}\nDebug.Print fx\nEnd Sub\nMain");
        AssertDebugLog([new Vb6Value(new string('\0', 5))]);
    }

    [Theory]
    [InlineData("\"ab\"", "ab   ")]
    [InlineData("\"1234567\"", "12345")]
    [InlineData("\"\"", "     ")]
    [InlineData("Empty", "     ")]
    [InlineData("12", "12   ")]
    [InlineData("1.5", "1.5  ")]
    [InlineData("True", "True ")]
    public async Task Stores_convert_pad_and_truncate(string source, string expected)
    {
        using var locale = VbLocale.Use(System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
        await Run($"Dim fx As String * 5\nfx = {source}\nDebug.Print fx\nDebug.Print Len(fx)\nDebug.Print LenB(fx)\nDebug.Print TypeName(fx)\nDebug.Print VarType(fx) = vbString");
        AssertDebugLog([new Vb6Value(expected), new Vb6Value(5L), new Vb6Value(10), new Vb6Value("String"), new Vb6Value(true)]);
    }

    [Fact]
    public async Task Null_leaves_previous_value_and_padding_is_observable()
    {
        await Run("Dim fx As String * 5\nfx = \"ab\"\nOn Error Resume Next\nfx = Null\nDebug.Print Err.Number\nDebug.Print fx = \"ab\"\nDebug.Print fx = \"ab   \"\nDebug.Print fx & \"|\"\nDebug.Print CStr(fx)\nDim v\nv = fx\nDebug.Print v");
        AssertDebugLog([new Vb6Value(94L), new Vb6Value(false), new Vb6Value(true), new Vb6Value("ab   |"), new Vb6Value("ab   "), new Vb6Value("ab   ")]);
    }

    [Theory]
    [InlineData("LSet", "\"ab\"", "ab   ")]
    [InlineData("LSet", "\"1234567\"", "12345")]
    [InlineData("LSet", "\"\"", "     ")]
    [InlineData("RSet", "\"ab\"", "   ab")]
    [InlineData("RSet", "\"1234567\"", "12345")]
    [InlineData("RSet", "\"\"", "     ")]
    public async Task Justification_matches_2517(string statement, string source, string expected)
    {
        // docs/vb6-fidelity-oracle/lset-and-rset-into-a-string.md, six f5 rows.
        await Run($"Dim fx As String * 5\n{statement} fx = {source}\nDebug.Print fx");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("Mid$(fx, 2, 2) = \"XY\"", "aXYde")]
    [InlineData("Mid$(fx, 4) = \"WXYZ\"", "abcWX")]
    [InlineData("fx = fx & \"x\"", "abcde")]
    public async Task In_place_stores_keep_width(string statement, string expected)
    {
        await Run($"Dim fx As String * 5\nfx = \"abcde\"\n{statement}\nDebug.Print fx");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task Constant_width_is_read()
    {
        await Run("Const N = 5\nDim fx As String * N\nfx = \"ab\"\nDebug.Print fx");
        AssertDebugLog([new Vb6Value("ab   ")]);
    }

    [Theory]
    [InlineData("Dim fx As String * 0", "Invalid length for fixed-length string")]
    [InlineData("Dim fx As String * 65527", "Invalid length for fixed-length string")]
    [InlineData("Dim n As Long\nDim fx As String * n", "Constant expression required")]
    public async Task Invalid_width_is_compile_error(string declaration, string message)
    {
        var run = () => Run(declaration);
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain(message);
    }
    [Fact]
    public async Task ByVal_keeps_caller_and_date_uses_locale_short_form()
    {
        using var locale = VbLocale.Use(System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
        await Run("Dim fx As String * 10\nfx = #1/2/2003#\nDebug.Print fx\nTake fx\nDebug.Print fx\nSub Take(ByVal s As String)\ns = \"changed\"\nEnd Sub");
        AssertDebugLog([new Vb6Value("02/01/2003"), new Vb6Value("02/01/2003")]);
    }

    [Theory]
    [InlineData("Const N = \"unmeasured\"\nDim fx As String * N", "Fixed-length string width conversion is not implemented (#179)")]
    public async Task Remaining_storage_paths_are_explicitly_refused(string program, string message)
    {
        var run = () => Run(program);
        (await run.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Be(message);
    }

}
