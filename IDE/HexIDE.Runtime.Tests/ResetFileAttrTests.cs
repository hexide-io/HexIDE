using System.IO;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// #2521: vb6.exe SP6, native and p-code agreeing; see the oracle's reset-and-fileattr.md.
public class ResetFileAttrTests : BaseVBTestFixture, IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hexide-reset-").FullName;

    public void Dispose() => Directory.Delete(dir, true);

    private string OpenFour => $$"""
        Open "{{Path.Combine(dir, "a.txt")}}" For Output As #1
        Open "{{Path.Combine(dir, "b.txt")}}" For Append As #2
        Open "{{Path.Combine(dir, "c.txt")}}" For Random As #3
        Open "{{Path.Combine(dir, "d.txt")}}" For Binary As #4
        """;

    [Theory]
    [InlineData("1, 1", 2L)]
    [InlineData("2, 1", 8L)]
    [InlineData("3, 1", 4L)]
    [InlineData("4, 1", 32L)]
    [InlineData("1", 2L)]
    [InlineData("1.6, 1", 8L)]
    [InlineData("\"1\", 1", 2L)]
    public async Task FileAttr_returns_a_Long_mode(string arguments, long expected)
    {
        await Run(OpenFour + $"\nDebug.Print FileAttr({arguments})\nDebug.Print TypeName(FileAttr({arguments}))\nClose");
        AssertDebugLog([new Vb6Value(expected), new Vb6Value("Long")]);
    }

    [Theory]
    [InlineData("5, 1", 52L)] // The probe's Input open failed; #5 was never open.
    [InlineData("1, 2", 5L)]
    [InlineData("1, 3", 5L)]
    [InlineData("1, 0", 5L)]
    [InlineData("9, 1", 52L)]
    [InlineData("0, 1", 52L)]
    [InlineData("Null, 1", 94L)]
    public async Task FileAttr_reports_the_measured_error(string arguments, long expected)
    {
        await Run(OpenFour + $"\nOn Error Resume Next\nDebug.Print FileAttr({arguments})\nDebug.Print Err.Number\nClose");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task Input_mode_refuses_an_unmeasured_value()
    {
        var path = Path.Combine(dir, "input.txt");
        File.WriteAllText(path, "");
        Func<Task> run = () => Run($"Open \"{path}\" For Input As #1\nDebug.Print FileAttr(1, 1)");
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*Input*unmeasured*");
    }

    [Theory]
    [InlineData("FileNumber:=1")]
    [InlineData("1, \"1\"")]
    public async Task Unmeasured_arguments_refuse(string arguments)
    {
        Func<Task> run = () => Run($"Debug.Print FileAttr({arguments})");
        await run.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public async Task Reset_with_no_files_does_nothing()
    {
        await Run("Reset\nDebug.Print FreeFile");
        AssertDebugLog([new Vb6Value(1)]);
    }

    [Theory]
    [InlineData("Debug.Print FreeFile", 1L)]
    [InlineData("v = FileAttr(1, 1): Debug.Print Err.Number", 52L)]
    [InlineData("v = EOF(1): Debug.Print Err.Number", 52L)]
    [InlineData("Debug.Print FileLen(\"{a}\")", 7L)]
    [InlineData("Debug.Print FileLen(\"{b}\")", 6L)]
    [InlineData("Close #1: Debug.Print Err.Number", 0L)]
    public async Task Reset_closes_every_file_and_flushes_writes(string observation, long expected)
    {
        File.WriteAllText(Path.Combine(dir, "e.txt"), "");
        observation = observation.Replace("{a}", Path.Combine(dir, "a.txt")).Replace("{b}", Path.Combine(dir, "b.txt"));
        await Run(OpenFour + $$"""

            Open "{{Path.Combine(dir, "e.txt")}}" For Input As #5
            Print #1, "12345"
            Print #2, "1234"
            Reset
            On Error Resume Next
            {{observation}}
            """);
        Convert.ToInt64(debug.Should().ContainSingle().Which.Value).Should().Be(expected);
    }
}
