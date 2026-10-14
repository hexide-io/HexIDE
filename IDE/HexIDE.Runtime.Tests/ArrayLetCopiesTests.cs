using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// VB6 copies an array wherever it stores one, and HexIDE shared it, so a write through either name reached the other
/// (#970). Every expectation here was measured on vb6.exe, built as native code and as p-code and run in the IDE, all
/// three agreeing, unless a row says otherwise; the oracle's "Arrays copy on Let" has the probe and the tables.
/// </summary>
public class ArrayLetCopiesTests : BaseVBTestFixture
{
    private string[] Log => debug.Select(v => v.Value?.ToString() ?? "").ToArray();

    [Theory]
    [InlineData("Dim f(3) As Long, d() As Long")]   // into a dynamic array
    [InlineData("Dim f(3) As Long, d")]             // into a Variant
    public async Task An_array_Let_copies_the_array(string declaration)
    {
        await Run($"{declaration}\nf(1) = 7\nd = f\nd(1) = 99\nDebug.Print f(1)\nDebug.Print d(1)\n");

        Log.Should().Equal("7", "99");
    }

    [Theory]
    [InlineData("Dim f(1, 2) As Long, d() As Long")]
    [InlineData("Dim f(1, 2) As Long, d")]
    public async Task An_array_Let_copies_every_dimension(string declaration)
    {
        await Run($"{declaration}\nf(1, 2) = 7\nd = f\nd(1, 2) = 99\nDebug.Print f(1, 2)\nDebug.Print d(1, 2)\n");

        Log.Should().Equal("7", "99");
    }

    [Fact]
    public async Task A_Variant_holding_an_array_copied_into_another_is_its_own()
    {
        await Run("Dim v, w\nv = Array(1, 2)\nw = v\nw(0) = 77\nDebug.Print v(0)\n");

        Log.Should().Equal("1");
    }

    private const string Module = "Dim modArr(3) As Long\n";

    [Fact]
    public async Task A_Function_returning_a_module_array_returns_a_copy()
    {
        await Run(Module + "Dim d() As Long, v\nmodArr(1) = 7\nd = RetFixed()\nd(1) = 99\nDebug.Print modArr(1) & \" \" & d(1)\n"
            + "v = RetVariant()\nv(1) = 98\nDebug.Print modArr(1) & \" \" & v(1)\n"
            + "Function RetFixed() As Long()\n    RetFixed = modArr\nEnd Function\n"
            + "Function RetVariant()\n    RetVariant = modArr\nEnd Function\n");

        Log.Should().Equal("7 99", "7 98");
    }

    [Fact]
    public async Task A_ByVal_parameter_gets_a_copy_and_a_ByRef_Variant_shares()
    {
        await Run(Module + "modArr(1) = 7\nByValV modArr\nDebug.Print modArr(1)\nByRefV modArr\nDebug.Print modArr(1)\n"
            + "Sub ByValV(ByVal v)\n    v(1) = 55\nEnd Sub\nSub ByRefV(v As Variant)\n    v(1) = 66\nEnd Sub\n");

        Log.Should().Equal("7", "66");
    }

    [Fact]
    public async Task An_element_takes_a_copy()
    {
        await Run("Dim inner(1) As Long, outer(1), t\ninner(0) = 1\nouter(0) = inner\ninner(0) = 50\nt = outer(0)\nDebug.Print t(0)\n");

        Log.Should().Equal("1");
    }

    [Fact]
    public async Task The_row_buffer_idiom_keeps_each_row()
    {
        await Run("Dim row(0) As Long, rows(2), i As Long, a, b, c\nFor i = 0 To 2\n    row(0) = i\n    rows(i) = row\nNext\n"
            + "a = rows(0): b = rows(1): c = rows(2)\nDebug.Print a(0) & \"|\" & b(0) & \"|\" & c(0)\n");

        Log.Should().Equal("0|1|2");
    }

    [Fact]
    public async Task A_For_Each_variable_holding_an_array_is_a_copy()
    {
        await Run("Dim nest(1), e, a, b\nnest(0) = Array(1, 2)\nnest(1) = Array(3, 4)\nFor Each e In nest\n    e(0) = 99\nNext\n"
            + "a = nest(0): b = nest(1)\nDebug.Print a(0) & \" \" & b(0)\n");

        Log.Should().Equal("1 3");
    }

