using System.IO;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6, native and p-code: https://github.com/hexide-io/HexIDE/issues/2554.
public class WidthTests : BaseVBTestFixture, IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hexide-width-").FullName;
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }
    private string PathOf(string name) => Path.Combine(dir, name);

    [Theory]
    [InlineData("5", 0)]
    [InlineData("0", 0)]
    [InlineData("255", 0)]
    [InlineData("256", 5)]
    [InlineData("-1", 5)]
    [InlineData("3.6", 0)]
    [InlineData("\"4\"", 0)]
    public async Task Width_validates_the_measured_values(string width, int error)
    {
        await Run($$"""
            Open "{{PathOf("out")}}" For Output As #1
            On Error Resume Next
            Width #1, {{width}}
            Debug.Print Err.Number
            Close #1
            """);
        AssertDebugLog([new Vb6Value((long)error)]);
    }

    [Fact]
    public async Task An_unopened_number_is_52()
    {
        await Run("On Error Resume Next: Width #9, 5: Debug.Print Err.Number");
        AssertDebugLog([new Vb6Value(52L)]);
    }

    [Theory]
    [InlineData("Binary")]
    [InlineData("Input")]
    public async Task Width_is_accepted_on_the_measured_modes(string mode)
    {
        File.WriteAllText(PathOf("in"), "");
        await Run($$"""
            Open "{{PathOf("in")}}" For {{mode}} As #1
            On Error Resume Next
            Width #1, 5
            Debug.Print Err.Number
            Close #1
            """);
        AssertDebugLog([new Vb6Value(0L)]);
    }

    [Theory]
    [InlineData("5", "Print #1, \"ab\"; \"cd\"; \"ef\"", "abcd\r\nef\r\n")]
    [InlineData("5", "Print #1, \"abcdefghijkl\"", "abcdefghijkl\r\n")]
    [InlineData("5", "Print #1, \"abcdefg\"; \"h\"", "abcdefg\r\nh\r\n")]
    [InlineData("5", "Print #1, 1; 2; 3", " 1 \r\n 2 \r\n 3 \r\n")]
    [InlineData("5", "Print #1, \"abc\", \"de\"", "abc\r\nde\r\n")]
    [InlineData("5", "Print #1, \"a\"; Tab(4); \"b\"", "a  b\r\n")]
    [InlineData("5", "Print #1, \"abc\";: Print #1, \"de\"", "abcde\r\n")]
    [InlineData("5", "Print #1, \"abcde\"; \"f\"", "abcde\r\nf\r\n")]
    [InlineData("5", "Write #1, \"abcdefghij\", 123", "\"abcdefghij\",123\r\n")]
    [InlineData("0", "Print #1, \"abcdefghijkl\"", "abcdefghijkl\r\n")]
    [InlineData("3.6", "Print #1, \"ab\"; \"cd\"", "abcd\r\n")]
    [InlineData("3.4", "Print #1, \"ab\"; \"c\"", "abc\r\n")]
    [InlineData("4.5", "Print #1, \"ab\"; \"cde\"", "ab\r\ncde\r\n")]
    [InlineData("5.5", "Print #1, \"abc\"; \"def\"", "abcdef\r\n")]
    public async Task Print_wraps_as_measured_and_Write_does_not(string width, string statement, string expected)
    {
        await Run($$"""
            Open "{{PathOf("out")}}" For Output As #1
            Width #1, {{width}}
            {{statement}}
            Close #1
            """);
        File.ReadAllText(PathOf("out")).Should().Be(expected);
    }
}
