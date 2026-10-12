using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A subscript or an array bound is a Long, coerced as <c>CLng</c> coerces it. Every expectation was measured on
/// vb6.exe; the probe is in docs/vb6-fidelity-oracle.md, "Subscripts, bounds, and a method called from
/// Class_Initialize". Only an Integer, a Byte or an integral string used to be accepted, and anything else stopped
/// the run with an untrappable "Type mismatch": <c>a(i)</c> with <c>i</c> a Long, and <c>a(UBound(a))</c>.
/// </summary>
public class SubscriptCoercionTests : BaseVBTestFixture
{
    // a(j) = j * 10, for j from 0 to 5.
    private const string Filled = "Dim a(5) As Integer\nDim j As Integer\nFor j = 0 To 5: a(j) = j * 10: Next\n";

    [Theory]
    [InlineData("a(CLng(2))", 20)]
    [InlineData("a(2&)", 20)]
    [InlineData("a(UBound(a))", 50)]
    [InlineData("a(1.5)", 20)]            // banker's rounding: 2
    [InlineData("a(2.5)", 20)]            // and 2
    [InlineData("a(2.4)", 20)]
    [InlineData("a(2.6)", 30)]
    [InlineData("a(\"3\")", 30)]
    [InlineData("a(\" 3 \")", 30)]
    [InlineData("a(\"2.5\")", 20)]
    [InlineData("a(Empty)", 0)]
    [InlineData("a(False)", 0)]
    [InlineData("a(CCur(2.5))", 20)]
    [InlineData("a(CSng(3.5))", 40)]
    [InlineData("a(CDate(2))", 20)]
    public async Task A_subscript_is_coerced_as_CLng_does(string expression, int expected)
    {
        await Run($"{Filled}Debug.Print {expression}\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task A_Long_loop_counter_indexes_an_array()
    {
        await Run($"{Filled}Dim i As Long, s As Long\nFor i = 0 To UBound(a): s = s + a(i): Next\nDebug.Print s\n");
        AssertDebugLog([new Vb6Value(150L)]);
    }

    [Theory]
    [InlineData("a(\"x\")", 13L)]
    [InlineData("a(Null)", 94L)]
    [InlineData("a(True)", 9L)]           // -1, out of bounds
    [InlineData("a(-1)", 9L)]
    [InlineData("a(6)", 9L)]
    [InlineData("a(40000)", 9L)]          // a Long, so out of bounds rather than an Integer overflow
    [InlineData("a(3000000000#)", 6L)]    // beyond a Long
    public async Task A_subscript_raises_what_VB6_raises(string expression, long error)
    {
        await Run($"{Filled}On Error Resume Next\nDim c\nc = {expression}\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(error)]);
    }

    [Theory]
    [InlineData("a(2.5) = 99", "a(2)", 99)]
    [InlineData("a(3.5) = 77", "a(4)", 77)]
    public async Task An_assigned_element_is_found_the_same_way(string assignment, string readBack, int expected)
    {
        await Run($"{Filled}{assignment}\nDebug.Print {readBack}\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task Every_subscript_of_a_two_dimensional_array_is_coerced()
    {
        await Run("Dim m(2, 2) As Integer\nm(2, 0) = 200\nm(2, 1) = 201\nDebug.Print m(1.5, 0.5)\nDebug.Print m(CLng(2), CLng(1))\n");
        AssertDebugLog([new Vb6Value(200), new Vb6Value(201)]);
    }

    [Theory]
    [InlineData("ReDim b(CLng(3))", 0L, 3L)]
    [InlineData("ReDim b(2.5)", 0L, 2L)]
    [InlineData("ReDim b(3.5)", 0L, 4L)]
    [InlineData("ReDim b(1.5 To 3.5)", 2L, 4L)]
    [InlineData("ReDim b(\"4\")", 0L, 4L)]
    [InlineData("i = 4\nReDim b(i)", 0L, 4L)]
    public async Task A_ReDim_bound_is_coerced_as_CLng_does(string redim, long lower, long upper)
    {
        await Run($"Dim b() As Integer\nDim i As Long\n{redim}\nDebug.Print LBound(b)\nDebug.Print UBound(b)\n");
        AssertDebugLog([new Vb6Value(lower), new Vb6Value(upper)]);
    }

    [Theory]
    [InlineData("ReDim b(\"x\")", 13L)]
    [InlineData("ReDim b(Null)", 94L)]
    [InlineData("ReDim b(3000000000#)", 6L)]
    public async Task A_ReDim_bound_that_cannot_be_coerced_raises_and_leaves_the_array_alone(string redim, long error)
    {
        await Run($"Dim b() As Integer\nReDim b(1)\nOn Error Resume Next\n{redim}\nDebug.Print Err.Number\nDebug.Print UBound(b)\n");
        AssertDebugLog([new Vb6Value(error), new Vb6Value(1L)]);
    }

    [Theory]
    [InlineData("ReDim b(-1)")]
    [InlineData("ReDim b(5 To 2)")]
    [InlineData("ReDim b(0 To -1)")]
    public async Task A_ReDim_whose_upper_bound_is_below_its_lower_raises_9_and_leaves_the_array_erased(string redim)
    {
        // Not as it was: UBound then raises 9 as well.
        await Run($"Dim b() As Integer\nReDim b(1)\nOn Error Resume Next\n{redim}\nDebug.Print Err.Number\n"
                  + "Err.Clear\nDim u\nu = UBound(b)\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(9L), new Vb6Value(9L)]);
    }

    [Fact]
    public async Task A_Dim_bounded_by_a_Long_constant_is_allocated()
    {
        // The module did not load at all: its declarations' bounds were parsed as plain integers before it ran.
        await Run("Const N As Long = 3\nDim d(N) As Integer\nDim d2(-2& To 2&) As Integer\n"
                  + "Debug.Print UBound(d)\nDebug.Print LBound(d2)\nDebug.Print UBound(d2)\n");
        AssertDebugLog([new Vb6Value(3L), new Vb6Value(-2L), new Vb6Value(2L)]);
    }

    [Theory]
    [InlineData("Const N = 2.5\nDim d(N) As Integer", 0L, 2L)]                 // banker's rounding, as CLng
    [InlineData("Const M = 3.5\nDim d(M) As Integer", 0L, 4L)]
    [InlineData("Dim d(&HA) As Integer", 0L, 10L)]
    [InlineData("Dim d(8 + 1) As Integer", 0L, 9L)]
    [InlineData("Const N = 2.5\nDim d(N - 1) As Integer", 0L, 2L)]
    [InlineData("Const M = 3.5\nDim d(1.5 To M) As Integer", 2L, 4L)]
    [InlineData("Const GRID_MAX = 9\nDim d(0 To GRID_MAX, 0 To GRID_MAX) As Integer", 0L, 9L)]
    public async Task A_module_array_bounded_by_any_constant_expression_loads(string declaration, long lower, long upper)
    {
        // #760: each of these threw a FormatException in the pre-pass, so the module never loaded.
        await Run($"{declaration}\nDebug.Print LBound(d)\nDebug.Print UBound(d)\n");
        AssertDebugLog([new Vb6Value(lower), new Vb6Value(upper)]);
    }

    [Fact]
    public async Task A_procedure_array_bounded_by_a_constant_is_rounded_the_same_way()
    {
        await Run("Show\nSub Show()\n    Const P = 2.5\n    Dim pA(P) As Integer\n    Dim pB(P + 1) As Integer\n"
                  + "    Debug.Print UBound(pA)\n    Debug.Print UBound(pB)\nEnd Sub\n");
        AssertDebugLog([new Vb6Value(2L), new Vb6Value(4L)]);
    }

    [Fact]
    public async Task A_class_field_array_bounded_by_a_constant_loads()
    {
        await RunClasses("Dim g As Grid\nSet g = New Grid\nDebug.Print g.Size\n",
            ("Grid", "Private Const GRID_MAX = 9\nPrivate mCells(0 To GRID_MAX) As Integer\n"
                     + "Public Function Size() As Long\n    Size = UBound(mCells)\nEnd Function\n"));
        AssertDebugLog([new Vb6Value(9L)]);
    }

    [Fact]
    public async Task A_procedure_reads_a_module_array_bounded_by_a_constant()
    {
        await Run("Const N As Long = 2\nDim d(N) As Integer\nd(N) = 7\nShow\nSub Show()\n    Debug.Print d(N)\n    Debug.Print UBound(d)\nEnd Sub\n");
        AssertDebugLog([new Vb6Value(7), new Vb6Value(2L)]);
    }
}
