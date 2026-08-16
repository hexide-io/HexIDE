using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Numeric-literal typing fidelity. VB6 types an unsuffixed floating-point literal (with a decimal point
/// or exponent) as <c>Double</c>; the type-char suffixes <c>#</c>/<c>!</c> force Double/Single.
/// </summary>
public class LiteralTests : BaseVBTestFixture
{
    [Fact]
    public async Task BareDecimalLiteral_IsDouble_notSingle()
    {
        await Run("Debug.Print 3.14\n");

        // 3.14 here is a C# double; the comparer treats Single and Double as distinct types, so this
        // assertion only passes when the literal is evaluated as Double (it threw/failed as Single before).
        AssertDebugLog([new Vb6Value(3.14)]);
    }

    [Fact]
    public async Task FloatingLiteralSuffixes_AreHonoured()
    {
        await Run("Debug.Print 2.5#\nDebug.Print 2.5!\n");

        AssertDebugLog([new Vb6Value(2.5), new Vb6Value(2.5f)]); // # -> Double, ! -> Single
    }

    [Fact]
    public async Task WholeNumberLiteral_WithinInt16_IsInteger()
    {
        await Run("Debug.Print 32767\n");

        AssertDebugLog([new Vb6Value(32767)]);   // fits Int16 -> Integer
    }

    [Fact]
    public async Task WholeNumberLiteral_BeyondInt16_IsLong()
    {
        await Run("Debug.Print 40000\n");

        // 40000 exceeds Int16, so the magnitude rule types it Long (VB6-correct) — not an overflowing Integer.
        AssertDebugLog([new Vb6Value(40000)]);
    }

    [Fact]
    public async Task WholeNumberLiteral_BeyondInt32_IsDouble()
    {
        await Run("Debug.Print 3000000000\n");
        AssertDebugLog([new Vb6Value(3000000000.0)]);   // exceeds Long -> Double
    }

    [Fact]
    public async Task ExponentWithoutDot_IsDouble()
    {
        await Run("Debug.Print 1e5\n");
        AssertDebugLog([new Vb6Value(100000.0)]);
    }

    // Type-char suffixes on a whole-number literal: & Long, ! Single, # Double, @ Currency.
    [Fact]
    public async Task LongSuffix()
    {
        await Run("Debug.Print 10&\n");
        AssertDebugLog([new Vb6Value(10L)]);
    }

    [Fact]
    public async Task SingleAndDoubleSuffix()
    {
        await Run("Debug.Print 10!\nDebug.Print 10#\n");
        AssertDebugLog([new Vb6Value(10f), new Vb6Value(10.0)]);
    }

    [Fact]
    public async Task CurrencySuffix()
    {
        await Run("Debug.Print 10@\n");
        AssertDebugLog([Vb6Value.NewCurrency(10m)]);
    }

    // Octal literals: typed by magnitude; a trailing & forces Long.
    [Fact]
    public async Task OctalLiteral_IsInteger()
    {
        await Run("Debug.Print &O17\n");
        AssertDebugLog([new Vb6Value(15)]);   // 17 octal = 15, fits Int16 -> Integer
    }

    [Fact]
    public async Task OctalLiteral_LongSuffix()
    {
        await Run("Debug.Print &O17&\n");
        AssertDebugLog([new Vb6Value(15L)]);
    }

    // VB6 &H hex / &O octal are unsigned bit-patterns with 16/32-bit two's-complement wrap (verified vs vb6.exe).
    [Theory]
    [InlineData("&HFF", 255)]              // Integer
    [InlineData("&H7FFF", 32767)]          // Integer
    [InlineData("&H8000", -32768)]         // 16-bit wrap -> Integer
    [InlineData("&HFFFF", -1)]             // 16-bit wrap -> Integer
    [InlineData("&H10000", 65536L)]        // Long
    [InlineData("&HFFFFFFFF", -1L)]        // 32-bit wrap -> Long
    [InlineData("&HFFFF&", 65535L)]        // & forces Long (no 16-bit wrap)
    [InlineData("&H8000&", 32768L)]        // & forces Long
    [InlineData("&HFFFF%", -1)]            // % forces Integer (16-bit wrap)
    [InlineData("&O177777", -1)]           // octal 65535 -> 16-bit wrap -> Integer
    [InlineData("&O37777777777", -1L)]     // octal 0xFFFFFFFF -> 32-bit wrap -> Long
    public async Task HexOctalLiteral_IsNumericBitPattern(string expr, object expected)
    {
        await Run($"Debug.Print {expr}\n");
        Vb6Value ev = expected is long l ? new Vb6Value(l) : new Vb6Value((int)expected);
        AssertDebugLog([ev]);
    }
}
