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
        AssertDebugLog([0, 2, 20, 8204, "Variant()"]);
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
        AssertDebugLog([0, 2, "a", "c", "String()"]);
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
        AssertDebugLog([2, "", 0, -1, "two", "b-c-d"]);
    }

    [Fact]
    public async Task Join_DefaultDelimiterIsSpace()
    {
        await Run(
            "Debug.Print Join(Array(\"a\", \"b\", \"c\"), \"-\")\n" +
            "Debug.Print Join(Array(\"a\", \"b\", \"c\"))\n");
        AssertDebugLog(["a-b-c", "a b c"]);
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
        AssertDebugLog([0, "banana", 1, "apple", "cherry", -1]);
    }

    [Fact]
    public async Task Filter_TextCompare_IsCaseInsensitive()
    {
        await Run(
            "Dim r\n" +
            "r = Filter(Array(\"apple\", \"BANANA\"), \"an\", True, vbTextCompare)\n" +
            "Debug.Print UBound(r)\n" +   // 0 (BANANA matches "an" case-insensitively)
            "Debug.Print r(0)\n");
        AssertDebugLog([0, "BANANA"]);
    }
}
