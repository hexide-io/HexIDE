using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class PartitionTests : BaseVBTestFixture
{
    // #2526: vb6.exe SP6, native and p-code, including the exact padding and result type.
    [Theory]
    [InlineData("25, 0, 100, 10", " 20: 29")]
    [InlineData("0, 0, 100, 10", "  0:  9")]
    [InlineData("100, 0, 100, 10", "100:100")]
    [InlineData("101, 0, 100, 10", "101:   ")]
    [InlineData("-1, 0, 100, 10", "   : -1")]
    [InlineData("5, 0, 9, 1", " 5: 5")]
    [InlineData("5, 0, 99, 5", "  5:  9")]
    [InlineData("1000, 0, 999, 100", "1000:    ")]
    [InlineData("50, 10, 90, 25", "35:59")]
    [InlineData("24.6, 0, 100, 10", " 20: 29")]
    [InlineData("25, 0, 100, 10.6", " 22: 32")]
    [InlineData("Null, 0, 100, 10", null)]
    [InlineData("25, Null, 100, 10", null)]
    [InlineData("\"25\", 0, 100, 10", " 20: 29")]
    [InlineData("25, 0, 100, 200", "  0:100")]
    [InlineData("99999, 0, 100000, 10", " 99990: 99999")]
    public async Task Measured_ranges_and_nulls(string arguments, string? expected)
    {
        await Run($"""
            On Error Resume Next
            Dim result
            result = Partition({arguments})
            Debug.Print result
            Debug.Print Err.Number
            """);

        AssertDebugLog([expected is null ? Vb6Value.Null : new Vb6Value(expected), new Vb6Value(0L)]);
    }

    [Theory]
    [InlineData("-5, -10, 10, 5", 5)]
    [InlineData("25, 100, 0, 10", 5)]
    [InlineData("25, 0, 100, 0", 5)]
    [InlineData("25, 0, 0, 10", 5)]
    [InlineData("25, -1, 100, 10", 5)]
    [InlineData("\"x\", 0, 100, 10", 13)]
    public async Task Measured_errors(string arguments, int number)
    {
        await Run($"""
            On Error Resume Next
            Dim result
            result = Partition({arguments})
            Debug.Print Err.Number
            """);

        AssertDebugLog([new Vb6Value((long)number)]);
    }

    [Theory]
    [InlineData("25, 0, Null, 10")]
    [InlineData("25, 0, 100, Null")]
    public async Task Null_as_Stop_or_Interval_is_refused_until_measured(string arguments)
    {
        var run = () => Run($"""
            Dim result
            result = Partition({arguments})
            """);

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*Null as Stop or Interval*#2526*");
    }
}