    [Fact]
    public async Task An_array_stored_into_its_own_element_is_a_copy()
    {
        // `v(1) = v` stores a copy, so no array holds itself and the next Let has nothing to recurse through. Sharing made a
        // cycle that overflowed the stack and took the host down.
        await Run("On Error Resume Next\nDim v, w, inner\nv = Array(1, 2)\nv(1) = v\nw = v\ninner = w(1)\n"
            + "Debug.Print Err.Number & \" \" & UBound(inner) & \" \" & inner(1)\n");

        Log.Should().Equal("0 1 2");
    }

    [Fact]
    public async Task Erase_of_a_copy_leaves_the_original()
    {
        await Run("On Error Resume Next\nDim fx(3) As Long, d() As Long, x\nfx(1) = 7\nd = fx\nErase d\n"
            + "Debug.Print fx(1) & \" \" & UBound(fx)\nx = UBound(d)\nDebug.Print Err.Number\n");

        Log.Should().Equal("7 3", "9");
    }

    [Fact]
    public async Task A_Variant_sort_routine_sorts_its_callers_array()
    {
        // `Sub BubbleSort(arr As Variant)` is the idiom a ByRef Variant's sharing exists for: #873's fifth round copied on Let
        // without it and left the array unsorted.
        await Run("Dim a(4) As Long, i As Long, s\na(0) = 5: a(1) = 3: a(2) = 9: a(3) = 1: a(4) = 7\nBubbleSort a\n"
            + "For i = 0 To 4\n    s = s & a(i)\nNext\nDebug.Print s\n"
            + "Sub BubbleSort(arr As Variant)\n    Dim i As Long, j As Long, t\n"
            + "    For i = LBound(arr) To UBound(arr) - 1\n        For j = LBound(arr) To UBound(arr) - 1 - i\n"
            + "            If arr(j) > arr(j + 1) Then t = arr(j): arr(j) = arr(j + 1): arr(j + 1) = t\n"
            + "        Next\n    Next\nEnd Sub\n");

        Log.Should().Equal("13579");
    }

    [Fact]
    public async Task A_ByRef_Variant_parameter_ReDim_reaches_the_callers_array()
    {
        // Measured for #873's sixth round, native and in the IDE: given a fixed array, 10 and the array kept; given a dynamic
        // one, the caller's array is the one ReDimmed. The parameter had a slot of its own, so neither reached the caller.
        await Run("On Error Resume Next\nDim buf(3) As Byte, dyn() As Long\nbuf(1) = 9\nGA buf\nDebug.Print Err.Number & \" \" & UBound(buf) & \" \" & buf(1)\n"
            + "Err.Clear\nReDim dyn(3)\nGA dyn\nDebug.Print Err.Number & \" \" & UBound(dyn)\n"
            + "Sub GA(v As Variant)\n    ReDim v(10)\nEnd Sub\n");

        Log.Should().Equal("10 3 9", "0 10");
    }

    [Theory]
    [InlineData("-1", "9 3")]
    [InlineData("5 To 4", "9 3")]
    [InlineData("5 To 2, 5 To 4", "9 3")]
    [InlineData("5 To 2", "7 -")]
    [InlineData("0 To -5", "7 -")]
    [InlineData("0 To 1, 5 To 2", "7 -")]
    public async Task An_untyped_parameter_ReDims_by_the_Variant_rule_whatever_it_shares(string bounds, string expected)
    {
        // Measured for #873's seventh review, native. One below the lower bound is 9 and the caller's array is kept; further
        // below is 7 and it is erased. The parameter shares the caller's declared-array slot, whose rule erased it with 9.
        await Run($"On Error Resume Next\nDim a() As Long, hi\nReDim a(3)\nG a\nErr.Clear\nhi = \"-\"\nhi = UBound(a)\nDebug.Print Err.Number & \" \" & hi\n"
            + $"Sub G(v)\n    On Error Resume Next\n    ReDim v({bounds})\n    Debug.Print Err.Number\nEnd Sub\n");

        var parts = expected.Split(' ');
        Log.Should().Equal(parts[0], (parts[1] == "-" ? "9" : "0") + " " + parts[1]);
    }

