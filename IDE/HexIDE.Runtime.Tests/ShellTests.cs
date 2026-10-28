using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class ShellTests : BaseVBTestFixture
{
    private static string Command => OperatingSystem.IsWindows() ? "cmd /c exit" : "/bin/sh -c true";
    private static string Literal(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";

    [Theory]
    [InlineData("vbHide")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("3")]
    [InlineData("4")]
    [InlineData("6")]
    [InlineData("7")]
    [InlineData("2.6")]
    public async Task StartsProcess(string style)
    {
        await Run($"Debug.Print Shell({Literal(Command)}, {style}) > 0");
        AssertDebugLog([true]);
    }

    [Fact]
    public async Task ResultTypeName()
    {
        await Run($"Debug.Print TypeName(Shell({Literal(Command)}, 0))");
        AssertDebugLog(["Double"]);
    }

    [Fact]
    public async Task ResultVarType()
    {
        await Run($"Debug.Print VarType(Shell({Literal(Command)}, 0))");
        AssertDebugLog([new Vb6Value(5L)]);
    }

    [Theory]
    [InlineData("\"nosuchprogram_x.exe\"", "", "53")]
    [InlineData("\"\"", "", "5")]
    [InlineData("Null", "", "94")]
    [InlineData("", ", 10", "5")]
    [InlineData("", ", -1", "5")]
    [InlineData("", ", Null", "94")]
    public async Task Errors(string path, string style, string error)
    {
        await Run($"On Error Resume Next\nShell {(path.Length == 0 ? Literal(Command) : path)}{style}\nDebug.Print Err.Number");
        AssertDebugLog([new Vb6Value(long.Parse(error))]);
    }

    [Fact]
    public async Task FolderIsInvalid()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide shell folder " + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            await Run($"On Error Resume Next\nShell {Literal(directory)}\nDebug.Print Err.Number");
            AssertDebugLog([new Vb6Value(5L)]);
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task QuotedProgram()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide shell " + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var executable = Path.Combine(directory, OperatingSystem.IsWindows() ? "helper.exe" : "helper");
            File.Copy(OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe") : "/bin/sh", executable);
            var command = "\"" + executable + "\"" + (OperatingSystem.IsWindows() ? " /c exit" : " -c true");
            await Run($"Debug.Print Shell({Literal(command)}, vbHide)");
            debug.Should().ContainSingle();
            debug[0].Type.Should().Be(Vb6Value.ValueType.Double);
            var id = Convert.ToInt32(debug[0].Value);
            id.Should().BeGreaterThan(0);
            try
            {
                using var process = Process.GetProcessById(id);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
            catch (ArgumentException)
            {
                // A short-lived helper can have exited before the process lookup.
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task StatementWritesFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "hexide-shell-" + Guid.NewGuid() + ".txt");
        try
        {
            var command = OperatingSystem.IsWindows()
                ? $"cmd /c echo x>\"{path}\""
                : $"/bin/sh -c \"echo x > '{path}'\"";
            await Run($"Shell {Literal(command)}, vbHide");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while ((!File.Exists(path) || new FileInfo(path).Length == 0) && DateTime.UtcNow < deadline)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            File.ReadAllText(path).Trim().Should().Be("x");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
