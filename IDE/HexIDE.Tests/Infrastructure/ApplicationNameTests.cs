using System.Diagnostics;
using System.Globalization;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;

namespace HexIDE.Tests.Infrastructure;

public class ApplicationNameTests
{
    [Theory]
    [InlineData("stock")]
    [InlineData("brand")]
    public async Task Application_uses_distribution_name(string identity)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { typeof(ApplicationNameTests).Assembly.Location, "-method", "*.Application_name_probe" }
        };
        start.Environment["HEXIDE_APPLICATION_NAME_PROBE"] = identity;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        process.ExitCode.Should().Be(0, await output + await error);
    }

    [AvaloniaFact]
    public void Application_name_probe()
    {
        var identity = Environment.GetEnvironmentVariable("HEXIDE_APPLICATION_NAME_PROBE");
        if (identity is null)
            return;
        if (identity == "brand")
            Distribution.Establish(new DistributionIdentity("acme-probe", "Acme Probe"));
        var culture = CultureInfo.DefaultThreadCurrentCulture;
        var uiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            var app = new App();
            app.Initialize();
            app.Name.Should().Be(identity == "brand" ? "Acme Probe" : "HexIDE");
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        }
    }
}
