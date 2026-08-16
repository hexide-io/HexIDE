using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 3.7.4 — Format string masks (@ & < > !), Boolean named formats, and the no-format default dispatch.
/// Pinned against vb6.exe. String/Boolean output is ASCII, so the assertions are CI-stable.
/// </summary>
public class FormatStringBooleanTests : BaseVBTestFixture
{
    [Fact]
    public async Task CharacterPlaceholders_RightAlignAndOverflow()
    {
        await Run(
            "Debug.Print Format(\"abc\", \"@@@@@\")\n" +   // "  abc" (@ pads with spaces)
            "Debug.Print Format(\"abc\", \"&&&&&\")\n" +   // "abc"   (& pads with nothing)
            "Debug.Print Format(\"abcde\", \"@@@\")\n" +   // "abcde" (excess chars overflow, never truncated)
            "Debug.Print Format(\"ab\", \"@@@@\")\n" +     // "  ab"
            "Debug.Print Format(\"ab\", \"&&&&\")\n");     // "ab"
        AssertDebugLog(["  abc", "abc", "abcde", "  ab", "ab"]);
    }

    [Fact]
    public async Task LeftAlign_And_LiteralsInMask()
    {
        await Run(
            "Debug.Print Format(\"abc\", \"!@@@@@\")\n" +            // "abc  " (! left-aligns)
            "Debug.Print Format(\"1234567\", \"@@@-@@@@\")\n" +      // "123-4567"
            "Debug.Print Format(\"5551234\", \"(@@@) @@@-@@@@\")\n");// "(   ) 555-1234"
        AssertDebugLog(["abc  ", "123-4567", "(   ) 555-1234"]);
    }

    [Fact]
    public async Task CaseModifiers()
    {
        await Run(
            "Debug.Print Format(\"HeLLo\", \"<\")\n" +      // hello
            "Debug.Print Format(\"hello\", \">\")\n" +      // HELLO
            "Debug.Print Format(\"abc\", \">@@@@@\")\n");   // "  ABC"
        AssertDebugLog(["hello", "HELLO", "  ABC"]);
    }

    [Fact]
    public async Task BooleanNamedFormats()
    {
        await Run(
            "Debug.Print Format(True, \"Yes/No\")\n" +
            "Debug.Print Format(False, \"Yes/No\")\n" +
            "Debug.Print Format(5, \"Yes/No\")\n" +      // nonzero -> Yes
            "Debug.Print Format(0, \"Yes/No\")\n" +
            "Debug.Print Format(0, \"True/False\")\n" +
            "Debug.Print Format(-1, \"On/Off\")\n");
        AssertDebugLog(["Yes", "No", "Yes", "No", "False", "On"]);
    }

    [Fact]
    public async Task NoFormat_DefaultDispatch()
    {
        await Run(
            "Debug.Print Format(True)\n" +
            "Debug.Print Format(False)\n" +
            "Debug.Print Format(\"hi\")\n" +
            "Debug.Print Format(1234)\n" +
            "Debug.Print Format(1234.5678)\n");
        AssertDebugLog(["True", "False", "hi", "1234", "1234.5678"]);
    }

    [Fact]
    public async Task NonNumericString_UnderNumericMask_IsUnchanged()
    {
        await Run("Debug.Print Format(\"abc\", \"0.00\")\n");
        AssertDebugLog(["abc"]);
    }
}
