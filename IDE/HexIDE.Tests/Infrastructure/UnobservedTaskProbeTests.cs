using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit.v3;

namespace HexIDE.Tests.Infrastructure;

public class UnobservedTaskProbeTests
{
    [Fact]
    public void Probe_records_a_deliberately_unobserved_fault()
    {
        var previous = Environment.GetEnvironmentVariable("HEXIDE_UNOBSERVED_PROBE");
        var path = Path.GetTempFileName();
        var probe = new UnobservedTaskProbe();
        var method = MethodBase.GetCurrentMethod() as MethodInfo;
        var test = Substitute.For<IXunitTest>();
        test.TestDisplayName.Returns("deliberate probe leak");
        try
        {
            Environment.SetEnvironmentVariable("HEXIDE_UNOBSERVED_PROBE", path);
            probe.Before(method!, test);
            Leak();
            probe.After(method!, test);
            var recorded = File.ReadAllText(path).ReplaceLineEndings("\n");
            recorded.Should().Contain("=== after deliberate probe leak\n");
            recorded.Should().Contain("InvalidOperationException: deliberate unobserved task for #4833");
        }
        finally
        {
            Environment.SetEnvironmentVariable("HEXIDE_UNOBSERVED_PROBE", previous);
            test.TestDisplayName.Returns(TestContext.Current.Test!.TestDisplayName);
            probe.Before(method!, test);
            File.Delete(path);
        }
    }

    public static bool ProbeRequested =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HEXIDE_UNOBSERVED_PROBE"));

    public static bool ForeignProbeRequested => ProbeRequested &&
        DebugAdapter.ForeignAdapterAcquisition.Resolve("netcoredbg").Options is not null;

    [Theory(Skip = "Set HEXIDE_UNOBSERVED_PROBE to run teardown diagnostics.", SkipUnless = nameof(ProbeRequested))]
    [InlineData("ShutdownWireShapeTests")]
    [InlineData("RenameWireShapeTests")]
    [InlineData("CancellationWireShapeTests")]
    [InlineData("TraceOnTheWireTests")]
    [InlineData("StdioProcessLspTransportTests")]
    [InlineData("WebSocketLspTransportTests")]
    public Task Teardown_leaves_no_unobserved_fault(string testClass) => CheckClassAsync(testClass);

    [Fact(Skip = "Set HEXIDE_UNOBSERVED_PROBE and provide netcoredbg to run adapter teardown diagnostics.",
        SkipUnless = nameof(ForeignProbeRequested))]
    public Task Foreign_adapter_teardown_leaves_no_unobserved_fault() => CheckClassAsync("ForeignAdapterTests");

    // A separate host keeps deliberately forced finalization from collecting another class's work.
    // Include the existing final collection as a sentinel: its hook collects the last fixture's faults too.
    private static async Task CheckClassAsync(string testClass)
    {
        var path = Path.GetTempFileName();
        var report = path + ".xml";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var token = deadline.Token;
        try
        {
            var info = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = RepoTree.Root(),
            };
            foreach (var argument in new[] { "test", "--root-directory", AppContext.BaseDirectory,
                         "--test-modules", Path.GetFileName(typeof(UnobservedTaskProbeTests).Assembly.Location), "--",
                         "--filter-class", "*" + testClass, "*LspSessionLeakCheck", "--minimum-expected-tests", "1",
                         "--report-xunit", "--report-xunit-filename", Path.GetFileName(report),
                         "--results-directory", Path.GetDirectoryName(report)!, "--hangdump", "--hangdump-timeout", "4m" })
                info.ArgumentList.Add(argument);
            info.Environment["HEXIDE_UNOBSERVED_PROBE"] = path;
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync(token);
            var stderr = process.StandardError.ReadToEndAsync(token);
            try { await process.WaitForExitAsync(token); }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            var output = await stdout + await stderr;
            process.ExitCode.Should().Be(0, output);
            var assembly = XDocument.Load(report).Root!.Element("assembly")!;
            assembly.Attribute("total")!.Value.Should().NotBe("0", "the class must actually run");
            assembly.Attribute("errors")!.Value.Should().Be("0", output);
            assembly.Descendants("test").Should().Contain(test =>
                test.Attribute("type")!.Value.EndsWith("." + testClass, StringComparison.Ordinal),
                "the requested class must run as well as the final collection's sentinel");
            File.ReadAllText(path).Should().BeEmpty("{0} must finish and observe the work it starts", testClass);
        }
        finally
        {
            File.Delete(path);
            File.Delete(report);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Leak() => _ = Task.FromException(new InvalidOperationException("deliberate unobserved task for #4833"));
}
