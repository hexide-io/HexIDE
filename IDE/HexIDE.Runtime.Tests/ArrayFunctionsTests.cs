using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 3 — array-producing intrinsics (Array, Split, Join, Filter) and LBound/UBound over them. Every
/// expected value pinned against vb6.exe: Split's default delimiter is a space, Split("") and an empty Filter
/// are both empty arrays (0,-1), Array/Filter yield Variant arrays while Split yields a String array.
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
