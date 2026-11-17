using HexIDE.Desktop;
using HexIDE.Startup;

namespace HexIDE.Tests.Infrastructure;

public class DisplayBackendTests
{
    [Theory]
    [InlineData("x11")]
    [InlineData("auto")]
    [InlineData("wayland")]
    public void Platform_option_accepts_each_backend(string value)
    {
        var option = ServerOptions.Options.SingleOrDefault(o => o.Name == "platform");
        option.Should().NotBeNull();
        var previous = ServerOptions.Platform;
        try
        {
            option!.Apply(value).Should().BeTrue();
            ServerOptions.Platform.Should().Be(value);
            var initializer = HexIDE.Desktop.Program.BuildAvaloniaApp().WindowingSubsystemInitializer;
            initializer.Should().NotBeNull();
            if (OperatingSystem.IsLinux())
                initializer!.Method.Name.Should().Contain(value switch
                {
                    "auto" => "UseWaylandWithFallback",
                    "wayland" => "UseWayland",
                    _ => "UseX11"
                });
        }
        finally
        {
            typeof(ServerOptions).GetProperty(nameof(ServerOptions.Platform))!.SetValue(null, previous);
        }
    }

    [Theory]
    [InlineData(null, null, "x11")]
    [InlineData(null, "auto", "auto")]
    [InlineData(null, "WAYLAND", "wayland")]
    [InlineData("x11", "wayland", "x11")]
    [InlineData("AUTO", "x11", "auto")]
    [InlineData("invalid", "wayland", "x11")]
    public void Selection_preserves_default_and_argument_precedence(string? argument, string? environment, string expected)
    {
        DisplayBackend.Select(argument, environment).Should().Be(expected);
    }

    public static bool IsLinux => OperatingSystem.IsLinux();

    [Theory(Skip = "Wayland startup is Linux-only", SkipUnless = nameof(IsLinux))]
    [InlineData("wayland", null)]
    [InlineData(null, "wayland")]
    public void Required_wayland_refuses_with_one_line_and_exit_two(string? argument, string? environment)
    {
        var previous = Console.Error;
        using var stderr = new StringWriter();
        var logged = new List<string>();
        try
        {
            Console.SetError(stderr);
            var exit = DisplayBackend.Run(DisplayBackend.Select(argument, environment),
                () => throw new Avalonia.Wayland.AvaloniaWaylandException("Unable to connect to Wayland display"),
                reason => string.Format(new HexIDE.Localization.LocalizationService()
                    .GetStringFrom("en", argument != null ? "Str.Startup.WaylandUnavailableArgument" : "Str.Startup.WaylandUnavailableEnvironment"), reason), logged.Add);

            exit.Should().Be(2);
            logged.Should().ContainSingle().Which.Should().Be(
                argument != null
                    ? "Native Wayland was required by --platform wayland, but no Wayland display could be reached: Unable to connect to Wayland display. Start with --platform x11 or --platform auto instead."
                    : "Native Wayland was required by HEXIDE_PLATFORM=wayland, but no Wayland display could be reached: Unable to connect to Wayland display. Set HEXIDE_PLATFORM to x11 or auto, or unset it.");
            stderr.ToString().Should().Be(logged.Single() + Environment.NewLine);
        }
        finally { Console.SetError(previous); }
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("x11")]
    public void Other_selections_do_not_handle_wayland_exceptions(string selection)
    {
        Action run = () => DisplayBackend.Run(selection,
            () => throw new Avalonia.Wayland.AvaloniaWaylandException("sentinel"),
            _ => throw new InvalidOperationException("must not format"), _ => throw new InvalidOperationException("must not log"));
        run.Should().Throw<Avalonia.Wayland.AvaloniaWaylandException>().WithMessage("sentinel");
    }

    [Fact]
    public void Required_wayland_does_not_handle_unrelated_exceptions()
    {
        Action run = () => DisplayBackend.Run("wayland", () => throw new InvalidOperationException("sentinel"),
            _ => throw new Exception("must not format"), _ => throw new Exception("must not log"));
        run.Should().Throw<InvalidOperationException>().WithMessage("sentinel");
    }

    [Fact]
    public void Successful_start_preserves_the_lifetime_exit_code()
    {
        DisplayBackend.Run("wayland", () => 7,
            _ => throw new Exception("must not format"), _ => throw new Exception("must not log")).Should().Be(7);
    }
}
