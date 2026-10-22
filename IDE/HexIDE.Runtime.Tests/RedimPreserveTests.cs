using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// ReDim Preserve (#1459), which HexIDE refused as not implemented, so any program that grows an array stopped at it. Each
/// transcript is what <c>vb6.exe</c> logged for the same program under en-GB, native and p-code agreeing (the oracle's
/// "ReDim Preserve").
/// </summary>
public class RedimPreserveTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    // Each array's bounds, as "(0 To 4)" or "(0 To 1,0 To 2)", and "()" for one with none.
    private const string Bounds = """
        Private Function B(ByRef x As Variant) As String
            On Error Resume Next
            Dim s As String, n As Integer, e As Long
            n = 0
            Do
                n = n + 1
                e = UBound(x, n)
                If Err.Number <> 0 Then Exit Do
                If n > 1 Then s = s & ","
                s = s & LBound(x, n) & " To " & e
            Loop
            Err.Clear
            B = "(" & s & ")"
        End Function

        """;

    [Fact]
    public async Task Preserve_keeps_the_elements_in_range_and_changes_only_the_last_upper_bound()
    {
        await Run(Bounds + """
            Dim a() As Long, m() As Long, bb() As Long, c() As Long, u() As Long, s() As String, w() As Variant
            Dim e As Long, i As Integer
            On Error Resume Next
            ReDim a(2): a(2) = 7: Err.Clear: ReDim Preserve a(4)
            e = Err.Number: Err.Clear: Debug.Print "grow: err " & e & " " & B(a) & " a(2) " & a(2) & " a(4) " & TypeName(a(4)) & " " & a(4)
            ReDim c(4): c(1) = 1: c(4) = 4: Err.Clear: ReDim Preserve c(2)
            e = Err.Number: Err.Clear: Debug.Print "shrink: err " & e & " " & B(c) & " c(1) " & c(1)
            Err.Clear: i = c(4): Debug.Print "c(4) after: err " & Err.Number
            ReDim c(4): Err.Clear: ReDim Preserve c(-1)
            e = Err.Number: Err.Clear: Debug.Print "shrink to none: err " & e & " " & B(c)
            ReDim m(1, 2): m(1, 2) = 5: m(0, 1) = 6: Err.Clear: ReDim Preserve m(1, 4)
            e = Err.Number: Err.Clear: Debug.Print "2-D last upper: err " & e & " " & B(m) & " m(1,2) " & m(1, 2) & " m(0,1) " & m(0, 1)
            ReDim m(1, 2): m(1, 2) = 5: Err.Clear: ReDim Preserve m(2, 2)
            e = Err.Number: Err.Clear: Debug.Print "2-D first upper: err " & e & " " & B(m) & " m(1,2) " & m(1, 2)
            ReDim m(1, 2): Err.Clear: ReDim Preserve m(1, 1 To 2)
            e = Err.Number: Err.Clear: Debug.Print "2-D last lower: err " & e & " " & B(m)
            ReDim m(1, 2): Err.Clear: ReDim Preserve m(4)
            e = Err.Number: Err.Clear: Debug.Print "2-D to 1-D: err " & e & " " & B(m)
            ReDim bb(1 To 3): bb(2) = 5: Err.Clear: ReDim Preserve bb(2 To 5)
            e = Err.Number: Err.Clear: Debug.Print "lower up: err " & e & " " & B(bb) & " bb(2) " & bb(2)
            ReDim bb(1 To 3): Err.Clear: ReDim Preserve bb(0 To 5)
            e = Err.Number: Err.Clear: Debug.Print "lower down: err " & e & " " & B(bb)
            ReDim bb(1 To 3): bb(2) = 5: Err.Clear: ReDim Preserve bb(1 To 6)
            e = Err.Number: Err.Clear: Debug.Print "same lower: err " & e & " " & B(bb) & " bb(2) " & bb(2)
            Err.Clear: ReDim Preserve u(3)
            e = Err.Number: Err.Clear: Debug.Print "unallocated: err " & e & " " & B(u)
            ReDim s(1): s(1) = "x": Err.Clear: ReDim Preserve s(3)
            e = Err.Number: Err.Clear: Debug.Print "String: err " & e & " s(1) " & s(1) & " s(3) [" & s(3) & "]"
            ReDim w(1): w(1) = 2: Err.Clear: ReDim Preserve w(3)
            e = Err.Number: Err.Clear: Debug.Print "Variant array: err " & e & " w(1) " & TypeName(w(1)) & " w(3) " & TypeName(w(3))
            ReDim a(2): a(1) = 5: Err.Clear: ReDim Preserve a(2147483647)
            e = Err.Number: Err.Clear: Debug.Print "too big: err " & e & " " & B(a) & " a(1) " & a(1)
            ReDim a(2): Err.Clear: ReDim Preserve a(1 To 2147483647)
            e = Err.Number: Err.Clear: Debug.Print "too big, lower moved: err " & e & " " & B(a)
            ReDim a(2): Err.Clear: ReDim Preserve a(4.5)
            e = Err.Number: Err.Clear: Debug.Print "bound 4.5: err " & e & " " & B(a)
            ReDim a(2): a(1) = 5: Err.Clear: ReDim Preserve a("x")
            e = Err.Number: Err.Clear: Debug.Print "bound x: err " & e & " " & B(a) & " a(1) " & a(1)

            """);

        AssertDebugLog(Lines("""
            grow: err 0 (0 To 4) a(2) 7 a(4) Long 0
            shrink: err 0 (0 To 2) c(1) 1
            c(4) after: err 9
            shrink to none: err 9 (0 To 4)
            2-D last upper: err 0 (0 To 1,0 To 4) m(1,2) 5 m(0,1) 6
            2-D first upper: err 9 (0 To 1,0 To 2) m(1,2) 5
            2-D last lower: err 9 (0 To 1,0 To 2)
            2-D to 1-D: err 9 (0 To 1,0 To 2)
            lower up: err 9 (1 To 3) bb(2) 5
            lower down: err 9 (1 To 3)
            same lower: err 0 (1 To 6) bb(2) 5
            unallocated: err 0 (0 To 3)
            String: err 0 s(1) x s(3) []
            Variant array: err 0 w(1) Integer w(3) Empty
            too big: err 7 (0 To 2) a(1) 5
            too big, lower moved: err 9 (0 To 2)
            bound 4.5: err 0 (0 To 4)
            bound x: err 13 (0 To 2) a(1) 5
            """));
    }

    [Fact]
    public async Task Preserve_through_a_Variant_a_parameter_and_an_As_clause()
    {
        await Run(Bounds + """
            Private Sub GP(x() As Long)
                ReDim Preserve x(6)
            End Sub
            Private Sub GV(x)
                ReDim Preserve x(6)
            End Sub
            Private Sub GF(x() As Long)
                On Error Resume Next
                ReDim Preserve x(6)
                Debug.Print "GF inside: err " & Err.Number
            End Sub
            Dim a() As Long, v As Variant, ev As Variant, tv As Variant, f(3) As Long, e As Long
            On Error Resume Next
            v = Array(1, 2, 3): Err.Clear: ReDim Preserve v(4)
            e = Err.Number: Err.Clear: Debug.Print "Array(): err " & e & " " & TypeName(v) & " " & B(v) & " v(2) " & v(2) & " v(4) " & TypeName(v(4))
            Err.Clear: ReDim Preserve ev(2)
            e = Err.Number: Err.Clear: Debug.Print "Empty: err " & e & " " & TypeName(ev)
            ReDim a(2): a(1) = 8: tv = a: Err.Clear: ReDim Preserve tv(5)
            e = Err.Number: Err.Clear: Debug.Print "Long(): err " & e & " " & TypeName(tv) & " " & B(tv) & " tv(1) " & tv(1) & " tv(5) " & TypeName(tv(5))
            tv = 5: Err.Clear: ReDim Preserve tv(2)
            e = Err.Number: Err.Clear: Debug.Print "5: err " & e & " " & TypeName(tv)
            v = Array(1, 2, 3): Err.Clear: ReDim Preserve v(4) As Long
            e = Err.Number: Err.Clear: Debug.Print "Variant() As Long: err " & e & " " & B(v)
            v = Array(1, 2, 3): Err.Clear: ReDim Preserve v(4) As Variant
            e = Err.Number: Err.Clear: Debug.Print "Variant() As Variant: err " & e & " " & B(v)
            ReDim a(2): tv = a: Err.Clear: ReDim Preserve tv(4) As Integer
            e = Err.Number: Err.Clear: Debug.Print "Long() As Integer: err " & e & " " & B(tv)
            ReDim a(2): a(1) = 1: Err.Clear: ReDim Preserve a(4) As Long
            e = Err.Number: Err.Clear: Debug.Print "As Long: err " & e & " " & B(a) & " a(1) " & a(1)
            ReDim a(2): a(1) = 8: Err.Clear: GP a
            e = Err.Number: Err.Clear: Debug.Print "GP a: err " & e & " " & B(a) & " a(1) " & a(1)
            ReDim a(2): a(1) = 8: Err.Clear: GV a
            e = Err.Number: Err.Clear: Debug.Print "GV a: err " & e & " " & B(a) & " a(1) " & a(1)
            f(1) = 8: Err.Clear: GF f
            e = Err.Number: Err.Clear: Debug.Print "GF f: err " & e & " " & B(f) & " f(1) " & f(1)

            """);

        AssertDebugLog(Lines("""
            Array(): err 0 Variant() (0 To 4) v(2) 3 v(4) Empty
            Empty: err 13 Empty
            Long(): err 0 Long() (0 To 5) tv(1) 8 tv(5) Long
            5: err 13 Integer
            Variant() As Long: err 13 (0 To 2)
            Variant() As Variant: err 0 (0 To 4)
            Long() As Integer: err 13 (0 To 2)
            As Long: err 0 (0 To 4) a(1) 1
            GP a: err 0 (0 To 6) a(1) 8
            GV a: err 0 (0 To 6) a(1) 8
            GF inside: err 10
            GF f: err 10 (0 To 3) f(1) 8
            """));
    }

    [Fact]
    public async Task Preserve_keeps_objects_and_terminates_the_ones_a_shrink_drops()
    {
        // The dropped object's Terminate comes during the ReDim Preserve, before the next statement.
        await RunClasses("""
            Private Sub RunIt()
                Dim ov() As Variant, c1 As Class1, c2 As Class1
                ReDim ov(1)
                Set c1 = New Class1: c1.Tag = "one": Set ov(1) = c1: Set c1 = Nothing
                Set c2 = New Class1: c2.Tag = "zero": Set ov(0) = c2: Set c2 = Nothing
                ReDim Preserve ov(3)
                Debug.Print "grow: " & TypeName(ov(1)) & " " & ov(1).Tag & " " & TypeName(ov(3))
                Debug.Print "shrink:"
                ReDim Preserve ov(0)
                Debug.Print "after: " & UBound(ov) & " " & ov(0).Tag
            End Sub
            RunIt
            Debug.Print "end"

            """, ("Class1", """
            Public Tag As String
            Private Sub Class_Terminate()
                Debug.Print "Terminate " & Tag
            End Sub

            """));

        AssertDebugLog(Lines("""
            grow: Class1 one Empty
            shrink:
            Terminate one
            after: 0 zero
            Terminate zero
            end
            """));
    }

    [Theory]
    [InlineData("Dim f(3) As Long\nReDim Preserve f(5)\n", "*Array already dimensioned*")]
    [InlineData("Dim a() As Long\nReDim a(2)\nReDim Preserve a(4) As Integer\n", "*Can't change data types of array elements*")]
    [InlineData("Dim a() As Long\nReDim a(2)\nReDim Preserve a(4) As Variant\n", "*Can't change data types of array elements*")]
    public async Task What_VB6_refuses_to_compile_is_refused(string code, string message)
    {
        var run = () => Run(code);

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage(message);
    }
}
