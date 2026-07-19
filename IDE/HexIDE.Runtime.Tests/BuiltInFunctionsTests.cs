using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class BuiltInFunctionsTests : BaseVBTestFixture
{
    [Theory]
    [InlineData(false, "Hello, World!", 1, null, "Hello, World!")]        // Full string from start
    [InlineData(false, "Hello, World!", 1, 5, "Hello")]                   // First 5 characters
    [InlineData(false, "Hello, World!", 8, 5, "World")]                   // Middle of the string
    [InlineData(false, "Hello, World!", 14, null, "")]                   // Start greater than string length
    [InlineData(false, "Hello, World!", 8, 20, "World!")]                  // Start within string, length exceeds
    [InlineData(false, "Hello, World!", 1, 0, "")]                        // Length is 0
    [InlineData(true, "Hello, World!", 0, 5, "")]                        // Start is 0 (invalid)
    [InlineData(false, "Hello, World!", 15, 5, "")]                       // Start exceeds length, returns ""
    [InlineData(false, null, 1, null, null)]                               // Null string input
    [InlineData(false, "", 1, null, "")]                                   // Empty string
    [InlineData(false, "Test", 2, 2, "es")]                                // Start within bounds, length within bounds
    public async Task MidFunction_ShouldReturnExpectedResult(bool assertThrows, string? inputString, int start, int? length, string? expectedResult)
    {
        var vbString = inputString == null ? "Null" : $@"""{inputString}""";
        string code = $@"
            Dim result
            result = Mid({vbString}, {start}{(length.HasValue ? $", {length.Value}" : "")})
            Debug.Print result
        ";

        if (assertThrows)
        {
            await ((Func<Task>)(async () => await Run(code))).Should().ThrowAsync<Exception>();
        }
        else
        {
            await Run(code);

            Vb6Value expectedValue = expectedResult is string str ? new Vb6Value(str) : expectedResult is null ? Vb6Value.Null : throw new Exception();

            AssertDebugLog([expectedValue]);
        }
    }

    [Theory]
    [InlineData("hello", "HELLO")]                // Lowercase string
    [InlineData("HELLO", "HELLO")]                // Uppercase string
    [InlineData("Hello World!", "HELLO WORLD!")]  // Mixed case
    [InlineData("1234!@#$", "1234!@#$")]          // Non-letter characters
    [InlineData("", "")]                          // Empty string
    [InlineData(null, null)]                      // Null string
    public async Task UCaseFunction_ShouldReturnExpectedResult(string? inputString, string? expectedResult)
    {
        string vbString = ConvertToVb6Value(inputString);
        string code = $@"
            Dim result
            result = UCase({vbString})
            Debug.Print result
        ";

        await Run(code);

        Vb6Value expectedValue = expectedResult is { } str ? new Vb6Value(str) : expectedResult is null ? Vb6Value.Null : throw new Exception();

        AssertDebugLog([expectedValue]);
    }

    [Fact]
    public async Task BuiltinConstants_HaveOriginalValue()
    {
        string code = $@"
            Debug.Print vbMsgBoxRtlReading
        ";

        await Run(code);

        AssertDebugLog([1048576]);
    }
}