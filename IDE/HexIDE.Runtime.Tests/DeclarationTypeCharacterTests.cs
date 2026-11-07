using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A declaration written with a type character, <c>Dim s$</c>, <c>Sub F(n%)</c>, <c>Property Get P$()</c> (#742). A
/// module-level one stopped its module loading, a local one stopped the run, and a parameter's was ignored, so
/// <c>Sub F(n%)</c> took a Variant. The character is the As clause it stands for. Every expectation was measured on
/// vb6.exe, native and p-code, in docs/vb6-fidelity-oracle/a-declaration-written-with-a-type-character.md.
/// </summary>
public class DeclarationTypeCharacterTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(params string[] lines) => [.. lines.Select(l => new Vb6Value(l))];

    private const string T = """
        Function T(v)
            If IsArray(v) Then T = TypeName(v) & " " & LBound(v) & " To " & UBound(v): Exit Function
            T = TypeName(v) & " [" & CStr(v) & "]"
        End Function

        """;

    [Fact]
    public async Task A_module_level_declaration_takes_the_characters_type()
    {
        await Run("""
            Option Explicit
            Dim mI%, mL&, mG!, mD#, mC@, mS$
            Private privS$
            Public qI%
            Dim mixA%, mixB As Long, mixC$
            Debug.Print T(mI) & "; " & T(mL) & "; " & T(mG) & "; " & T(mD) & "; " & T(mC) & "; " & T(mS)
            Debug.Print T(privS) & "; " & T(qI) & "; " & T(mixA) & "; " & T(mixB) & "; " & T(mixC)
            mI = 7.5
            Debug.Print T(mI)

            """ + T);

        AssertDebugLog(Lines("Integer [0]; Long [0]; Single [0]; Double [0]; Currency [0]; String []",
            "String []; Integer [0]; Integer [0]; Long [0]; String []", "Integer [8]"));
    }

    [Fact]
    public async Task A_local_declaration_takes_the_characters_type_and_converts_its_stores()
    {
        await Run("""
            Sub Main()
                Dim lI%, lL&, lG!, lD#, lC@, lS$
                Debug.Print T(lI) & "; " & T(lL) & "; " & T(lG) & "; " & T(lD) & "; " & T(lC) & "; " & T(lS)
                lI = 2.5: lL = 3.5: lS = 12
                Debug.Print T(lI) & "; " & T(lL) & "; " & T(lS)
                On Error Resume Next
                lI = 40000: Debug.Print Err.Number: Err.Clear
                lI = "x": Debug.Print Err.Number
            End Sub
            Main

            """ + T);

        AssertDebugLog([new Vb6Value("Integer [0]; Long [0]; Single [0]; Double [0]; Currency [0]; String []"),
            new Vb6Value("Integer [2]; Long [4]; String [12]"), new Vb6Value(6L), new Vb6Value(13L)]);
    }

    [Fact]
    public async Task An_array_declared_with_a_character_has_that_element_type()
    {
        await Run("""
            Dim mA%(3), mB$()
            Sub Main()
                Dim lA&(2), lB#()
                Debug.Print T(mA) & ", " & T(mA(0))
                ReDim mB(2)
                Debug.Print T(mB) & ", " & T(mB(0))
                ReDim mB$(4)
                Debug.Print T(mB)
                Debug.Print T(lA) & ", " & T(lA(0))
                ReDim lB(1)
                Debug.Print T(lB) & ", " & T(lB(0))
            End Sub
            Main

            """ + T);

        AssertDebugLog(Lines("Integer() 0 To 3, Integer [0]", "String() 0 To 2, String []", "String() 0 To 4",
            "Long() 0 To 2, Long [0]", "Double() 0 To 1, Double [0]"));
    }

    [Fact]
    public async Task A_Static_declared_with_a_character_keeps_its_value_and_type()
    {
        await Run("""
            Sub CountStatic()
                Static sI%, sS$
                sI = sI + 1
                sS = sS & "x"
                Debug.Print T(sI) & ", " & T(sS)
            End Sub
            CountStatic
            CountStatic

            """ + T);

        AssertDebugLog(Lines("Integer [1], String [x]", "Integer [2], String [xx]"));
    }

    [Fact]
    public async Task A_parameter_declared_with_a_character_has_that_type()
    {
        await Run("""
            Sub TakeI(n%)
                Debug.Print "TakeI " & T(n)
            End Sub
            Sub TakeIV(ByVal n%)
                Debug.Print "TakeIV " & T(n)
            End Sub
            Sub TakeS(ByVal s$)
                Debug.Print "TakeS " & T(s)
            End Sub
            Sub TakeOpt(Optional n% = 3, Optional s$ = "d")
                Debug.Print "TakeOpt " & T(n) & ", " & T(s)
            End Sub
            Sub TakeArr(a%())
                Debug.Print "TakeArr " & T(a)
            End Sub
            Sub Bump(n%)
                n = n + 1
            End Sub
            Sub Main()
                TakeI 2.6
                TakeIV 2.6
                TakeIV "7"
                TakeS 5
                TakeOpt
                TakeOpt 9, "e"
                Dim arr%(1 To 2)
                TakeArr arr
                Dim k%
                k = 4
                Bump k
                Debug.Print "Bump " & T(k)
            End Sub
            Main

            """ + T);

        AssertDebugLog(Lines("TakeI Integer [3]", "TakeIV Integer [3]", "TakeIV Integer [7]", "TakeS String [5]",
            "TakeOpt Integer [3], String [d]", "TakeOpt Integer [9], String [e]", "TakeArr Integer() 1 To 2", "Bump Integer [5]"));
    }

    [Fact]
    public async Task A_Property_Get_declared_with_a_character_returns_that_type()
    {
        // Measured in a standard module. Read through a class, because HexIDE does not reach a standard module's Property
        // Get by name (#977).
        await RunClasses("""
            Dim o As Foo
            Set o = New Foo
            o.PL = 12
            Debug.Print TypeName(o.PS) & " [" & o.PS & "]; " & TypeName(o.PN) & " [" & o.PN & "]; " & TypeName(o.Q)
            """,
            ("Foo", """
                Private mP$
                Public Property Get PS$()
                    PS = "got " & mP
                End Property
                Public Property Let PL(v$)
                    mP = v
                End Property
                Public Property Get PN%()
                    PN = 7.6
                End Property
                Public Property Get Q$() As Long
                    Q = 5
                End Property

                """));

        AssertDebugLog(Lines("String [got 12]; Integer [8]; Long"));
    }

    [Fact]
    public async Task A_Public_declared_with_a_character_in_another_module_converts_a_store_from_this_one()
    {
        await RunModules("qI = 2.6: qS = 1.5\nDebug.Print TypeName(qI) & \" \" & qI & \"; \" & TypeName(qS) & \" \" & qS\n",
            ("Module2", "Public qI%, qS$\n"));

        AssertDebugLog(Lines("Integer 3; String 1.5"));
    }

    [Fact]
    public async Task ReDim_with_the_arrays_own_character_redimensions_it()
    {
        await Run("""
            Dim x%()
            Sub Main()
                ReDim x(2)
                Debug.Print TypeName(x)
                ReDim x%(3)
                Debug.Print TypeName(x) & " " & UBound(x)
            End Sub
            Main
            """);

        AssertDebugLog(Lines("Integer()", "Integer() 3"));
    }

    [Theory]
    [InlineData("Dim x% As Integer\n", "*Expected: end of statement*")]
    [InlineData("Sub Main()\n    Dim x% As Integer\nEnd Sub\nMain\n", "*Expected: end of statement*")]
    [InlineData("Sub P(n% As Integer)\nEnd Sub\nP 1\n", "*Expected: list separator or )*")]
    [InlineData("Dim x() As Long\nSub R()\n    ReDim x%(2)\nEnd Sub\nR\n", "*Type-declaration character does not match declared data type*")]
    [InlineData("Sub R()\n    Dim x() As Long\n    ReDim x%(2)\nEnd Sub\nR\n", "*Type-declaration character does not match declared data type*")]
    public async Task A_character_VB6_refuses_on_a_declaration_is_its_compile_error(string code, string message)
    {
        var run = () => Run(code);

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage(message);
    }
}
