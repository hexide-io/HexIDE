namespace HexIDE.Tests.LspClient;

public class ForeignServerReportTests
{
    [Fact]
    public void VersionProbeRunsOnLinuxToo()
    {
        ForeignServerReport.ReadVersion("dotnet").Should().MatchRegex(@"^\d+\.\d+\.\d+");
    }

    [Fact]
    public void FailedVersionProbeIsReportedWithoutInventingAVersion()
    {
        ForeignServerReport.ReadVersion(Path.Combine(AppContext.BaseDirectory, "absent-server"))
            .Should().StartWith("unavailable (");
    }

    [Fact]
    public void NodeServerVersionComesFromTheInstalledPackage()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "report-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        try
        {
            File.WriteAllText(Path.Combine(root, "package.json"), "{\"version\":\"4.10.0\"}");
            ForeignServerReport.ReadPackageVersion(Path.Combine(root, "bin", "server"))
                .Should().Be("4.10.0");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    [Fact]
    public void ReportFileNamesTheServerOriginExecutableAndVersion()
    {
        const string variable = "HEXIDE_REPORT_TEST_SERVER";
        var previous = Environment.GetEnvironmentVariable(variable);
        var executable = typeof(ForeignServerReportTests).Assembly.Location;
        var report = Path.Combine(AppContext.BaseDirectory, "server-report-" + Guid.NewGuid().ToString("N"));
        var server = new ForeignServer(ForeignServerAcquisition.Cpp, variable, "test-server", "", "cpp", []);
        try
        {
            Environment.SetEnvironmentVariable(variable, executable);
            ForeignServerReport.Write(server, report);
            var lines = File.ReadAllLines(report);
            lines.Should().ContainSingle();
            lines[0].Should().StartWith($"test-server: variable {variable}; executable={executable}; version=");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
            File.Delete(report);
        }
    }
}