    [Fact]
    public async Task A_fixed_array_given_an_array_through_a_parameter_is_13_and_kept()
    {
        await Run("On Error Resume Next\nDim f3(3) As Long\nf3(1) = 9\nSB f3\nDebug.Print Err.Number & \" \" & UBound(f3) & \" \" & f3(1)\n"
            + "Sub SB(b() As Long)\n    Dim gO() As Long\n    ReDim gO(5)\n    b = gO\nEnd Sub\n");

        Log.Should().Equal("13 3 9");
    }

    [Fact]
    public async Task A_fixed_array_assigned_an_array_named_directly_is_VB6s_compile_error()
    {
        var run = () => Run("Dim t(3) As Long, a() As Long\nReDim a(2)\nt = a\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Can't assign to array*");
    }

    private const string Foo = "Public Name As String\nPrivate Sub Class_Terminate()\n    Debug.Print \"  Terminate \" & Name\nEnd Sub\n";

    [Fact]
    public async Task A_copied_array_shares_its_objects_and_each_holder_keeps_them_alive()
    {
        await RunClasses("Scope1\nDebug.Print \"after Scope1\"\nScope2\nDebug.Print \"after Scope2\"\nScope3\nDebug.Print \"after Scope3\"\n"
            + "Sub Scope1()\n    Dim a(0) As Foo, b() As Foo\n    Set a(0) = New Foo\n    a(0).Name = \"A\"\n    b = a\n"
            + "    Debug.Print \"Scope1: b = a; b(0) Is a(0) \" & (b(0) Is a(0))\n    Set b(0) = New Foo\n    b(0).Name = \"B\"\n"
            + "    Debug.Print \"Scope1: Set b(0) = New Foo; a(0).Name \" & a(0).Name\nEnd Sub\n"
            + "Sub Scope2()\n    Dim a(0) As Foo, v\n    Set a(0) = New Foo\n    a(0).Name = \"C\"\n    v = a\n    Set a(0) = Nothing\n"
            + "    Debug.Print \"Scope2: v = a, Set a(0) = Nothing; v(0).Name \" & v(0).Name\nEnd Sub\n"
            + "Sub Scope3()\n    Dim a(0) As Foo\n    Set a(0) = New Foo\n    a(0).Name = \"D\"\n    Hold a\n    Debug.Print \"Scope3: after Hold a\"\nEnd Sub\n"
            + "Sub Hold(ByVal v)\n    Set v(0) = Nothing\n    Debug.Print \"Hold: Set v(0) = Nothing\"\nEnd Sub\n",
            ("Foo", Foo));

        Log.Should().Equal(
            "Scope1: b = a; b(0) Is a(0) True",
            "Scope1: Set b(0) = New Foo; a(0).Name A",
            "  Terminate A",
            "  Terminate B",
            "after Scope1",
            "Scope2: v = a, Set a(0) = Nothing; v(0).Name C",
            "  Terminate C",
            "after Scope2",
            "Hold: Set v(0) = Nothing",
            "Scope3: after Hold a",
            "  Terminate D",
            "after Scope3");
    }

    [Fact]
    public async Task An_object_array_a_Function_returns_is_released_where_VB6_releases_it()
    {
        // Stored, its object terminates at the end of the Sub that stored it; discarded, right after the call. Measured.
        await RunClasses("ScopeA\nDebug.Print \"after ScopeA\"\nScopeB\nDebug.Print \"after ScopeB\"\nFunction MakeArr(ByVal nm As String)\n    Dim a(0) As Foo\n    Set a(0) = New Foo\n    a(0).Name = nm\n    MakeArr = a\nEnd Function\nSub ScopeA()\n    Dim x\n    x = MakeArr(\"M1\")\n    Debug.Print \"ScopeA: x(0).Name \" & x(0).Name\nEnd Sub\nSub ScopeB()\n    MakeArr \"M2\"\n    Debug.Print \"ScopeB: after MakeArr\"\nEnd Sub\nSub ScopeC()\n    Dim a(1) As Foo, o\n    Set a(0) = New Foo\n    a(0).Name = \"F0\"\n    Set a(1) = New Foo\n    a(1).Name = \"F1\"\n    For Each o In a\n        Debug.Print \"ScopeC: loop \" & o.Name\n    Next\n    Debug.Print \"ScopeC: after the loop, TypeName(o) \" & TypeName(o)\n    Set a(0) = Nothing\n    Debug.Print \"ScopeC: after Set a(0) = Nothing\"\n    Set a(1) = Nothing\n    Debug.Print \"ScopeC: after Set a(1) = Nothing\"\nEnd Sub\nSub ScopeD()\n    On Error Resume Next\n    Dim v(0)\n    Set v(0) = New Foo\n    v(0).Name = \"E0\"\n    v(0) = 5\n    Debug.Print \"ScopeD: after v(0) = 5, err \" & Err.Number & \", TypeName(v(0)) \" & TypeName(v(0))\nEnd Sub\nSub ScopeE()\n    On Error Resume Next\n    Dim w\n    Set w = New Foo\n    w.Name = \"W0\"\n    w = 5\n    Debug.Print \"ScopeE: after w = 5, err \" & Err.Number & \", TypeName(w) \" & TypeName(w)\nEnd Sub\n", ("Foo", Foo));

        Log.Should().Equal(
            "ScopeA: x(0).Name M1",
            "  Terminate M1",
            "after ScopeA",
            "  Terminate M2",
            "ScopeB: after MakeArr",
            "after ScopeB");
    }

