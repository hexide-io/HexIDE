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

    // A Currency beside a Double in + and -: the Double becomes a Currency first, by its binary value, then the sum
    // is a Currency (measured on SP6 with /make, native and p-code, #966's arithmetic comment). `d` is a typed Double.
    [Theory]
    [InlineData("1.00005", "1.0001", "-0.0001")]
    [InlineData("2.00005", "2", "-1")]
    [InlineData("0.00015", "0.0001", "0.9999")]
    [InlineData("0.00025", "0.0003", "0.9997")]
    [InlineData("1.00015", "1.0002", "-0.0002")]
    [InlineData("3.00005", "3", "-2")]
    public async Task Plus_and_minus_convert_the_Double_first(string d, string plus, string minus)
    {
        await Run($"Dim d As Double\nd = {d}\nDebug.Print CCur(0) + d\nDebug.Print d + CCur(0)\nDebug.Print CCur(1) - d\n"
            + "Dim v As Variant\nv = d\nDebug.Print CCur(0) + v\n");
        var p = Vb6Value.NewCurrency(decimal.Parse(plus, System.Globalization.CultureInfo.InvariantCulture));
        var m = Vb6Value.NewCurrency(decimal.Parse(minus, System.Globalization.CultureInfo.InvariantCulture));
        AssertDebugLog([p, p, m, p]);
    }

    [Theory]
    [InlineData("0@ + 0.00015", "0.0001")]
    [InlineData("1@ + 0.00005", "1.0001")]
    [InlineData("2@ + 0.00005", "2.0001")]
    public async Task Plus_with_literals(string expression, string expected)
    {
        await Run($"Debug.Print {expression}\n");
        AssertDebugLog([Vb6Value.NewCurrency(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture))]);
    }

    // A Currency times a Double is a Double and is not rounded; only a store into a Currency rounds it, by the
    // Double's binary value (measured on SP6 with /make, native and p-code, #966's arithmetic comment; #230's table).
    [Theory]
    [InlineData("1.00005", "1.0001")]
    [InlineData("2.00005", "2")]
    [InlineData("0.00015", "0.0001")]
    [InlineData("0.00025", "0.0003")]
    [InlineData("1.00015", "1.0002")]
    [InlineData("3.00005", "3")]
    public async Task Times_a_Double_is_an_unrounded_Double(string d, string stored)
    {
        await Run($"Dim d As Double, c As Currency\nd = {d}\nDebug.Print TypeName(CCur(1) * d)\nDebug.Print CCur(1) * d\n"
            + "Debug.Print TypeName(d * CCur(1))\nc = CCur(1) * d\nDebug.Print c\n");
        debug[0].Value.Should().Be("Double");
        debug[1].Value.Should().Be(double.Parse(d, System.Globalization.CultureInfo.InvariantCulture));
        debug[2].Value.Should().Be("Double");
        debug[3].Should().Be(Vb6Value.NewCurrency(decimal.Parse(stored, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Times_a_Single_is_a_Double_of_the_Singles_value()
    {
        await Run("Dim c As Currency\nDebug.Print TypeName(1@ * 1.00005!)\nDebug.Print 1@ * 1.00005!\nc = CCur(1) * CSng(1.00005)\nDebug.Print c\n");
        debug[0].Value.Should().Be("Double");
        debug[1].Value.Should().Be((double)1.00005f);
        debug[2].Should().Be(Vb6Value.NewCurrency(1m));
    }

    [Fact]
    public async Task Times_a_Decimal_is_a_Decimal()
    {
        await Run("Dim c As Currency\nDebug.Print TypeName(CCur(1) * CDec(1.00005))\nDebug.Print CCur(1) * CDec(1.00005)\n"
            + "c = CCur(1) * CDec(1.00005)\nDebug.Print c\n");
        debug[0].Value.Should().Be("Decimal");
        debug[1].Value.Should().Be(1.00005m);
        debug[2].Should().Be(Vb6Value.NewCurrency(1m));
    }
}
