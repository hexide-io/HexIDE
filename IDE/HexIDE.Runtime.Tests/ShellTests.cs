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

    // What Shell starts is recorded, so get_process_windows can read its windows and nothing else's (#2591).
    [Fact]
    public async Task RecordsTheProcessItStarted()
    {
        await Run($"Debug.Print Shell({Literal(Command)}, 6)");

        var pid = (int)(double)debug[0].Value!;
        ShellLaunches.All.Should().ContainSingle(l => l.ProcessId == pid)
            .Which.Should().Match<ShellLaunches.Launch>(l => l.Command == Command && l.WindowStyle == 6);
    }

    [Fact]
    public async Task RecordsNothingWhenItFails()
    {
        var before = ShellLaunches.All.Count;

        await Run("On Error Resume Next\nShell \"nosuchprogram_x.exe\", 1\nDebug.Print Err.Number");

        AssertDebugLog([new Vb6Value(53L)]);
        ShellLaunches.All.Count.Should().Be(before);
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
            DeleteHelperDirectory(directory);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void HelperDirectoryCleanupToleratesDeleteFailures(bool accessDenied, bool persistent)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide shell cleanup " + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "helper.exe"), "test");
        var attempts = 0;
        try
        {
            DeleteHelperDirectory(directory, path =>
            {
                attempts++;
                if (persistent || attempts < 3)
                {
                    if (accessDenied)
                        throw new UnauthorizedAccessException();
                    throw new IOException();
                }
                Directory.Delete(path, true);
            });
            attempts.Should().Be(persistent ? 5 : 3);
            Directory.Exists(directory).Should().Be(persistent);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    private static void DeleteHelperDirectory(string directory, Action<string>? delete = null)
    {
        delete ??= path => Directory.Delete(path, true);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                delete(directory);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A scanner may still hold helper.exe after exit (#2741). Leave it to the temp cleaner if busy.
                if (attempt < 4)
                    System.Threading.Thread.Sleep(25 * (attempt + 1));
            }
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
