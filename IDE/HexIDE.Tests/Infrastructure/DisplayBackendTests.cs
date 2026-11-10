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
}
