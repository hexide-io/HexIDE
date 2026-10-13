using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 3 — array-producing intrinsics (Array, Split, Join, Filter) and LBound/UBound over them. Every
/// expected value pinned against vb6.exe: Split's default delimiter is a space, Split("") and an empty Filter
/// are both empty arrays (0,-1), Array yields a Variant array while Split and Filter yield String arrays.
/// </summary>
public class ArrayFunctionsTests : BaseVBTestFixture
{
    [Fact]
    public async Task Array_BuildsZeroBasedVariantArray()
    {
        await Run(
            "Dim a\n" +
            "a = Array(10, 20, 30)\n" +
            "Debug.Print LBound(a)\n" +
            "Debug.Print UBound(a)\n" +
            "Debug.Print a(1)\n" +
            "Debug.Print VarType(a)\n" +   // vbArray(8192) + vbVariant(12)
            "Debug.Print TypeName(a)\n");
        // LBound/UBound/VarType are Long (#193); the element value keeps its own subtype.
        AssertDebugLog([new Vb6Value(0L), new Vb6Value(2L), new Vb6Value(20), new Vb6Value(8204L), "Variant()"]);
    }

    [Fact]
    public async Task Split_Basic_YieldsStringArray()
    {
        await Run(
            "Dim p\n" +
            "p = Split(\"a,b,c\", \",\")\n" +
            "Debug.Print LBound(p)\n" +
            "Debug.Print UBound(p)\n" +
            "Debug.Print p(0)\n" +
            "Debug.Print p(2)\n" +
            "Debug.Print TypeName(p)\n");
        AssertDebugLog([new Vb6Value(0L), new Vb6Value(2L), "a", "c", "String()"]);   // bounds are Long (#193)
    }

    [Fact]
    public async Task Split_EmptyMiddle_And_EmptyString_And_DefaultSpace_And_Limit()
    {
        await Run(
            "Dim p\n" +
            "p = Split(\"a,,b\", \",\")\n" +
            "Debug.Print UBound(p)\n" +   // 2 (empty middle preserved)
            "Debug.Print p(1)\n" +        // "" empty element
            "p = Split(\"\")\n" +
            "Debug.Print LBound(p)\n" +   // 0
            "Debug.Print UBound(p)\n" +   // -1 (empty array)
            "p = Split(\"one two three\")\n" +
            "Debug.Print p(1)\n" +        // "two" (default delimiter is a space)
            "p = Split(\"a-b-c-d\", \"-\", 2)\n" +
            "Debug.Print p(1)\n");        // "b-c-d" (limit puts the remainder in the last element)
        AssertDebugLog([new Vb6Value(2L), "", new Vb6Value(0L), new Vb6Value(-1L), "two", "b-c-d"]);   // the three bounds are Long (#193)
    }

    [Fact]
    public async Task Join_DefaultDelimiterIsSpace()
    {
        await Run(
            "Debug.Print Join(Array(\"a\", \"b\", \"c\"), \"-\")\n" +
            "Debug.Print Join(Array(\"a\", \"b\", \"c\"))\n");
        AssertDebugLog(["a-b-c", "a b c"]);
    }

