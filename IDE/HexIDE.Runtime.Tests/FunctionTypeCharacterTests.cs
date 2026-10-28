using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A Function declared with a type-declaration character, <c>Function F%()</c> (#740), which stopped its whole module
/// loading. The character declares the return type, as the As clause it stands for would, and a call may carry it, checked
/// against that type. Every expectation was measured on vb6.exe, native and p-code, in
/// docs/vb6-fidelity-oracle/a-function-declared-with-a-type-character.md.
/// </summary>
public class FunctionTypeCharacterTests : BaseVBTestFixture
{
    private const string Typed = """
        Function FS$()
            FS = "s"
        End Function
        Function FI%()
            FI = 41
        End Function
        Function FL&()
            FL = 70000
        End Function
        Function FG!()
            FG = 1.5
        End Function
        Function FD#()
            FD = 2.25
        End Function
        Function FC@()
            FC = 3.5
        End Function

        """;

    private const string Converting = """
        Function AI%(ByVal v As Variant)
            AI = v
        End Function
        Function AL&(ByVal v As Variant)
            AL = v
        End Function
        Function AC@(ByVal v As Variant)
            AC = v
        End Function
        Function AD#(ByVal v As Variant)
            AD = v
        End Function
        Function AS_$(ByVal v As Variant)
            AS_ = v
        End Function

        """;

    private static List<Vb6Value> Lines(params string[] lines) => [.. lines.Select(l => new Vb6Value(l))];

    [Fact]
    public async Task Each_character_declares_the_return_type()
    {
        await Run("Debug.Print TypeName(FS()) & \" \" & FS()\nDebug.Print TypeName(FI()) & \" \" & FI()\n"
                  + "Debug.Print TypeName(FL()) & \" \" & FL()\nDebug.Print TypeName(FG()) & \" \" & FG()\n"
                  + "Debug.Print TypeName(FD()) & \" \" & FD()\nDebug.Print TypeName(FC()) & \" \" & FC()\n" + Typed);

        AssertDebugLog(Lines("String s", "Integer 41", "Long 70000", "Single 1.5", "Double 2.25", "Currency 3.5"));
    }

    [Fact]
    public async Task A_Function_never_assigned_returns_its_types_default()
    {
        await Run("""
            Debug.Print TypeName(NS()) & " " & Len(NS())
            Debug.Print TypeName(NI()) & " " & NI()
            Debug.Print TypeName(NC()) & " " & NC()
            Function NS$()
            End Function
            Function NI%()
            End Function
            Function NC@()
            End Function

            """);

        AssertDebugLog(Lines("String 0", "Integer 0", "Currency 0"));
    }

    [Fact]
    public async Task A_call_may_carry_the_character_with_or_without_parentheses()
    {
        await Run("Debug.Print TypeName(FI%()) & \" \" & FI%()\nDebug.Print TypeName(FS$()) & \" \" & FS$()\n"
                  + "Dim v\nv = FI% + 1\nDebug.Print TypeName(v) & \" \" & v\nv = FI + FG\nDebug.Print TypeName(v) & \" \" & v\n"
                  + "Debug.Print TypeName(FC@()) & \" \" & FC@()\n" + Typed);

        AssertDebugLog(Lines("Integer 41", "String s", "Integer 42", "Single 42.5", "Currency 3.5"));
    }

    [Fact]
    public async Task A_Function_calls_itself_through_its_character()
    {
        await Run("""
            Debug.Print TypeName(RI%(4)) & " " & RI%(4)
            Function RI%(ByVal n As Integer)
                If n > 0 Then RI = n + RI%(n - 1)
            End Function

            """);

        AssertDebugLog(Lines("Integer 10"));
    }

    [Fact]
    public async Task What_is_stored_into_the_return_is_converted_to_the_declared_type()
    {
        await Run("Debug.Print TypeName(AI(2.5)) & \" \" & AI(2.5)\nDebug.Print AI(\"12\")\nDebug.Print AL(1.5)\n"
                  + "Debug.Print TypeName(AS_(5)) & \" \" & AS_(5)\nDebug.Print AC(1.23456)\n"
                  + "Debug.Print TypeName(AD(#1/2/2001#)) & \" \" & AD(#1/2/2001#)\n" + Converting);

        AssertDebugLog([new Vb6Value("Integer 2"), new Vb6Value((short)12), new Vb6Value(2L), new Vb6Value("String 5"),
            Vb6Value.NewCurrency(1.2346m), new Vb6Value("Double 36893")]);
    }

    [Fact]
    public async Task A_store_the_type_cannot_hold_raises_its_error_to_the_caller()
    {
        await Run("Dim r\nOn Error Resume Next\nr = AI(40000)\nDebug.Print Err.Number\nErr.Clear\n"
                  + "r = AI(\"x\")\nDebug.Print Err.Number\nErr.Clear\nr = AS_(Null)\nDebug.Print Err.Number\n" + Converting);

        AssertDebugLog([new Vb6Value(6L), new Vb6Value(13L), new Vb6Value(94L)]);
    }

    [Fact]
    public async Task The_character_wins_over_a_DefType()
    {
        await Run("""
            DefInt A-Z
            Debug.Print TypeName(FS()) & " " & FS()
            Debug.Print TypeName(FG()) & " " & FG()
            Debug.Print TypeName(FN()) & " " & FN()
            Function FS$()
                FS = "s"
            End Function
            Function FG!()
                FG = 2.6
            End Function
            Function FN()
                FN = 2.6
            End Function

            """);

        AssertDebugLog(Lines("String s", "Single 2.6", "Integer 3"));
    }

    [Fact]
    public async Task A_character_and_an_As_clause_together_are_a_compile_error()
    {
        var run = () => Run("Debug.Print F()\nFunction F%() As Integer\n    F = 1\nEnd Function\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Expected: end of statement*");
    }

    [Fact]
    public async Task A_call_with_another_character_is_a_compile_error()
    {
        var run = () => Run("Debug.Print F&()\nFunction F%()\n    F = 1\nEnd Function\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>())
            .WithMessage("*Type-declaration character does not match declared data type*");
    }

    [Fact]
    public async Task The_same_name_with_two_characters_is_ambiguous()
    {
        var run = () => Run("Debug.Print F()\nFunction F$()\n    F = \"a\"\nEnd Function\nFunction F%()\n    F = 1\nEnd Function\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Ambiguous name detected: F*");
    }

    [Fact]
    public async Task The_return_assigned_through_its_character_sets_it()
    {
        // Through the assignment's bare name (#739), which is the return slot.
        await Run("Debug.Print TypeName(BI()) & \" \" & BI()\nDebug.Print TypeName(BS()) & \" \" & BS()\n"
                  + "Function BI%()\n    BI% = 7\nEnd Function\nFunction BS$()\n    BS$ = \"b\"\nEnd Function\n");

        AssertDebugLog(Lines("Integer 7", "String b"));
    }

    [Fact]
    public async Task The_return_assigned_through_another_character_is_a_compile_error()
    {
        var run = () => Run("Debug.Print F()\nFunction F%()\n    F& = 1\nEnd Function\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>())
            .WithMessage("*Type-declaration character does not match declared data type*");
    }

    [Fact]
    public async Task A_typed_Function_in_another_module_answers_its_character()
    {
        await RunModules("Debug.Print TypeName(FL&()) & \" \" & FL&()\nDebug.Print TypeName(FD#()) & \" \" & FD#()\n",
            ("Module2", Typed));

        AssertDebugLog(Lines("Long 70000", "Double 2.25"));
    }
}
