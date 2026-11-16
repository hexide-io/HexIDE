using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6, native and p-code: https://github.com/hexide-io/HexIDE/issues/434
// Oracle PR #4041: currency-and-decimal-overflow-is-error-6.md.
public class CurrencyDecimalOverflowTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("5", "1E+30", 5)]
    [InlineData("7", "CDbl(-1E+30)", 7)]
    [InlineData("900000000000000@", "c * 100", 900000000000000L)]
    public async Task Currency_store_overflow_preserves_target(string initial, string expression, long expected)
    {
        await Run($"On Error Resume Next\nDim c As Currency\nc = {initial}\nc = {expression}\nDebug.Print Err.Number\nDebug.Print c\n");
        AssertDebugLog([new Vb6Value(6L), Vb6Value.NewCurrency(expected)]);
    }

    [Theory]
    [InlineData("CCur")]
    [InlineData("CDec")]
    public async Task Conversion_overflow_preserves_string(string conversion)
    {
        await Run($"On Error Resume Next\nDim x As Variant\nx = \"kept\"\nx = {conversion}(1E+30)\nDebug.Print Err.Number\nDebug.Print TypeName(x)\nDebug.Print x\n");
        AssertDebugLog([new Vb6Value(6L), new Vb6Value("String"), new Vb6Value("kept")]);
    }

    [Fact]
    public async Task Decimal_multiplication_overflow_preserves_target()
    {
        await Run("On Error Resume Next\nDim x As Variant\nx = CDec(1)\nx = x * 1E+30\nDebug.Print Err.Number\nDebug.Print TypeName(x)\nDebug.Print x\n");
        AssertDebugLog([new Vb6Value(6L), new Vb6Value("Decimal"), Vb6Value.NewDecimal(1m)]);
    }

    [Fact]
    public async Task Currency_times_Double_widens_without_overflow()
    {
        await Run("On Error Resume Next\nDim x As Variant\nx = CCur(1)\nx = x * 1E+30\nDebug.Print Err.Number\nDebug.Print TypeName(x)\nDebug.Print x\n");
        AssertDebugLog([new Vb6Value(0L), new Vb6Value("Double"), new Vb6Value(1E30)]);
    }

    [Fact]
    public async Task Currency_overflow_reaches_GoTo_handler()
    {
        await Run("On Error GoTo Handler\nDim c As Currency\nc = -1E+30\nDebug.Print \"unreachable\"\nExit Sub\nHandler:\nDebug.Print Err.Number\nDebug.Print Err.Description\n");
        AssertDebugLog([new Vb6Value(6L), new Vb6Value("Overflow")]);
    }
}