    [Fact]
    public async Task A_For_Each_variable_is_Empty_once_the_loop_runs_out()
    {
        // So it holds neither object afterwards, and each terminates when the array lets go of it. Measured.
        await RunClasses("ScopeC\nDebug.Print \"after ScopeC\"\nFunction MakeArr(ByVal nm As String)\n    Dim a(0) As Foo\n    Set a(0) = New Foo\n    a(0).Name = nm\n    MakeArr = a\nEnd Function\nSub ScopeA()\n    Dim x\n    x = MakeArr(\"M1\")\n    Debug.Print \"ScopeA: x(0).Name \" & x(0).Name\nEnd Sub\nSub ScopeB()\n    MakeArr \"M2\"\n    Debug.Print \"ScopeB: after MakeArr\"\nEnd Sub\nSub ScopeC()\n    Dim a(1) As Foo, o\n    Set a(0) = New Foo\n    a(0).Name = \"F0\"\n    Set a(1) = New Foo\n    a(1).Name = \"F1\"\n    For Each o In a\n        Debug.Print \"ScopeC: loop \" & o.Name\n    Next\n    Debug.Print \"ScopeC: after the loop, TypeName(o) \" & TypeName(o)\n    Set a(0) = Nothing\n    Debug.Print \"ScopeC: after Set a(0) = Nothing\"\n    Set a(1) = Nothing\n    Debug.Print \"ScopeC: after Set a(1) = Nothing\"\nEnd Sub\nSub ScopeD()\n    On Error Resume Next\n    Dim v(0)\n    Set v(0) = New Foo\n    v(0).Name = \"E0\"\n    v(0) = 5\n    Debug.Print \"ScopeD: after v(0) = 5, err \" & Err.Number & \", TypeName(v(0)) \" & TypeName(v(0))\nEnd Sub\nSub ScopeE()\n    On Error Resume Next\n    Dim w\n    Set w = New Foo\n    w.Name = \"W0\"\n    w = 5\n    Debug.Print \"ScopeE: after w = 5, err \" & Err.Number & \", TypeName(w) \" & TypeName(w)\nEnd Sub\n", ("Foo", Foo));

        Log.Should().Equal(
            "ScopeC: loop F0",
            "ScopeC: loop F1",
            "ScopeC: after the loop, TypeName(o) Empty",
            "  Terminate F0",
            "ScopeC: after Set a(0) = Nothing",
            "  Terminate F1",
            "ScopeC: after Set a(1) = Nothing",
            "after ScopeC");
    }

