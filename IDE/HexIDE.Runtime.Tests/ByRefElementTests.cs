using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// An array element passed to a ByRef parameter is passed by reference, as a variable is (#1455). Each transcript is what
/// <c>vb6.exe</c> logged under en-GB, native and p-code agreeing (the oracle's "An array element passed ByRef"). HexIDE copied
/// the element, so `Swap s(0), s(1)` swapped nothing and every hand-written sort left its array as it was.
/// </summary>
public class ByRefElementTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    [Fact]
    public async Task An_element_passed_ByRef_is_the_element_itself()
    {
        await Run(""""
            Private gi As Integer
            Private Sub PL(x As Long)
                x = "9"
            End Sub
            Private Sub PV(x As Variant)
                x = "v"
            End Sub
            Private Sub PLI(x As Long)
                gi = 2
                x = 9
            End Sub
            Private Sub Swap(a As Variant, b As Variant)
                Dim t As Variant
                t = a: a = b: b = t
            End Sub
            Dim a(2) As Long, w(2) As Variant, v As Variant, m(2, 3) As Long, s(1) As Variant
            On Error Resume Next
            a(1) = 7: Err.Clear: PL a(1): Debug.Print "PL a(1): " & TypeName(a(1)) & " " & a(1) & " err " & Err.Number
            w(1) = 7: Err.Clear: PV w(1): Debug.Print "PV w(1): " & TypeName(w(1)) & " " & w(1) & " err " & Err.Number
            a(1) = 7: Err.Clear: PV a(1): Debug.Print "PV a(1): " & TypeName(a(1)) & " " & a(1) & " err " & Err.Number
            v = Array(1, 2, 3): Err.Clear: PV v(1): Debug.Print "PV v(1): " & TypeName(v(1)) & " " & v(1) & " err " & Err.Number
            gi = 1: a(1) = 0: a(2) = 0: Err.Clear: PLI a(gi): Debug.Print "PLI a(gi): a(1) " & a(1) & " a(2) " & a(2) & " err " & Err.Number
            Err.Clear: PL m(1, 2): Debug.Print "PL m(1, 2): " & m(1, 2) & " err " & Err.Number
            s(0) = 2: s(1) = 1: Swap s(0), s(1): Debug.Print "Swap s(0), s(1): " & s(0) & " " & s(1)
            a(0) = 4: Err.Clear: PL a(0 + 0): Debug.Print "PL a(0 + 0): " & a(0) & " err " & Err.Number
            a(0) = 4: Err.Clear: PL (a(0)): Debug.Print "PL (a(0)): " & a(0) & " err " & Err.Number

            """");

        AssertDebugLog(Lines("""
            PL a(1): Long 9 err 0
            PV w(1): String v err 0
            PV a(1): Long 7 err 13
            PV v(1): String v err 0
            PLI a(gi): a(1) 9 a(2) 0 err 0
            PL m(1, 2): 9 err 0
            Swap s(0), s(1): 1 2
            PL a(0 + 0): 9 err 0
            PL (a(0)): 4 err 0
            """));
    }

    [Fact]
    public async Task An_element_passed_ByRef_reads_as_the_element_does_and_sees_a_store_into_the_array()
    {
        // An Integer array's element is typed in `PI(x As Integer)`, so `x * 7000` overflows, and a Variant's in a Variant
        // parameter. A store into the array while the call runs shows through the parameter, and the other way round.
        await Run("""
            Private g(2) As Long
            Private Sub PI(x As Integer)
                On Error Resume Next
                Dim r As Variant
                r = x * 7000
                Debug.Print "PI " & TypeName(x) & " " & r & " err " & Err.Number
            End Sub
            Private Sub PV(x As Variant)
                On Error Resume Next
                Dim r As Variant
                r = x * 7000
                Debug.Print "PV " & TypeName(x) & " " & r & " err " & Err.Number
            End Sub
            Private Sub PG(x As Long)
                x = 9
                Debug.Print "inside, g(1) is " & g(1)
                g(1) = 4
                Debug.Print "after g(1) = 4, x is " & x
            End Sub
            Dim a(2) As Integer, w(2) As Variant
            a(1) = 5: w(1) = 5
            PI a(1): Err.Clear
            PV a(1): Err.Clear
            PV w(1): Err.Clear
            g(1) = 7: PG g(1): Debug.Print "PG g(1): " & g(1)

            """);

        AssertDebugLog(Lines("""
            PI Integer  err 6
            PV Integer 35000 err 0
            PV Integer 35000 err 0
            inside, g(1) is 9
            after g(1) = 4, x is 4
            PG g(1): 4
            """));
    }

    [Fact]
    public async Task An_array_one_of_whose_elements_is_passed_ByRef_is_locked_until_the_call_returns()
    {
        // ReDim, Erase, ReDim Preserve, a store into the array's variable and a store into a Variant holding it each raise 10
        // and leave it as it was; after the call, ReDim works. HexIDE refuses any other ReDim Preserve as not implemented.
        await Run("""
            Private d() As Long
            Private e() As Long
            Private v As Variant
            Private Sub PRD(x As Long)
                On Error Resume Next
                ReDim d(5)
                Debug.Print "ReDim inside: err " & Err.Number & " ubound " & UBound(d)
                Err.Clear
                Erase d
                Debug.Print "Erase inside: err " & Err.Number
                Err.Clear
                ReDim Preserve d(5)
                Debug.Print "ReDim Preserve inside: err " & Err.Number & " ubound " & UBound(d)
                Err.Clear
                ReDim Preserve d("x")
                Debug.Print "Preserve, bound ""x"": err " & Err.Number
                Err.Clear
                ReDim Preserve d(5 To 2)
                Debug.Print "Preserve, inverted: err " & Err.Number
                Err.Clear
                ReDim d("x")
                Debug.Print "ReDim, bound ""x"": err " & Err.Number
                Err.Clear
                ReDim d(5 To 2)
                Debug.Print "ReDim, inverted: err " & Err.Number
                Err.Clear
                d = e
                Debug.Print "d = e inside: err " & Err.Number & " ubound " & UBound(d)
                x = 9
            End Sub
            Private Sub PVV(x As Variant)
                On Error Resume Next
                v = 5
                Debug.Print "v = 5 inside: err " & Err.Number & " " & TypeName(v)
            End Sub
            ReDim d(2): ReDim e(3): d(1) = 7
            PRD d(1)
            Debug.Print "PRD d(1): " & d(1) & " ubound " & UBound(d)
            Err.Clear: ReDim d(4): Debug.Print "ReDim after the call: err " & Err.Number & " ubound " & UBound(d)
            v = Array(1, 2, 3): PVV v(1)
            Debug.Print "PVV v(1): " & TypeName(v)

            """);

        AssertDebugLog(Lines("""
            ReDim inside: err 10 ubound 2
            Erase inside: err 10
            ReDim Preserve inside: err 10 ubound 2
            Preserve, bound "x": err 13
            Preserve, inverted: err 10
            ReDim, bound "x": err 13
            ReDim, inverted: err 10
            d = e inside: err 10 ubound 2
            PRD d(1): 9 ubound 2
            ReDim after the call: err 0 ubound 4
            v = 5 inside: err 10 Variant()
            PVV v(1): Variant()
            """));
    }
}
