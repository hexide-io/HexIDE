using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A name read through a type-declaration character, <c>s$</c>, <c>i%</c>, <c>a%(1)</c> (#742), which stopped the run. The
/// character is a check, not a conversion: the name is read as it would be without it, and its type must be the character's.
/// A name nothing declares is declared by the read, with the character's type. Every expectation was measured on vb6.exe,
/// native and p-code, in docs/vb6-fidelity-oracle/a-name-read-with-a-type-character.md.
/// </summary>
public class TypeCharacterReadTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(params string[] lines) => [.. lines.Select(l => new Vb6Value(l))];

    private const string Show = """

        Function T(v As Variant) As String
            If IsArray(v) Then T = TypeName(v) & " " & LBound(v) & " To " & UBound(v): Exit Function
            T = TypeName(v) & " [" & CStr(v) & "]"
        End Function
        """;

    private async Task Refused(string code)
    {
        var run = () => Run(code + Show);

        (await run.Should().ThrowAsync<VBCompileErrorException>())
            .WithMessage("*Type-declaration character does not match declared data type*");
    }

    [Fact]
    public async Task A_variable_reads_through_the_character_of_its_type()
    {
        await Run("""
            Dim lS As String, lI As Integer, lL As Long, mD As Double, mC As Currency, mG As Single
            lS = "ls": lI = 9: lL = 70000: mD = 2.5: mC = 1.25: mG = 1.5
            Debug.Print T(lS$) & "; " & T(lI%) & "; " & T(lL&)
            Debug.Print T(mD#) & "; " & T(mC@) & "; " & T(mG!)
            Debug.Print T(lI% + 1) & "; " & T(lS$ & "x")
            """ + Show);

        AssertDebugLog(Lines("String [ls]; Integer [9]; Long [70000]", "Double [2.5]; Currency [1.25]; Single [1.5]",
            "Integer [10]; String [lsx]"));
    }

    [Fact]
    public async Task A_local_reads_through_its_character()
    {
        await Run("""
            Sub Main()
                Dim s As String, i As Integer
                s = "in": i = 4
                Debug.Print T(s$) & "; " & T(i%)
            End Sub
            Main
            """ + Show);

        AssertDebugLog(Lines("String [in]; Integer [4]"));
    }

    [Fact]
    public async Task A_Const_reads_through_its_declared_type_or_its_values()
    {
        await Run("""
            Const KS$ = "konst"
            Const KL As Long = 7
            Const K = 5
            Const KB = 70000
            Const D = 1.5
            Debug.Print T(KS$) & "; " & T(KS) & "; " & T(KL&)
            Debug.Print T(K%) & "; " & T(KB&) & "; " & T(D#)
            """ + Show);

        AssertDebugLog(Lines("String [konst]; String [konst]; Long [7]", "Integer [5]; Long [70000]; Double [1.5]"));
    }

    [Fact]
    public async Task An_Enum_member_reads_as_a_Long()
    {
        await Run("Enum E\n    E1 = 3\nEnd Enum\nDebug.Print T(E1&)\n" + Show);

        AssertDebugLog(Lines("Long [3]"));
    }

    [Fact]
    public async Task An_array_reads_through_its_element_types_character()
    {
        await Run("""
            Dim arrI(2) As Integer, arrS() As String
            arrI(1) = 5
            ReDim arrS(1)
            arrS(1) = "e"
            Debug.Print T(arrI%(1)) & "; " & T(arrS$(1)) & "; " & UBound(arrI%) & "; " & TypeName(arrI%)
            """ + Show);

        AssertDebugLog(Lines("Integer [5]; String [e]; 2; Integer()"));
    }

    [Fact]
    public async Task A_parameter_reads_through_its_character()
    {
        await Run("""
            Sub ReadParam(n As Integer, s As String)
                Debug.Print T(n%) & "; " & T(s$)
            End Sub
            ReadParam 6, "q"
            """ + Show);

        AssertDebugLog(Lines("Integer [6]; String [q]"));
    }

    [Fact]
    public async Task Another_modules_Public_reads_through_its_character()
    {
        await RunModules("pS = \"ps\": pI = 4\nDebug.Print TypeName(pS$) & \" \" & pS$ & \"; \" & TypeName(pI%) & \" \" & pI%\n",
            ("Module2", "Public pS As String, pI As Integer\n"));

        AssertDebugLog(Lines("String ps; Integer 4"));
    }

    [Fact]
    public async Task A_name_never_declared_is_declared_by_the_read_with_the_characters_type()
    {
        await Run("""
            Debug.Print T(u$) & "; " & T(u)
            Debug.Print T(z@) & "; " & T(z)
            """ + Show);

        AssertDebugLog(Lines("String []; String []", "Currency [0]; Currency [0]"));
    }

    [Fact]
    public async Task The_character_wins_over_a_DefType()
    {
        await Run("DefInt A-Z\nDebug.Print T(x$) & \"; \" & T(x)\n" + Show);

        AssertDebugLog(Lines("String []; String []"));
    }

    [Fact]
    public async Task A_DefType_variable_reads_through_its_types_character()
    {
        await Run("DefInt A-Z\nDim y\ny = 4\nDebug.Print T(y%)\n" + Show);

        AssertDebugLog(Lines("Integer [4]"));
    }

    [Fact]
    public async Task Under_Option_Explicit_a_name_never_declared_is_not_defined()
    {
        var run = () => Run("Option Explicit\nDebug.Print u%\n");

        await run.Should().ThrowAsync<VBVariableNotDefinedException>();
    }

    [Theory]
    [InlineData("Dim v As Long\nDebug.Print v%\n")]
    [InlineData("Dim s As String\ns = \"a\"\nDebug.Print s%\n")]
    [InlineData("Dim v\nDebug.Print v$\n")]
    [InlineData("Dim o As Object\nDebug.Print TypeName(o$)\n")]
    [InlineData("Const K As Long = 1\nDebug.Print K%\n")]
    [InlineData("Const K = 5\nDebug.Print K&\n")]
    [InlineData("Const K = 5\nDebug.Print K$\n")]
    [InlineData("Enum E\n    E1 = 3\nEnd Enum\nDebug.Print E1%\n")]
    [InlineData("Dim a(2) As Long\nDebug.Print a%(1)\n")]
    [InlineData("Dim va(2)\nva(1) = \"s\"\nDebug.Print va$(1)\n")]
    [InlineData("Dim b As Byte\nDebug.Print b%\n")]
    [InlineData("Dim bo As Boolean\nDebug.Print bo%\n")]
    [InlineData("Dim d As Date\nDebug.Print d#\n")]
    [InlineData("Sub P(v)\n    Debug.Print v$\nEnd Sub\nP \"x\"\n")]
    [InlineData("DefInt A-Z\nDim y\nDebug.Print y&\n")]
    [InlineData("Dim q\nq = q$ & \"x\"\n")]
    public async Task A_character_its_type_does_not_match_is_a_compile_error(string code)
    {
        await Refused(code);
    }

    private const string Bumpers = """

        Sub Bump(n As Integer)
            n = n + 1
        End Sub
        Sub App(s As String)
            s = s & "x"
        End Sub
        Function BumpF(n As Integer) As Integer
            n = n + 10
            BumpF = n
        End Function
        """;

    [Fact]
    public async Task A_variable_passed_through_its_character_is_the_variable()
    {
        await Run("""
            Dim i As Integer, t As String, a(1) As Integer, x As Integer
            i = 1
            Bump i%
            Debug.Print "Bump i%: " & i
            Call Bump(i%)
            Debug.Print "Call Bump(i%): " & i
            Bump (i%)
            Debug.Print "Bump (i%): " & i
            t = "a"
            App t$
            Debug.Print "App t$: " & t
            Bump a%(1)
            Debug.Print "Bump a%(1): " & a(1)
            x = BumpF(i%)
            Debug.Print "BumpF(i%): " & i & " " & x
            """ + Bumpers);

        AssertDebugLog(Lines("Bump i%: 2", "Call Bump(i%): 3", "Bump (i%): 3", "App t$: ax", "Bump a%(1): 1", "BumpF(i%): 13 13"));
    }

    [Fact]
    public async Task A_name_never_declared_passed_through_its_character_is_declared_there()
    {
        await Run("Bump u%\nDebug.Print u & \" \" & TypeName(u)\n" + Bumpers);

        AssertDebugLog(Lines("1 Integer"));
    }
}