    [Fact]
    public async Task A_Let_over_an_object_in_an_element_or_a_variable_releases_it()
    {
        // With no default member, the Let replaces the object, which terminates at the Let. Measured.
        await RunClasses("ScopeD\nDebug.Print \"after ScopeD\"\nScopeE\nDebug.Print \"after ScopeE\"\nFunction MakeArr(ByVal nm As String)\n    Dim a(0) As Foo\n    Set a(0) = New Foo\n    a(0).Name = nm\n    MakeArr = a\nEnd Function\nSub ScopeA()\n    Dim x\n    x = MakeArr(\"M1\")\n    Debug.Print \"ScopeA: x(0).Name \" & x(0).Name\nEnd Sub\nSub ScopeB()\n    MakeArr \"M2\"\n    Debug.Print \"ScopeB: after MakeArr\"\nEnd Sub\nSub ScopeC()\n    Dim a(1) As Foo, o\n    Set a(0) = New Foo\n    a(0).Name = \"F0\"\n    Set a(1) = New Foo\n    a(1).Name = \"F1\"\n    For Each o In a\n        Debug.Print \"ScopeC: loop \" & o.Name\n    Next\n    Debug.Print \"ScopeC: after the loop, TypeName(o) \" & TypeName(o)\n    Set a(0) = Nothing\n    Debug.Print \"ScopeC: after Set a(0) = Nothing\"\n    Set a(1) = Nothing\n    Debug.Print \"ScopeC: after Set a(1) = Nothing\"\nEnd Sub\nSub ScopeD()\n    On Error Resume Next\n    Dim v(0)\n    Set v(0) = New Foo\n    v(0).Name = \"E0\"\n    v(0) = 5\n    Debug.Print \"ScopeD: after v(0) = 5, err \" & Err.Number & \", TypeName(v(0)) \" & TypeName(v(0))\nEnd Sub\nSub ScopeE()\n    On Error Resume Next\n    Dim w\n    Set w = New Foo\n    w.Name = \"W0\"\n    w = 5\n    Debug.Print \"ScopeE: after w = 5, err \" & Err.Number & \", TypeName(w) \" & TypeName(w)\nEnd Sub\n", ("Foo", Foo));

        Log.Should().Equal(
            "  Terminate E0",
            "ScopeD: after v(0) = 5, err 0, TypeName(v(0)) Integer",
            "after ScopeD",
            "  Terminate W0",
            "ScopeE: after w = 5, err 0, TypeName(w) Integer",
            "after ScopeE");
    }

    [Fact]
    public async Task Exit_For_leaves_the_variable_holding_its_element()
    {
        // X0 outlives Set a(0) = Nothing, held by o, and goes first at End Sub, a scalar before an array. Measured.
        await RunClasses("ScopeF\nDebug.Print \"after ScopeF\"\nSub ScopeF()\n    Dim a(1) As Foo, o\n    Set a(0) = New Foo\n    a(0).Name = \"X0\"\n    Set a(1) = New Foo\n    a(1).Name = \"X1\"\n    For Each o In a\n        Debug.Print \"ScopeF: loop \" & o.Name\n        Exit For\n    Next\n    Debug.Print \"ScopeF: after Exit For, TypeName(o) \" & TypeName(o) & \" \" & o.Name\n    Set a(0) = Nothing\n    Debug.Print \"ScopeF: after Set a(0) = Nothing\"\nEnd Sub\nSub ScopeG()\n    Dim e, n(1) As Long, s\n    For Each e In Array(1, 2)\n    Next\n    Debug.Print \"ScopeG: after For Each over Array(1, 2), TypeName(e) \" & TypeName(e)\n    n(0) = 3: n(1) = 4\n    For Each e In n\n    Next\n    Debug.Print \"ScopeG: after For Each over a Long(), TypeName(e) \" & TypeName(e)\n    e = 9\n    s = Split(\"\")\n    For Each e In s\n    Next\n    Debug.Print \"ScopeG: after For Each over Split of nothing, e held 9, TypeName(e) \" & TypeName(e)\nEnd Sub\n", ("Foo", Foo));

        Log.Should().Equal(
            "ScopeF: loop X0",
            "ScopeF: after Exit For, TypeName(o) Foo X0",
            "ScopeF: after Set a(0) = Nothing",
            "  Terminate X0",
            "  Terminate X1",
            "after ScopeF");
    }

    [Fact]
    public async Task A_For_Each_over_any_array_leaves_its_variable_Empty()
    {
        // Over an object-free Variant array and a Long() too, and over an empty array, which still empties it. Measured.
        await RunClasses("ScopeG\nSub ScopeF()\n    Dim a(1) As Foo, o\n    Set a(0) = New Foo\n    a(0).Name = \"X0\"\n    Set a(1) = New Foo\n    a(1).Name = \"X1\"\n    For Each o In a\n        Debug.Print \"ScopeF: loop \" & o.Name\n        Exit For\n    Next\n    Debug.Print \"ScopeF: after Exit For, TypeName(o) \" & TypeName(o) & \" \" & o.Name\n    Set a(0) = Nothing\n    Debug.Print \"ScopeF: after Set a(0) = Nothing\"\nEnd Sub\nSub ScopeG()\n    Dim e, n(1) As Long, s\n    For Each e In Array(1, 2)\n    Next\n    Debug.Print \"ScopeG: after For Each over Array(1, 2), TypeName(e) \" & TypeName(e)\n    n(0) = 3: n(1) = 4\n    For Each e In n\n    Next\n    Debug.Print \"ScopeG: after For Each over a Long(), TypeName(e) \" & TypeName(e)\n    e = 9\n    s = Split(\"\")\n    For Each e In s\n    Next\n    Debug.Print \"ScopeG: after For Each over Split of nothing, e held 9, TypeName(e) \" & TypeName(e)\nEnd Sub\n", ("Foo", Foo));

        Log.Should().Equal(
            "ScopeG: after For Each over Array(1, 2), TypeName(e) Empty",
            "ScopeG: after For Each over a Long(), TypeName(e) Empty",
            "ScopeG: after For Each over Split of nothing, e held 9, TypeName(e) Empty");
    }

