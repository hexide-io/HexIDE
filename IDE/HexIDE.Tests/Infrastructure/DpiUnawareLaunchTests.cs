using HexIDE.Desktop;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// <c>--dpi-unaware</c>: the only route to scale 1 on a scaled Windows display, for a layout check at 100% (#3083).
/// </summary>
public class DpiUnawareLaunchTests
{
    [Theory]
    [InlineData("--dpi-unaware")]
    [InlineData("/dpi-unaware")]
    [InlineData("--DPI-UNAWARE")]
    public void TheFlagIsReadAndListed(string flag)
    {
        var property = typeof(ServerOptions).GetProperty(nameof(ServerOptions.DpiUnaware))!;
        property.SetValue(null, false);
        try
        {
            ServerOptions.ParseArgs([flag], Environment.CurrentDirectory);
            ServerOptions.DpiUnaware.Should().BeTrue();
        }
        finally
        {
            property.SetValue(null, false);
        }
        ServerOptions.HelpText(new ConsoleTarget(false, 120)).Should().Contain("--dpi-unaware");
    }

    [Fact]
    public void TheFlagMakesTheProcessDpiUnawareInDebugBuildsOnly()
    {
        // Like the launch-wiring guards beside it: Program is not run by this suite, so its source is what is checked.
        var source = File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "Program.cs"))
            .ReplaceLineEndings("\n");
        const string apply = "builder = builder.With(new Win32PlatformOptions { DpiAwareness = Win32DpiAwareness.Unaware });";
        var at = source.IndexOf(apply, StringComparison.Ordinal);
        at.Should().BeGreaterThan(0, "the flag has to reach Avalonia's Win32 options, or the IDE renders at the display's scale");
        var guard = source.LastIndexOf("if (ServerOptions.DpiUnaware)", at, StringComparison.Ordinal);
        guard.Should().BeGreaterThan(0);
        source.LastIndexOf("#if DEBUG", at, StringComparison.Ordinal).Should().BeGreaterThan(
            source.LastIndexOf("#endif", at, StringComparison.Ordinal), "a release build leaves the flag inert");
        source.IndexOf("StartWithClassicDesktopLifetime", StringComparison.Ordinal).Should().BeGreaterThan(at,
            "the options are read when the platform starts, so they have to be given before it does");
    }

    [Fact]
    public void AnotherPlatformRefusesTheFlagRatherThanSkipIt()
    {
        var source = File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "Program.cs"))
            .ReplaceLineEndings("\n");
        var at = source.IndexOf("if (ServerOptions.DpiUnaware && !OperatingSystem.IsWindows())", StringComparison.Ordinal);
        at.Should().BeGreaterThan(0);
        var end = source.IndexOf('}', at);
        source[at..end].Should().Contain("Environment.ExitCode = 2;").And.Contain("return;");
    }
}
