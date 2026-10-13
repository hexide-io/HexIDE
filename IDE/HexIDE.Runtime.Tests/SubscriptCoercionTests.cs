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

    [Theory]
    [InlineData("c = a(x)")]
    [InlineData("a(x) = 99")]
    [InlineData("ReDim b(x)")]
    public async Task An_omitted_Optional_as_a_subscript_or_a_bound_is_13_not_448(string use)
    {
        // Measured, native and in the IDE, with `Optional x As Variant`. CLng reads an omitted Optional as 448, so a(x)
        // was a(448). Untyped here, because an omitted `As Variant` one reads as Empty in HexIDE (#866).
        await Run($"Use\nSub Use(Optional x)\n    Dim a(500) As Long, b() As Long, c\n    a(0) = 10\n" +
                  $"    On Error Resume Next\n    {use}\n    Debug.Print Err.Number\n    Debug.Print a(0)\nEnd Sub\n");
        AssertDebugLog([new Vb6Value(13L), new Vb6Value(10L)]);
    }

    [Theory]
    [InlineData("v = Empty", "ReDim v(2)", "Variant()")]
    [InlineData("v = 5", "ReDim v(2)", "Variant()")]
    [InlineData("Dim l(1) As Long\nv = l", "ReDim v(2)", "Long()")]
    public async Task A_Variant_can_be_ReDimmed_and_keeps_the_element_type_of_an_array_it_held(string setup, string redim, string typeName)
    {
        // Measured, native and in the IDE. A Variant that had never held an array stopped with an internal error.
        await Run($"Dim v\n{setup}\n{redim}\nDebug.Print TypeName(v)\nDebug.Print UBound(v)\n");
        AssertDebugLog([typeName, new Vb6Value(2L)]);
    }

    [Theory]
    [InlineData("ReDim v(2)", "ReDim v(5 To 4)", 9L, "Variant() 2")]          // one below: kept
    [InlineData("ReDim v(2)", "ReDim v(-1)", 9L, "Variant() 2")]
    [InlineData("ReDim v(2)", "ReDim v(0 To 1, 5 To 4)", 9L, "Variant() 2")]
    [InlineData("ReDim v(2)", "ReDim v(5 To 2)", 7L, "Empty")]                // further below: emptied
    [InlineData("v = 5", "ReDim v(5 To 2)", 7L, "Empty")]
    [InlineData("ReDim v(2)", "ReDim v(0 To 1, 5 To 2)", 7L, "Empty")]
    [InlineData("ReDim v(2)", "ReDim v(5 To 2, 5 To 1)", 7L, "Empty")]
    [InlineData("ReDim v(2)", "ReDim v(5 To 4, 5 To 2)", 9L, "Variant() 2")]      // one dimension just below wins
    [InlineData("ReDim v(2)", "ReDim v(5 To 2, 5 To 4)", 9L, "Variant() 2")]
    [InlineData("ReDim v(2)", "ReDim v(5 To 2, 0 To 1, 5 To 4)", 9L, "Variant() 2")]
    [InlineData("ReDim v(2)", "ReDim v(0 To 1, 5 To 4, 5 To 2)", 9L, "Variant() 2")]
    [InlineData("ReDim v(2)", "ReDim v(5 To 4, 0 To 1)", 9L, "Variant() 2")]
    [InlineData("v = Empty", "ReDim v(5 To 4)", 9L, "Empty")]
    [InlineData("ReDim v(2)", "ReDim v(2147483647)", 7L, "Empty")]
    public async Task A_Variant_ReDimmed_to_an_inverted_or_huge_range_is_kept_or_emptied_as_measured(
        string setup, string redim, long error, string after)
    {
        // Measured, native and in the IDE. A variable declared an array is erased with 9 instead (the test above).
        await Run($"Dim v\n{setup}\nOn Error Resume Next\n{redim}\nDebug.Print Err.Number\n" +
                  "If IsArray(v) Then Debug.Print TypeName(v) & \" \" & UBound(v) Else Debug.Print TypeName(v)\n");
        AssertDebugLog([new Vb6Value(error), after]);
    }

    [Theory]
    [InlineData("ReDim b(2147483647)")]
    [InlineData("ReDim b(1 To 2147483647)")]
    [InlineData("ReDim b(-2147483648# To 2147483647)")]   // VB6 wraps this to no elements and raises nothing: a defect
    public async Task An_array_too_big_to_allocate_is_7_Out_of_memory_and_erases_the_array(string redim)
    {
        // Measured, native and in the IDE, bar the full range. 0 To 2147483647 raised 9: the count wrapped in an int.
        await Run($"Dim b() As Long\nReDim b(2)\nOn Error Resume Next\n{redim}\nDebug.Print Err.Number\n" +
                  "Err.Clear\nDim u\nu = UBound(b)\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(7L), new Vb6Value(9L)]);
    }

    [Theory]
    [InlineData("\"1\"", 0L, "3")]
    [InlineData("Null", 94L, "")]
    [InlineData("\"x\"", 13L, "")]
    [InlineData("3000000000#", 6L, "")]
    [InlineData("True", 9L, "")]
    [InlineData("Empty", 9L, "")]
    [InlineData("1.5", 9L, "")]          // 2: no second dimension
    [InlineData("2.5", 9L, "")]
    [InlineData("-1", 9L, "")]
    [InlineData("32767", 9L, "")]
    [InlineData("32768", 6L, "")]        // beyond an Integer: CInt, not CLng
    [InlineData("40000", 6L, "")]
    [InlineData("70000&", 6L, "")]
    [InlineData("-32769", 6L, "")]
    [InlineData("\"40000\"", 6L, "")]
    public async Task UBounds_dimension_is_coerced_as_CInt(string dimension, long error, string value)
    {
        // Measured, native and in the IDE, with b 1 To 3. It was read as CLng reads it, so 40000 was 9.
        await Run($"Dim b() As Long, c\nReDim b(1 To 3)\nOn Error Resume Next\nc = UBound(b, {dimension})\n" +
                  "Debug.Print Err.Number\nDebug.Print CStr(c)\n");
        AssertDebugLog([new Vb6Value(error), value]);
    }

    [Theory]
    [InlineData("UBound")]
    [InlineData("LBound")]
    public async Task An_omitted_Optional_dimension_is_13_not_the_first(string function)
    {
        // Measured, native and in the IDE. It was read as no dimension given, so dimension 1.
        await Run($"Use\nSub Use(Optional x)\n    Dim b() As Long, c\n    ReDim b(1 To 3)\n    On Error Resume Next\n" +
                  $"    c = {function}(b, x)\n    Debug.Print Err.Number\n    Debug.Print IsEmpty(c)\nEnd Sub\n");
        AssertDebugLog([new Vb6Value(13L), new Vb6Value(true)]);
    }

    [Fact]
    public async Task A_typed_array_parameter_is_erased_and_stays_typed_on_an_inverted_ReDim()
    {
        // Measured, native and in the IDE: 9, an erased Long(), and a later String store into it is 13. It was taken
        // for a Variant, so 7, Empty, and a Variant() that took the String.
        await Run("Dim b() As Long\nReDim b(2)\nTyped b\n" +
                  "Sub Typed(b() As Long)\n    On Error Resume Next\n    ReDim b(5 To 2)\n    Debug.Print Err.Number\n" +
                  "    Debug.Print TypeName(b)\n    Err.Clear\n    ReDim b(1)\n    b(0) = \"x\"\n    Debug.Print Err.Number\n" +
                  "    Debug.Print TypeName(b)\nEnd Sub\n");
        AssertDebugLog([new Vb6Value(9L), "Long()", new Vb6Value(13L), "Long()"]);
    }

    [Theory]
    [InlineData("Dim buf(3) As Byte", "Sub Grow(b() As Byte)", "ReDim b(10)")]     // a typed array parameter
    [InlineData("Dim buf(3)", "Sub Grow(b())", "ReDim b(10)")]                      // an untyped one
    [InlineData("Dim buf(3) As Byte", "Sub Grow(b() As Byte)", "ReDim b(3)")]      // the same bounds again
    public async Task A_fixed_array_ReDimmed_through_a_parameter_is_10_and_kept(string declaration, string sub, string redim)
    {
        // Measured, native and in the IDE: 10, "This array is fixed or temporarily locked", and the caller's array is as it
        // was, element 1 included. It was silently replaced by the callee's new array.
        await Run($"{declaration}\nbuf(1) = 9\nGrow buf\nDebug.Print UBound(buf)\nDebug.Print CLng(buf(1))\n" +
                  $"{sub}\n    On Error Resume Next\n    {redim}\n    Debug.Print Err.Number\n    Debug.Print UBound(b)\nEnd Sub\n");

        AssertDebugLog([new Vb6Value(10L), new Vb6Value(3L), new Vb6Value(3L), new Vb6Value(9L)]);
    }

    [Theory]
    [InlineData("ReDim b(\"x\")", 13L)]       // the bound is converted first
    [InlineData("ReDim b(5 To 2)", 10L)]      // then the 10, before an inverted range's 9
    public async Task A_fixed_array_parameters_ReDim_converts_its_bounds_then_raises_10(string redim, long error)
    {
        // Measured, native and in the IDE (the second in both): the array is kept, 0 To 3.
        await Run("Dim buf(3) As Byte\nGrow buf\nDebug.Print UBound(buf)\n" +
                  $"Sub Grow(b() As Byte)\n    On Error Resume Next\n    {redim}\n    Debug.Print Err.Number\nEnd Sub\n");

        AssertDebugLog([new Vb6Value(error), new Vb6Value(3L)]);
    }

    [Theory]
    [InlineData("Go\nSub Go()\n    Dim buf(3) As Byte\n    ReDim buf(10)\nEnd Sub")]
    [InlineData("Go\nPrivate mbuf(3) As Byte\nSub Go()\n    ReDim mbuf(10)\nEnd Sub")]
    public async Task A_fixed_array_ReDimmed_by_name_is_VB6s_Array_already_dimensioned(string code)
    {
        // Measured with /make: a local and a module-level one alike do not compile.
        var run = () => Run(code + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Array already dimensioned*");
    }

    [Fact]
    public async Task A_module_fixed_array_ReDimmed_through_a_parameter_is_10()
    {
        await Run("Go\nPrivate mbuf(3) As Byte\nSub Go()\n    Grow mbuf\n    Debug.Print UBound(mbuf)\nEnd Sub\n" +
                  "Sub Grow(b() As Byte)\n    On Error Resume Next\n    ReDim b(10)\n    Debug.Print Err.Number\nEnd Sub\n");

        AssertDebugLog([new Vb6Value(10L), new Vb6Value(3L)]);
    }

    [Theory]
    [InlineData("Dim d() As Long\nd = fx")]       // a dynamic array
    [InlineData("Dim d\nd = fx")]                 // a Variant
    public async Task An_array_Let_copies_the_array(string assignment)
    {
        // Measured: after the copy, d(1) = 99 leaves fx(1) at 7, for a dynamic array and a Variant alike. HexIDE shared the
        // array, so fx(1) read 99.
        await Run($"Dim fx(3) As Long\nfx(1) = 7\n{assignment}\nd(1) = 99\nDebug.Print fx(1)\nDebug.Print d(1)\n");

        AssertDebugLog([new Vb6Value(7L), new Vb6Value(99L)]);
    }

    [Theory]
    [InlineData("Dim fx(3) As Long, d() As Long\nfx(1) = 7\nd = fx\nReDim d(10)", "")]
    [InlineData("Dim fx(3) As Long, d\nfx(1) = 7\nd = fx\nReDim d(10)", "")]                      // into a Variant
    [InlineData("Private fx(3) As Long, d() As Long\nfx(1) = 7\nd = fx\nReDim d(10)", "")]         // at module level
    [InlineData("Dim fx(3) As Long, d() As Long\nfx(1) = 7\nd = fx\nGrow d", "Sub Grow(b() As Long)\n    ReDim b(10)\nEnd Sub\n")]
    public async Task A_copy_of_a_fixed_array_can_be_ReDimmed(string setup, string subs)
    {
        // Measured: the first two here, natively; the first, third and fourth by #873's review. The copy is dynamic, and
        // the fixed original keeps its bounds and its element. Sharing the array made the copy fixed, so it was "Array
        // already dimensioned", or a 10 through the parameter.
        await Run($"{setup}\nDebug.Print UBound(d)\nDebug.Print UBound(fx)\nDebug.Print fx(1)\n{subs}");

        AssertDebugLog([new Vb6Value(10L), new Vb6Value(3L), new Vb6Value(7L)]);
    }

    [Theory]
    [InlineData("Dim d() As Long")]
    [InlineData("Dim d")]
    public async Task An_array_Let_copies_every_dimension(string declaration)
    {
        // Measured, native: after `d = fx`, d(1, 2) = 99 leaves fx(1, 2) at 7, for a dynamic array and a Variant alike.
        await Run($"Dim fx(1, 2) As Long\nfx(1, 2) = 7\n{declaration}\nd = fx\nd(1, 2) = 99\nDebug.Print fx(1, 2)\nDebug.Print d(1, 2)\n");

        AssertDebugLog([new Vb6Value(7L), new Vb6Value(99L)]);
    }

    [Fact]
    public async Task A_Variant_parameter_holding_a_fixed_array_ReDimmed_is_10_and_kept()
    {
        // Measured, native: a ByRef Variant parameter shares the caller's fixed array, so its ReDim is 10, "This array is
        // fixed or temporarily locked", and the array keeps its bounds and its element. It was the compile error "Array
        // already dimensioned", which VB6 cannot give: what a Variant holds is not known until it runs.
        await Run("On Error Resume Next\nDim buf(3) As Byte\nbuf(1) = 9\nG buf\nDebug.Print Err.Number\n" +
                  "Debug.Print UBound(buf)\nDebug.Print CLng(buf(1))\nSub G(v)\n    ReDim v(10)\nEnd Sub\n");

        AssertDebugLog([new Vb6Value(10L), new Vb6Value(3L), new Vb6Value(9L)]);
    }

    [Fact]
    public async Task A_ByVal_Variant_parameter_gets_a_copy_it_can_ReDim()
    {
        // Measured, native: the callee's ReDim gives it 0 To 10, and the caller's array is untouched.
        await Run("On Error Resume Next\nDim buf(3) As Byte\nbuf(1) = 9\nGV buf\nDebug.Print Err.Number\n" +
                  "Debug.Print UBound(buf)\nDebug.Print CLng(buf(1))\n" +
                  "Sub GV(ByVal v)\n    ReDim v(10)\n    Debug.Print UBound(v)\nEnd Sub\n");

        AssertDebugLog([new Vb6Value(10L), new Vb6Value(0L), new Vb6Value(3L), new Vb6Value(9L)]);
    }

    private const string Plain = "Public Tag As Long\n";
    private const string Widget = "Public Function Value() As Long\nAttribute Value.VB_UserMemId = 0\n    Value = 2\nEnd Function\n";

    [Theory]
    [InlineData("x = a(k)", 438L)]   // an object with no default member
    [InlineData("x = a(n)", 91L)]    // Nothing
    public async Task An_object_as_a_subscript_is_VB6s_error(string use, long error)
    {
        // Measured, native: 438 and 91, and x keeps its value.
        await RunClasses("Dim a(5) As Long, x As Long\na(2) = 7\nDim k As Plain\nSet k = New Plain\nDim n As Plain\n" +
                         $"On Error Resume Next\nx = -1\n{use}\nDebug.Print x\nDebug.Print Err.Number\n", ("Plain", Plain));

        AssertDebugLog([new Vb6Value(-1L), new Vb6Value(error)]);
    }

    [Theory]
    [InlineData("x = a(w)")]
    [InlineData("a(w) = 5")]
    public async Task An_object_with_a_default_member_as_a_subscript_is_refused_until_174(string use)
    {
        // Measured, native: VB6 reads Widget's default member, 2, so a(w) is 7 and a(w) = 5 writes a(2). HexIDE cannot read
        // a class's default member yet, so it refuses, untrappably: under On Error Resume Next a trappable error would
        // leave x, or a(2), as it was.
        var run = () => RunClasses("Dim a(5) As Long, x As Long\na(2) = 7\nDim w As Widget\nSet w = New Widget\n" +
                                   $"On Error Resume Next\n{use}\n", ("Widget", Widget));

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#174*");
    }

    [Fact]
    public async Task A_dynamic_array_ReDimmed_through_a_parameter_is_resized()
    {
        // Measured, the control: no error, and the caller sees 0 To 10.
        await Run("Dim buf() As Byte\nReDim buf(3)\nGrow buf\nDebug.Print UBound(buf)\n" +
                  "Sub Grow(b() As Byte)\n    ReDim b(10)\nEnd Sub\n");

        AssertDebugLog([new Vb6Value(10L)]);
    }

    [Fact]
    public async Task A_fixed_array_Erased_through_a_parameter_keeps_its_bounds_and_loses_its_values()
    {
        // Measured, native and in the IDE: no error, UBound 3, and element 1 back to 0, in the callee and the caller.
        await Run("Dim buf(3) As Byte\nbuf(1) = 9\nClear buf\nDebug.Print UBound(buf)\nDebug.Print buf(1)\n" +
                  "Sub Clear(b() As Byte)\n    Erase b\nEnd Sub\n");

        AssertDebugLog([new Vb6Value(3L), new Vb6Value((byte)0)]);
    }

    [Theory]
    [InlineData("Dim u() As Variant\nReDim u(2)\nP u\nSub P(b())", 9L, "Variant()")]       // an untyped array parameter
    [InlineData("Dim v\nReDim v(2)\nP v\nSub P(b As Variant)", 7L, "Empty")]           // a Variant parameter
    [InlineData("Dim v\nReDim v(2)\nP v\nSub P(b)", 7L, "Empty")]
    public async Task A_parameter_is_ReDimmed_as_what_it_was_declared(string head, long error, string typeName)
    {
        // Measured, native and in the IDE, for the first two.
        await Run($"{head}\n    On Error Resume Next\n    ReDim b(5 To 2)\n    Debug.Print Err.Number\n" +
                  "    Debug.Print TypeName(b)\nEnd Sub\n");
        AssertDebugLog([new Vb6Value(error), typeName]);
    }

    [Theory]
    [InlineData("Go\nSub Go()\n    Dim v\n    ReDim v(2)\n    On Error Resume Next\n    ReDim v(5 To 2)\n    Debug.Print Err.Number\nEnd Sub\n")]   // a procedure's Dim
    [InlineData("x = 5\nOn Error Resume Next\nReDim x(5 To 2)\nDebug.Print Err.Number\n")]   // a variable created on first use
    public async Task Every_kind_of_Variant_is_a_Variant_to_ReDim(string code)
    {
        // Each is a Variant, so 7 and emptied, as `Dim v` is (measured). Only a slot known to be one is treated as one.
        await Run(code);
        AssertDebugLog([new Vb6Value(7L)]);
    }

    [Fact]
    public async Task A_module_Variant_is_a_Variant_to_ReDim_when_the_declarations_have_not_run()
    {
        // A form's run enters through ExecuteSub without running the declarations (#256), so only the pre-pass declared v.
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv,
            "Private v\nSub Go()\n    ReDim v(2)\n    On Error Resume Next\n    ReDim v(5 To 2)\n    Debug.Print Err.Number\nEnd Sub\n", "Form1");

        await vb.ExecuteSub("Go", null, true);

        AssertDebugLog([new Vb6Value(7L)]);
    }

    [Fact]
    public async Task A_Set_of_an_element_finds_it_the_same_way()
    {
        // Measured, native and in the IDE: a Long index, and Set wa(2.5) and Set wa(0.6) reach wa(2) and wa(1).
        await RunClasses(
            "Dim wa(3) As Thing, w As Thing, i As Long\nSet w = New Thing\ni = 2\nSet wa(i) = w\nDebug.Print wa(2) Is w\n" +
            "Set wa(2.5) = Nothing\nDebug.Print wa(2) Is Nothing\nSet wa(0.6) = w\nDebug.Print wa(1) Is w\n",
            ("Thing", "Public Tag As Long\n"));
        AssertDebugLog([new Vb6Value(true), new Vb6Value(true), new Vb6Value(true)]);
    }
}
