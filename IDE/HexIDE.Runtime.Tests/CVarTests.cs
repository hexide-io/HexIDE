using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// <c>CVar</c> (#1466), which stopped the program as undefined. It returns its argument as a Variant, keeping the argument's
/// own type, so the result widens in arithmetic where a typed one overflows (#987). Each transcript is what <c>vb6.exe</c>
/// logged for the same program under en-GB, native and p-code agreeing (the oracle's "CVar").
/// </summary>
public class CVarTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    [Fact]
    public async Task CVar_returns_its_argument_as_a_Variant()
    {
        await Run("""
            Private Sub Rows()
                On Error Resume Next
                Dim r, v, n As Integer, a(1) As Integer, c As Collection
                n = 5
                Set c = New Collection
                Debug.Print "CVar(5) " & TypeName(CVar(5))
                Debug.Print "CVar(""5"") " & TypeName(CVar("5"))
                Debug.Print "CVar(Null) " & TypeName(CVar(Null))
                Debug.Print "CVar(Empty) " & TypeName(CVar(Empty))
                Debug.Print "CVar(n) " & TypeName(CVar(n))
                Debug.Print "CVar(2.5) " & TypeName(CVar(2.5))
                Debug.Print "CVar(70000) " & TypeName(CVar(70000))
                Debug.Print "CVar(True) " & TypeName(CVar(True)) & " " & CVar(True)
                Debug.Print "CVar(#1/2/2000#) " & TypeName(CVar(#1/2/2000#))
                Debug.Print "CVar(a) " & TypeName(CVar(a))
                Debug.Print "CVar(Array(1, 2)) " & TypeName(CVar(Array(1, 2)))
                Debug.Print "CVar(c) " & TypeName(CVar(c))
                Debug.Print "CVar(Nothing) " & TypeName(CVar(Nothing))
                r = CVar(5) * 7000: Debug.Print "CVar(5) * 7000 = " & TypeName(r) & " " & r
                r = CVar(n) * 7000: Debug.Print "CVar(n) * 7000 = " & TypeName(r) & " " & r
                r = Empty: r = 5 * 7000: Debug.Print "5 * 7000 -> err " & Err.Number
                Err.Clear
                Select Case CVar(5)
                    Case Is < 5.4: Debug.Print "CVar(5) Case"
                    Case Else: Debug.Print "CVar(5) Else"
                End Select
                Select Case CVar("9")
                    Case Is > 10: Debug.Print "CVar(""9"") Case"
                    Case Else: Debug.Print "CVar(""9"") Else"
                End Select
                r = CVar("5") + 1: Debug.Print "CVar(""5"") + 1 = " & TypeName(r) & " " & r
                r = Empty: r = CVar("abc") + 1: Debug.Print "CVar(""abc"") + 1 -> err " & Err.Number
                Err.Clear
                Debug.Print "IsNull(CVar(Null)) " & IsNull(CVar(Null))
                Debug.Print "CVar$(5) " & TypeName(CVar$(5))
                a(0) = 1
                v = CVar(a)
                v(0) = 9
                Debug.Print "a(0) " & a(0) & ", v(0) " & v(0)
            End Sub

            Rows

            """);

        AssertDebugLog(Lines("""
            CVar(5) Integer
            CVar("5") String
            CVar(Null) Null
            CVar(Empty) Empty
            CVar(n) Integer
            CVar(2.5) Double
            CVar(70000) Long
            CVar(True) Boolean True
            CVar(#1/2/2000#) Date
            CVar(a) Integer()
            CVar(Array(1, 2)) Variant()
            CVar(c) Collection
            CVar(Nothing) Nothing
            CVar(5) * 7000 = Long 35000
            CVar(n) * 7000 = Long 35000
            5 * 7000 -> err 6
            CVar(5) Case
            CVar("9") Else
            CVar("5") + 1 = Double 6
            CVar("abc") + 1 -> err 13
            IsNull(CVar(Null)) True
            CVar$(5) Integer
            a(0) 1, v(0) 9
            """));
    }

    // CVar is one of the conversions that take no named argument (#1465).
    [Theory]
    [InlineData("Sub S()\n    Debug.Print CVar(Expression:=5)\nEnd Sub\nS", "*Syntax error*")]
    public async Task What_VB6_refuses_to_compile_is_refused(string program, string message)
    {
        var run = () => Run(program + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage(message);
    }
}