    [Theory]
    [InlineData("v = \"abc\"")]
    [InlineData("v = 5")]
    [InlineData("v = Empty")]
    [InlineData("v = Null")]
    [InlineData("Set v = Nothing")]   // 13, not the 91 the conversions give (measured)
    public async Task Join_Filter_and_UBound_of_anything_but_an_array_raise_13(string setup)
    {
        // Measured, native and in the IDE (#840). Join and Filter raised 5.
        await Run($"Dim v, c\n{setup}\nOn Error Resume Next\n" +
                  "c = Join(v, \"-\")\nDebug.Print Err.Number\nErr.Clear\n" +
                  "c = Filter(v, \"b\")\nDebug.Print Err.Number\nErr.Clear\n" +
                  "c = UBound(v)\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(13L), new Vb6Value(13L), new Vb6Value(13L)]);
    }

    [Theory]
    [InlineData("Dim d() As String")]
    [InlineData("Dim d()")]
    [InlineData("Dim d() As Long")]
    [InlineData("Dim d() As String\nReDim d(1)\nErase d")]
    public async Task Join_and_Filter_of_an_array_never_allocated_do_not_raise(string declaration)
    {
        // Measured, native and in the IDE: Join gives "", and Filter an unallocated String array (VarType 8200, whose
        // bounds raise 9), whatever the array's own type. Both indexed a bounds list that was not there, an exception
        // no On Error could trap.
        await Run($"{declaration}\nDim c\nOn Error Resume Next\n" +
                  "c = Join(d, \"-\")\nDebug.Print \"[\" & c & \"]\"\nDebug.Print Err.Number\n" +
                  "c = Filter(d, \"a\")\nDebug.Print Err.Number\nDebug.Print VarType(c)\n" +
                  "Dim n\nn = UBound(c)\nDebug.Print Err.Number\n");
        AssertDebugLog(["[]", new Vb6Value(0L), new Vb6Value(0L), new Vb6Value(8200L), new Vb6Value(9L)]);
    }

    [Fact]
    public async Task Filter_IncludeExclude_And_NoMatch()
    {
        await Run(
            "Dim r\n" +
            "r = Filter(Array(\"apple\", \"banana\", \"cherry\"), \"an\")\n" +
            "Debug.Print UBound(r)\n" +   // 0
            "Debug.Print r(0)\n" +        // "banana"
            "r = Filter(Array(\"apple\", \"banana\", \"cherry\"), \"an\", False)\n" +
            "Debug.Print UBound(r)\n" +   // 1
            "Debug.Print r(0)\n" +        // "apple"
            "Debug.Print r(1)\n" +        // "cherry"
            "r = Filter(Array(\"apple\", \"pear\"), \"xyz\")\n" +
            "Debug.Print UBound(r)\n");   // -1 (no matches)
        AssertDebugLog([new Vb6Value(0L), "banana", new Vb6Value(1L), "apple", "cherry", new Vb6Value(-1L)]);   // UBound is Long (#193)
    }

    [Theory]
    [InlineData("Filter(Array(\"apple\", \"pear\"), \"a\")", 1L)]   // a Variant array in
    [InlineData("Filter(Array(1, 12), \"1\")", 1L)]                 // numbers in, strings out
    [InlineData("Filter(Array(\"apple\", \"pear\"), \"xyz\")", -1L)] // no match
    public async Task Filter_always_gives_a_String_array(string call, long upper)
    {
        // Measured, native and in the IDE: VarType 8200 every time. It was a Variant array.
        await Run($"Dim r\nr = {call}\nDebug.Print VarType(r)\nDebug.Print TypeName(r)\nDebug.Print UBound(r)\n");
        AssertDebugLog([new Vb6Value(8200L), "String()", new Vb6Value(upper)]);
    }

    [Theory]
    [InlineData("Dim d() As String", "Filter(d, \"a\", \"x\")", 13L)]
    [InlineData("Dim d() As String", "Filter(d, Null)", 94L)]
    [InlineData("Dim d() As String", "Filter(d, \"a\", Null)", 94L)]
    [InlineData("Dim d(1) As String", "Filter(d, Null)", 94L)]
    [InlineData("Dim d(1) As String", "Filter(d, \"a\", Null)", 94L)]
    [InlineData("Dim d(1) As String", "Filter(d, \"a\", \"x\")", 13L)]
    [InlineData("Dim d() As String", "Filter(d, \"a\", True, Null)", 94L)]
    [InlineData("Dim d(1) As String", "Filter(d, \"A\", True, Null)", 94L)]
    [InlineData("Dim d() As String", "Filter(d, \"a\", True, \"x\")", 13L)]
    [InlineData("Dim d(1) As String", "Filter(d, \"A\", True, \"x\")", 13L)]
    [InlineData("Dim d", "Filter(\"abc\", Null)", 94L)]   // ahead of the array check, which gives 13
    [InlineData("Dim d", "Filter(\"abc\", \"a\", Null)", 94L)]
    [InlineData("Dim d", "Filter(\"abc\", \"a\", True, Null)", 94L)]
    [InlineData("Dim d", "Filter(Null, Null)", 94L)]
    [InlineData("Dim d", "Filter(Null, \"a\")", 13L)]
    [InlineData("Dim d", "Filter(5, \"a\", \"x\")", 13L)]
    public async Task Filter_reads_its_match_switch_and_compare_before_the_array(string declaration, string call, long error)
    {
        // Measured, native and in the IDE: these raise even for an array never allocated, which Filter otherwise answers
        // with no error. A Null compare was 13, and a Null match beside something that is not an array was 13.
        await Run($"{declaration}\nDim c\nOn Error Resume Next\nc = {call}\nDebug.Print Err.Number\nDebug.Print IsEmpty(c)\n");
        AssertDebugLog([new Vb6Value(error), new Vb6Value(true)]);
    }

    [Theory]
    [InlineData("Filter(d, \"a\", e)", -1L)]          // the switch reads as CBool: Empty excludes
    [InlineData("Filter(d, \"a\", \"True\")", 1L)]
    [InlineData("Filter(d, \"a\", \"False\")", -1L)]
    [InlineData("Filter(d, \"a\", 2)", 1L)]
    [InlineData("Filter(d, \"A\", True, e)", -1L)]    // the compare reads as a number: Empty is binary
    [InlineData("Filter(d, \"A\", True, \"1\")", 1L)]
    [InlineData("Filter(d, \"A\", True, 1)", 1L)]
    public async Task Filter_reads_its_switch_as_CBool_does_and_its_compare_as_a_number(string call, long upper)
    {
        // Measured, native and in the IDE, with d holding "apple" and "pear". Both were read as plain numbers, so an Empty
        // switch or compare raised 13.
        await Run($"Dim d(1) As String, e, r\nd(0) = \"apple\"\nd(1) = \"pear\"\nr = {call}\nDebug.Print UBound(r)\n");
        AssertDebugLog([new Vb6Value(upper)]);
    }

    [Theory]
    [InlineData("0", 0L, 0L)]          // binary: only "pear" holds a lower-case a
    [InlineData("0.5", 0L, 0L)]        // rounds to 0
    [InlineData("Empty", 0L, 0L)]
    [InlineData("1", 0L, 1L)]          // text: "Apple" too
    [InlineData("1.4", 0L, 1L)]        // rounds to 1
    [InlineData("\"1\"", 0L, 1L)]
    [InlineData("2", 5L, -2L)]         // vbDatabaseCompare
    [InlineData("\"2\"", 5L, -2L)]
    [InlineData("1.5", 5L, -2L)]       // rounds to 2
    [InlineData("-1", 5L, -2L)]
    [InlineData("True", 5L, -2L)]
    public async Task Filters_compare_is_0_binary_1_text_and_otherwise_5(string compare, long error, long upper)
    {
        // Measured, native and in the IDE, with d holding "Apple" and "pear". 2 and negatives were binary, and so was
        // every value but 1. -2 here stands for no result.
        await Run($"Dim d(1) As String, r\nd(0) = \"Apple\"\nd(1) = \"pear\"\nr = CLng(-2)\nOn Error Resume Next\n" +
                  $"r = Filter(d, \"a\", True, {compare})\nDebug.Print Err.Number\nIf IsArray(r) Then Debug.Print UBound(r) Else Debug.Print r\n");
        AssertDebugLog([new Vb6Value(error), new Vb6Value(upper)]);
    }

    [Theory]
    [InlineData("3")]
    [InlineData("1033")]
    [InlineData("3000")]
    public async Task Filters_compare_of_3_or_more_is_refused_because_VB6_reads_it_as_a_locale(string compare)
    {
        // Measured: a valid Windows locale identifier (3, 1033) compares as text in that locale, and an invalid one
        // (3000) is 5. HexIDE cannot tell which is which (#881).
        var run = () => Run($"Dim d(1) As String, r\nd(0) = \"Apple\"\nr = Filter(d, \"a\", True, {compare})\n");

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*locale*#881*");
    }

    [Theory]
    [InlineData("Dim d() As String", "Filter(d, \"a\", e)")]
    [InlineData("Dim d() As String", "Filter(d, \"a\", \"True\")")]
    [InlineData("Dim d() As String", "Filter(d, \"a\", True, e)")]
    [InlineData("Dim d() As String", "Filter(d, \"a\", True, 2)")]       // the compare is not checked
    [InlineData("Dim d() As String", "Filter(d, \"a\", True, -1)")]
    [InlineData("Dim d() As String", "Filter(d, \"a\", True, 3000)")]
    [InlineData("Dim d", "Filter(Array(), \"a\")")]                       // 0 To -1 in, unallocated out
    [InlineData("Dim d", "Filter(Array(), \"a\", True, 2)")]
    public async Task Filter_of_an_array_with_no_element_gives_an_unallocated_String_array(string declaration, string call)
    {
        // Measured, native and in the IDE: no error, and an unallocated String array back, whose bounds raise 9. For
        // Array() it gave 0 To -1.
        await Run($"{declaration}\nDim e, c, n\nOn Error Resume Next\nc = {call}\nDebug.Print Err.Number\nDebug.Print VarType(c)\n" +
                  "n = UBound(c)\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(0L), new Vb6Value(8200L), new Vb6Value(9L)]);
    }

    [Theory]
    [InlineData("Filter(d, \"a\", \"x\", 3000)", 13L)]     // the switch's 13: the compare is checked only later
    [InlineData("Filter(d, Null, \"x\")", 13L)]            // the switch before the match
    [InlineData("Filter(d, \"a\", \"x\", Null)", 94L)]     // the compare before the switch
    [InlineData("Filter(d, Null, True, 3000)", 94L)]
    [InlineData("Filter(\"abc\", \"a\", True, 3000)", 13L)]   // the array check before the compare's
    public async Task Filter_converts_its_arguments_right_to_left_and_checks_the_compare_last(string call, long error)
    {
        // Measured, native and in the IDE.
        await Run($"Dim d(1) As String, c\nd(0) = \"Apple\"\nOn Error Resume Next\nc = {call}\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(error)]);
    }

    [Theory]
    [InlineData("c = Join(Array(\"ab\", Null), \"-\")", 94L)]
    [InlineData("c = Filter(Array(\"ab\", Null), \"zz\", False)", 94L)]
    [InlineData("c = Filter(Array(\"ab\", Null), \"ul\")", 94L)]
    [InlineData("c = Join(Array(Array(1), \"b\"), \"-\")", 13L)]
    [InlineData("c = Filter(Array(Array(1, 2), \"b\"), \"b\", False)", 13L)]
    [InlineData("c = Filter(Array(\"ab\", Nothing), \"zz\", False)", 91L)]
    [InlineData("c = Join(Array(\"ab\", Nothing), \"-\")", 91L)]
    [InlineData("c = Join(Array(\"ab\", \"cd\"), o)", 91L)]   // a Nothing delimiter, with elements to join
    [InlineData("c = Filter(Array(\"ab\", \"cd\"), o)", 91L)] // a Nothing match
    public async Task An_element_that_cannot_be_a_string_raises_in_Join_and_Filter(string call, long error)
    {
        // Measured, native and in the IDE: each element is converted as a store into a String does. A Null element read
        // as "", an array element as the CLR type name, and Nothing as "".
        await Run($"Dim c, o\nSet o = Nothing\nOn Error Resume Next\n{call}\nDebug.Print Err.Number\nDebug.Print IsEmpty(c)\n");
        AssertDebugLog([new Vb6Value(error), new Vb6Value(true)]);
    }

    [Fact]
    public async Task Join_of_an_array_with_no_element_does_not_read_a_Nothing_delimiter()
    {
        // Measured, native and in the IDE: "" with no error.
        await Run("Dim d() As String, o, c\nSet o = Nothing\nOn Error Resume Next\nc = Join(d, o)\nDebug.Print Err.Number\nDebug.Print \"[\" & c & \"]\"\n");
        AssertDebugLog([new Vb6Value(0L), "[]"]);
    }

    [Theory]
    [InlineData("Dim d() As String", 0L)]                         // never allocated
    [InlineData("Dim d() As String\nReDim d(1)\nErase d", 0L)]   // erased
    [InlineData("Dim d\nd = Array()", 0L)]                        // allocated, 0 To -1
    [InlineData("Dim d\nd = Split(\"\")", 0L)]
    [InlineData("Dim d(0) As String", 94L)]                       // one element, so no delimiter is needed
    [InlineData("Dim d(1) As String", 94L)]
    public async Task Join_reads_a_Null_delimiter_only_when_the_array_has_an_element(string declaration, long error)
    {
        // Measured, native and in the IDE. A Null delimiter read as "".
        await Run($"{declaration}\nDim c\nOn Error Resume Next\nc = Join(d, Null)\nDebug.Print Err.Number\nDebug.Print TypeName(c)\n");
        AssertDebugLog([new Vb6Value(error), error == 0 ? "String" : "Empty"]);
    }

    [Fact]
    public async Task An_element_that_is_not_a_string_is_its_text_in_Join_and_Filter()
    {
        // Measured, native and in the IDE: True and Empty give "True" and "", and Filter matches "ru" inside True and
        // gives back the String "True". 42 is inferred from the measured 1.5.
        await Run("Dim r\nDebug.Print Join(Array(\"ab\", True, Empty, 42), \"-\")\n" +
                  "r = Filter(Array(\"ab\", True), \"ru\")\nDebug.Print UBound(r)\nDebug.Print TypeName(r(0)) & \" \" & r(0)\n");
        AssertDebugLog(["ab-True--42", new Vb6Value(0L), "String True"]);
    }

    [Fact]
    public async Task Filter_TextCompare_IsCaseInsensitive()
    {
        await Run(
            "Dim r\n" +
            "r = Filter(Array(\"apple\", \"BANANA\"), \"an\", True, vbTextCompare)\n" +
            "Debug.Print UBound(r)\n" +   // 0 (BANANA matches "an" case-insensitively)
            "Debug.Print r(0)\n");
        AssertDebugLog([new Vb6Value(0L), "BANANA"]);   // UBound is Long (#193)
    }
}