    [Fact]
    public async Task End_Sub_releases_scalar_locals_before_arrays()
    {
        // Variants and object references first, then arrays, each in declaration order. Measured, native and p-code.
        await RunClasses("ScopeH\nDebug.Print \"after ScopeH\"\nScopeI\nDebug.Print \"after ScopeI\"\nScopeJ\nDebug.Print \"after ScopeJ\"\nScopeK\nDebug.Print \"after ScopeK\"\nScopeM\nDebug.Print \"after ScopeM\"\nScopeN\nDebug.Print \"after ScopeN\"\nSub ScopeH()\n    Dim a(0) As Foo, o\n    Set a(0) = New Foo: a(0).Name = \"H-array\"\n    Set o = New Foo: o.Name = \"H-variant\"\n    Debug.Print \"ScopeH: end (Dim a(0) As Foo, o)\"\nEnd Sub\nSub ScopeI()\n    Dim o, a(0) As Foo\n    Set o = New Foo: o.Name = \"I-variant\"\n    Set a(0) = New Foo: a(0).Name = \"I-array\"\n    Debug.Print \"ScopeI: end (Dim o, a(0) As Foo)\"\nEnd Sub\nSub ScopeJ()\n    Dim p As Foo, a(0) As Foo\n    Set p = New Foo: p.Name = \"J-object\"\n    Set a(0) = New Foo: a(0).Name = \"J-array\"\n    Debug.Print \"ScopeJ: end (Dim p As Foo, a(0) As Foo)\"\nEnd Sub\nSub ScopeK()\n    Dim a(0) As Foo, p As Foo\n    Set a(0) = New Foo: a(0).Name = \"K-array\"\n    Set p = New Foo: p.Name = \"K-object\"\n    Debug.Print \"ScopeK: end (Dim a(0) As Foo, p As Foo)\"\nEnd Sub\nSub ScopeM()\n    Dim p As Foo, q As Foo\n    Set p = New Foo: p.Name = \"M-first\"\n    Set q = New Foo: q.Name = \"M-second\"\n    Debug.Print \"ScopeM: end (Dim p As Foo, q As Foo)\"\nEnd Sub\nSub ScopeN()\n    Dim a(0) As Foo, b(0) As Foo\n    Set a(0) = New Foo: a(0).Name = \"N-first\"\n    Set b(0) = New Foo: b(0).Name = \"N-second\"\n    Debug.Print \"ScopeN: end (Dim a(0) As Foo, b(0) As Foo)\"\nEnd Sub\n", ("Foo", Foo));

        Log.Should().Equal(
            "ScopeH: end (Dim a(0) As Foo, o)",
            "  Terminate H-variant",
            "  Terminate H-array",
            "after ScopeH",
            "ScopeI: end (Dim o, a(0) As Foo)",
            "  Terminate I-variant",
            "  Terminate I-array",
            "after ScopeI",
            "ScopeJ: end (Dim p As Foo, a(0) As Foo)",
            "  Terminate J-object",
            "  Terminate J-array",
            "after ScopeJ",
            "ScopeK: end (Dim a(0) As Foo, p As Foo)",
            "  Terminate K-object",
            "  Terminate K-array",
            "after ScopeK",
            "ScopeM: end (Dim p As Foo, q As Foo)",
            "  Terminate M-first",
            "  Terminate M-second",
            "after ScopeM",
            "ScopeN: end (Dim a(0) As Foo, b(0) As Foo)",
            "  Terminate N-first",
            "  Terminate N-second",
            "after ScopeN");
    }
}
