using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Only the five native/IDE measurements in #966; decimal payloads are compared exactly.
public class DoubleToCurrencyTests : BaseVBTestFixture
{
    [Fact]
    public async Task Store_AboveMidpoint()
    {
        await Run("Dim c As Currency\nc = 1.00005\nDebug.Print c\n");
        AssertDebugLog([Vb6Value.NewCurrency(1.0001m)]);
    }

    [Fact]
    public async Task CCur_AboveMidpoint()
    {
        await Run("Debug.Print CCur(1.00005)\n");
        AssertDebugLog([Vb6Value.NewCurrency(1.0001m)]);
    }

    [Fact]
    public async Task Const_AboveMidpoint()
    {
        await Run("Const C As Currency = 1.00005\nDebug.Print C\n");
        AssertDebugLog([Vb6Value.NewCurrency(1.0001m)]);
    }

    [Fact]
    public async Task CCur_BelowMidpoint_RightByLuck()
    {
        await Run("Debug.Print CCur(2.00005)\n");
        AssertDebugLog([Vb6Value.NewCurrency(2m)]);
    }

    [Fact]
    public async Task CCur_BelowMidpoint_DecimalTieWouldRoundUp()
    {
        await Run("Debug.Print CCur(0.00015)\n");
        AssertDebugLog([Vb6Value.NewCurrency(0.0001m)]);
    }
}
