using System.IO;
using System.Text;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Each row comes from vb6.exe SP6, native and p-code, in #2553:
// https://github.com/hexide-io/HexIDE/issues/2553. Setup locks are checked too.
public class FileLockTests : BaseVBTestFixture, IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hexide-locks-").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    [Theory]
    [InlineData("", "Lock #1", 0)]
    [InlineData("Lock #1", "Get #2, 5, b", 70, 0)]
    [InlineData("Lock #1", "Lock #1", 70)]
    [InlineData("Lock #1", "Unlock #1", 0)]
    [InlineData("Lock #1|Unlock #1", "Unlock #1", 70)]
    [InlineData("Lock #1|Unlock #1", "Get #2, 5, b", 0, 52)]
    [InlineData("", "Lock #1, 5", 0)]
    [InlineData("Lock #1, 5", "Get #2, 5, b", 70, 0)]
    [InlineData("Lock #1, 5", "Get #2, 4, b", 0, 51)]
    [InlineData("Lock #1, 5", "Get #2, 6, b", 0, 53)]
    [InlineData("Lock #1, 5", "Get #1, 5, b", 0, 52)]
    [InlineData("Lock #1, 5", "Put #2, 5, b", 70)]
    [InlineData("Lock #1, 5", "Unlock #1, 5", 0)]
    [InlineData("", "Lock #1, 2 To 4", 0)]
    [InlineData("Lock #1, 2 To 4", "Get #2, 2, b", 70, 0)]
    [InlineData("Lock #1, 2 To 4", "Get #2, 4, b", 70, 0)]
    [InlineData("Lock #1, 2 To 4", "Get #2, 5, b", 0, 52)]
    [InlineData("Lock #1, 2 To 4", "Unlock #1, 2 To 3", 70)]
    [InlineData("Lock #1, 2 To 4", "Unlock #1, 2 To 4", 0)]
    [InlineData("", "Lock #1, 0", 63)]
    [InlineData("", "Lock #1, -1", 63)]
    [InlineData("", "Lock #1, 5 To 2", 63)]
    [InlineData("", "Unlock #1, 5 To 2", 63)]
    [InlineData("", "Lock #1, 20", 0)]
    [InlineData("Lock #1, 20", "Get #2, 20, b", 70, 0)]
    [InlineData("Lock #1, 20", "Unlock #1, 20", 0)]
    [InlineData("", "Lock #9", 52)]
    [InlineData("", "Unlock #9", 52)]
    [InlineData("Lock #1, 3", "Close #1", 0)]
    [InlineData("Lock #1, 3|Close #1", "Get #2, 3, b", 0, 50)]
    [InlineData("", "Lock #1, To 4", 0)]
    [InlineData("Lock #1, To 4", "Get #2, 1, b", 70, 0)]
    [InlineData("Lock #1, To 4", "Get #2, 4, b", 70, 0)]
    [InlineData("Lock #1, To 4", "Get #2, 5, b", 0, 52)]
    [InlineData("Lock #1, To 4", "Unlock #1, To 4", 0)]
    public async Task Binary_rows(string setup, string statement, int error, int? value = null)
    {
        var path = Path.Combine(dir, "binary.dat");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("0123456789"));
        await Probe(path, "Binary Shared", setup, statement, error, value, "b", "Byte");
    }

    [Theory]
    [InlineData("", "Lock #1, 2", 0)]
    [InlineData("Lock #1, 2", "Get #2, 1, n", 0, 10)]
    [InlineData("Lock #1, 2", "Get #2, 2, n", 70, 0)]
    [InlineData("Lock #1, 2", "Get #2, 3, n", 0, 30)]
    [InlineData("Lock #1, 2", "Unlock #1, 2", 0)]
    [InlineData("", "Lock #1, 2 To 3", 0)]
    [InlineData("Lock #1, 2 To 3", "Get #2, 3, n", 70, 0)]
    [InlineData("Lock #1, 2 To 3", "Get #2, 4, n", 0, 40)]
    [InlineData("Lock #1, 2 To 3", "Unlock #1, 2 To 3", 0)]
    public async Task Random_rows(string setup, string statement, int error, int? value = null)
    {
        var path = Path.Combine(dir, "random.dat");
        File.WriteAllBytes(path, [10, 0, 0, 0, 20, 0, 0, 0, 30, 0, 0, 0, 40, 0, 0, 0]);
        await Probe(path, "Random Shared", setup, statement, error, value, "n", "Long", " Len = 4");
    }

    [Theory]
    [InlineData("", "Lock #1, 2", 0, null)]
    [InlineData("Lock #1, 2", "Line Input #2, s", 70, "")]
    [InlineData("Lock #1, 2", "Unlock #1, 2", 0, null)]
    [InlineData("Lock #1, 2|Unlock #1, 2", "Line Input #2, s", 0, "line one")]
    public async Task Sequential_rows(string setup, string statement, int error, string? value)
    {
        var path = Path.Combine(dir, "input.dat");
        File.WriteAllText(path, "line one\r\n", Encoding.ASCII);
        await Probe(path, "Input Shared", setup, statement, error, value, "s", "String");
    }

    [Fact]
    public async Task Two_opens_without_Shared_allow_lock_and_unlock()
    {
        var path = Path.Combine(dir, "plain.dat");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("0123456789"));
        await Probe(path, "Binary", "Lock #1", "Unlock #1", 0, null, "b", "Byte");
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("\"2\"")]
    [InlineData("Null")]
    public async Task Unmeasured_range_conversions_are_refused(string record)
    {
        Func<Task> act = () => Run("Lock #9, " + record);
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*Lock range conversion*");
    }

    private async Task Probe(string path, string mode, string setup, string statement, int error,
        object? value, string variable, string type, string length = "")
    {
        var body = new StringBuilder();
        var expected = new List<Vb6Value> { new(0L), new(0L) };
        foreach (var step in setup.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            body.AppendLine(step).AppendLine("Debug.Print Err.Number: Err.Clear");
            expected.Add(new Vb6Value(0L));
        }
        body.AppendLine(statement).AppendLine("Debug.Print Err.Number");
        expected.Add(new Vb6Value((long)error));
        if (value is not null)
        {
            body.AppendLine("Debug.Print " + variable);
            expected.Add(value is string text ? new Vb6Value(text)
                : type == "Byte" ? new Vb6Value(Convert.ToByte(value)) : new Vb6Value(Convert.ToInt64(value)));
        }
        await Run($$"""
            Sub Main()
                On Error Resume Next
                Dim {{variable}} As {{type}}
                Open "{{path}}" For {{mode}} As #1{{length}}
                Debug.Print Err.Number: Err.Clear
                Open "{{path}}" For {{mode}} As #2{{length}}
                Debug.Print Err.Number: Err.Clear
                {{body}}
                Close
            End Sub
            Main
            """);
        AssertDebugLog(expected);
    }
}
