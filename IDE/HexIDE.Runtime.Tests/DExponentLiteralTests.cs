using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// D is an exponent marker in a numeric literal, as E is, in either case (#1611). Measured on vb6.exe, native and p-code
/// alike, one /make project per literal, with the probe in docs/vb6-fidelity-oracle.md, "A D exponent in a source
/// literal". Each expected line is what vb6.exe logged for `L "lit: " & TypeName(lit) & " " & CStr(lit)`. Neither of
/// HexIDE's grammars lexed a D exponent in a whole number, so `x = 1D3` failed to load.
/// </summary>
public class DExponentLiteralTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("1D3@", "1D3@: Currency 1000")]
    [InlineData("1D3!", "1D3!: Single 1000")]
    [InlineData("1D3#", "1D3#: Double 1000")]
    [InlineData("1D3", "1D3: Double 1000")]
    [InlineData("1D300", "1D300: Double 1E+300")]
    [InlineData(".5D1", ".5D1: Double 5")]
    [InlineData("1d3", "1d3: Double 1000")]
    [InlineData("1.5D-2", "1.5D-2: Double 0.015")]
    [InlineData("1D+3", "1D+3: Double 1000")]
    [InlineData("2.5D2", "2.5D2: Double 250")]
    [InlineData("1E3", "1E3: Double 1000")]
    [InlineData("2.5E2", "2.5E2: Double 250")]
    [InlineData("-1D3", "-1D3: Double -1000")]
    [InlineData("1D-400", "1D-400: Double 0")]
    [InlineData("1e3", "1e3: Double 1000")]
    [InlineData("1E-400", "1E-400: Double 0")]
    public async Task A_literal_vb6_exe_compiles_has_its_type_and_value(string literal, string expected)
    {
        await Run($"Debug.Print \"{literal}: \" & TypeName({literal}) & \" \" & CStr({literal})\n");

        AssertDebugLog([new Vb6Value(expected)]);
    }

    // vb6.exe refuses each of these to build. A value beyond its type, or an exponent with % or &, is its "Syntax error",
    // raised as the module loads or the literal is read; a malformed exponent fails in HexIDE's parser, in its own words.
    [Theory]
    [InlineData("1D3&")]
    [InlineData("1D3%")]
    [InlineData("1D400")]
    [InlineData("1E3%")]
    [InlineData("1D20@")]
    [InlineData("1D39!")]
    [InlineData("1D")]
    [InlineData("1.5D")]
    [InlineData("1D-")]
    [InlineData("1 D3")]
    [InlineData("1D2D2")]
    [InlineData("1E3&")]
    [InlineData("1E20@")]
    [InlineData("1E39!")]
    [InlineData("1E400")]
    public async Task A_literal_vb6_exe_refuses_is_refused(string literal)
    {
        var run = () => Run($"Debug.Print TypeName({literal})\n");

        await run.Should().ThrowAsync<VBCompileErrorException>();
    }

    [Theory]
    [InlineData("1D3&")]
    [InlineData("1D3%")]
    [InlineData("1D400")]
    [InlineData("1E3%")]
    [InlineData("1D20@")]
    [InlineData("1D39!")]
    [InlineData("1D2D2")]
    [InlineData("1E3&")]
    [InlineData("1E20@")]
    [InlineData("1E39!")]
    [InlineData("1E400")]
    public async Task One_the_lexer_takes_is_vb6_exes_Syntax_error(string literal)
    {
        var run = () => Run($"Debug.Print TypeName({literal})\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Syntax error*");
    }

    // vb6.exe reads 5.D1 as "5.D1: Double 50". Neither grammar reads a literal ending in its point (#1615), so it
    // is refused as the module loads. Pinned so that fixing #1615 moves it into the theory above.
    [Fact]
    public async Task A_literal_ending_in_its_point_is_not_read_yet()
    {
        var run = () => Run("Debug.Print TypeName(5.D1)\n");

        await run.Should().ThrowAsync<VBCompileErrorException>();
    }

    // The probe also had `Const KS! = 2.5D2`, a Single 250 in vb6.exe, which ran once a Const took its type character (#1614).
    [Fact]
    public async Task A_Const_holds_one()
    {
        await Run("""
            Const K = 1D3
            Const KS! = 2.5D2
            Debug.Print "Const K = 1D3: " & TypeName(K) & " " & CStr(K)
            Debug.Print "Const KS! = 2.5D2: " & TypeName(KS) & " " & CStr(KS)
            """);

        AssertDebugLog([new Vb6Value("Const K = 1D3: Double 1000"), new Vb6Value("Const KS! = 2.5D2: Single 250")]);
    }

    [Fact]
    public async Task A_directive_reads_one()
    {
        await Run("""
            #Const K = 1D3
            #If K = 1000 Then
            Debug.Print "#Const K = 1D3: equals 1000"
            #Else
            Debug.Print "#Const K = 1D3: not 1000"
            #End If
            #If 2.5D2 = 250 Then
            Debug.Print "#If 2.5D2 = 250: true"
            #Else
            Debug.Print "#If 2.5D2 = 250: false"
            #End If
            """);

        AssertDebugLog([new Vb6Value("#Const K = 1D3: equals 1000"), new Vb6Value("#If 2.5D2 = 250: true")]);
    }

    // An Enum member's value is folded before the run, by its own reader of a literal. Composed from two measured facts:
    // 2.5D1 is the Double 25 and 1.25D1 is 12.5, wherever a literal is, and a member rounds half to even (PrePass).
    [Fact]
    public async Task An_Enum_member_folds_one()
    {
        await Run("""
            Enum E
                A = 2.5D1
                B = 1.25D1
            End Enum
            Debug.Print A & " " & B
            """);

        AssertDebugLog([new Vb6Value("25 12")]);
    }

    // A name that looks like an exponent, D2, is still a name where it is not glued to a number.
    [Fact]
    public async Task A_name_like_D2_is_still_a_name()
    {
        await Run("Dim D2 As Integer\nD2 = 7\nDebug.Print \"a name D2 beside a literal: \" & CStr(1 + D2)\n");

        AssertDebugLog([new Vb6Value("a name D2 beside a literal: 8")]);
    }
}
