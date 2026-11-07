using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// One Next closing several loops, <c>Next j, i</c> (#2393), which stopped its whole module loading. vb6.exe runs it as
/// <c>Next j: Next i</c>, for For and For Each alike. Every expectation was measured on vb6.exe, native and p-code, in
/// docs/vb6-fidelity-oracle/next-closing-several-loops.md.
/// </summary>
public class NextCounterListTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(params string[] lines) => [.. lines.Select(l => new Vb6Value(l))];

    [Theory]
    [InlineData("Next j, i")]
    [InlineData("Next j,i")]
    [InlineData("Next j , i")]
    [InlineData("Next j, _\n            i")]
    [InlineData("Next j _\n            , i")]
    public async Task One_Next_closes_two_loops(string next)
    {
        await Run($"""
            Sub Main()
                Dim i As Integer, j As Integer, s As String
                For i = 1 To 2
                    For j = 1 To 2
                        s = s & i & j & ","
                    {next}
                Debug.Print s & " after i " & i & ", j " & j
            End Sub
            Main
            """);

        AssertDebugLog(Lines("11,12,21,22, after i 3, j 3"));
    }

    [Fact]
    public async Task Two_loops_on_one_line()
    {
        await Run("""
            Sub Main()
                Dim i As Integer, j As Integer, s As String
                For i = 1 To 2: For j = 1 To 2: s = s & i & j & ",": Next j , i
                Debug.Print s & " after i " & i & ", j " & j
            End Sub
            Main
            """);

        AssertDebugLog(Lines("11,12,21,22, after i 3, j 3"));
    }

    [Fact]
    public async Task Three_loops_closed_by_one_Next_or_by_two()
    {
        await Run("""
            Sub Main()
                Dim i As Integer, j As Integer, k As Integer, s As String
                For i = 1 To 2
                    For j = 1 To 2
                        For k = 1 To 2
                            s = s & i & j & k & ","
                        Next k, j, i
                Debug.Print s & " after " & i & j & k
                s = ""
                For i = 1 To 2
                    For j = 1 To 2
                        For k = 1 To 2
                            s = s & i & j & k & ","
                        Next k
                    Next j, i
                Debug.Print s & " after " & i & j & k
                s = ""
                For i = 1 To 2
                    For j = 1 To 2
                        For k = 1 To 2
                            s = s & i & j & k & ","
                        Next k, j
                    s = s & "|"
                Next i
                Debug.Print s & " after " & i & j & k
            End Sub
            Main
            """);

        AssertDebugLog(Lines("111,112,121,122,211,212,221,222, after 333", "111,112,121,122,211,212,221,222, after 333",
            "111,112,121,122,|211,212,221,222,| after 333"));
    }

    [Fact]
    public async Task Exit_For_leaves_only_the_inner_loop()
    {
        await Run("""
            Sub Main()
                Dim i As Integer, j As Integer, s As String
                For i = 1 To 3
                    For j = 1 To 3
                        If j = 2 Then Exit For
                        s = s & i & j & ","
                    Next j, i
                Debug.Print s & " after i " & i & ", j " & j
            End Sub
            Main
            """);

        AssertDebugLog(Lines("11,21,31, after i 4, j 2"));
    }

    [Fact]
    public async Task For_Each_loops_close_the_same_way()
    {
        await Run("""
            Sub Main()
                Dim a As Variant, b As Variant, arr As Variant, i As Integer, s As String
                arr = Array("x", "y")
                For Each a In arr
                    For Each b In arr
                        s = s & a & b & ","
                    Next b, a
                Debug.Print s
                s = ""
                For Each a In arr
                    For i = 1 To 2
                        s = s & a & i & ","
                    Next i, a
                Debug.Print s & " after i " & i
            End Sub
            Main
            """);

        AssertDebugLog(Lines("xx,xy,yx,yy,", "x1,x2,y1,y2, after i 3"));
    }

    // vb6.exe's "Syntax error" for both (measured).
    [Theory]
    [InlineData("Next j,\n    Next i")]
    [InlineData("Next , i")]
    public async Task A_list_with_a_missing_name_is_still_a_syntax_error(string next)
    {
        var run = () => Run($"""
            Sub Main()
                Dim i As Integer, j As Integer
                For i = 1 To 2
                    For j = 1 To 2
                    {next}
            End Sub
            Main
            """);

        await run.Should().ThrowAsync<VBCompileErrorException>();
    }

    [Fact]
    public async Task Resume_Next_beside_a_Next_list_is_untouched()
    {
        await Run("""
            Sub Main()
                Dim i As Integer, j As Integer
                On Error Resume Next
                For i = 1 To 2
                    For j = 1 To 2
                    Next j, i
                Debug.Print i & j
            End Sub
            Main
            """);

        AssertDebugLog(Lines("33"));
    }
}
