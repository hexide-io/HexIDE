using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.Desktop.Server;
using ModelContextProtocol.Server;

namespace HexIDE.Integration.Tests.Automation;

public class WindowActivationTests
{
    private static void Active(Window window, bool active) => typeof(WindowBase)
        .GetMethod(active ? "HandleActivated" : "HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(window, null);

    private sealed class Scene : IDisposable
    {
        private readonly FieldInfo lifetime = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(IApplicationLifetime));
        private readonly object? previous;
        public Window Main { get; } = new() { Title = "Main", Width = 300, Height = 200, ShowActivated = false };
        public Window Other { get; }
        public int Requests { get; private set; }
        public bool Confirm { get; set; } = true;

        public Scene()
        {
            var backing = new Window();
            Other = new Window(PopupBoundsTests.ForwardingPlatform<IWindowImpl>.Wrap((method, args) =>
            {
                if (method.Name == "Activate")
                {
                    Requests++;
                    // Deliver the platform callback later, through the real dispatcher, after Activate returns.
                    if (Confirm) Dispatcher.UIThread.Post(() => { Active(Main, false); Active(Other!, true); }, DispatcherPriority.Background);
                    return null;
                }
                return method.Invoke(backing.PlatformImpl, args);
            })) { Title = "Other", Width = 200, Height = 100, ShowActivated = false };
            previous = lifetime.GetValue(Application.Current!);
            var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
            desktop.MainWindow.Returns(Main);
            desktop.Windows.Returns(new[] { Main, Other });
            lifetime.SetValue(Application.Current!, desktop);
            Main.Show();
            Other.Show();
        }

        public void Dispose()
        {
            Active(Other, false);
            Active(Main, false);
            Other.Close();
            Main.Close();
            lifetime.SetValue(Application.Current!, previous);
        }
    }

    // Reflection lets these regressions run against the old signature too, to measure their teeth.
    private static Task<WindowStateChangeResult> Call(string? state = null, bool activate = true, string window = "Other")
    {
        var method = typeof(HexIdeTools).GetMethod(nameof(HexIdeTools.SetWindowStateAsync))!;
        return (Task<WindowStateChangeResult>)method.Invoke(new HexIdeTools(null!), method.GetParameters().Select(p => p.Name switch
        {
            "state" => (object?)state, "activate" => activate, "window" => window,
            "ct" => TestContext.Current.CancellationToken, _ => p.DefaultValue,
        }).ToArray())!;
    }

    [AvaloniaFact]
    public async Task No_active_process_window_refuses_before_changing_state()
    {
        using var scene = new Scene();
        var reply = await Call("Maximized");
        reply.Success.Should().BeFalse();
        reply.Error.Should().Be("No HexIDE window is active, so activating 'Other' would take the foreground from another application, which this tool doesn't do.");
        scene.Other.WindowState.Should().Be(WindowState.Normal);
        scene.Requests.Should().Be(0);
    }

    [AvaloniaFact]
    public async Task Modal_over_main_is_named_and_nothing_changes()
    {
        using var scene = new Scene();
        var dialog = new Window { Title = "Answer me", ShowActivated = false };
        _ = dialog.ShowDialog(scene.Main);
        Active(scene.Other, true);
        try
        {
            var reply = await Call("Maximized", window: "ide");
            reply.Success.Should().BeFalse();
            reply.Error.Should().Contain("Answer me").And.Contain("Answer the dialog first");
            scene.Main.WindowState.Should().Be(WindowState.Normal);
        }
        finally { dialog.Close(); }
    }

    [AvaloniaFact]
    public async Task Switch_waits_for_platform_callback_and_reports_both_windows()
    {
        using var scene = new Scene();
        Active(scene.Main, true);
        var reply = await Call();
        reply.Success.Should().BeTrue(reply.Error);
        scene.Requests.Should().Be(1);
        var json = JsonSerializer.SerializeToElement(reply, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        json.GetProperty("window").GetProperty("isActive").GetBoolean().Should().BeTrue();
        json.GetProperty("previouslyActive").GetProperty("name").GetString().Should().Be("ide");
        json.GetProperty("previouslyActive").GetProperty("isActive").GetBoolean().Should().BeFalse();
        scene.Other.WindowState.Should().Be(WindowState.Normal);
    }

    [AvaloniaFact]
    public async Task Unconfirmed_activation_reports_timeout_and_actual_state()
    {
        using var scene = new Scene { Confirm = false };
        Active(scene.Main, true);
        var reply = await Call();
        reply.Success.Should().BeFalse();
        reply.Error.Should().Contain("activation").And.Contain("1 s");
        scene.Main.IsActive.Should().BeTrue();
        scene.Other.IsActive.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task No_state_or_activation_explains_what_to_pass()
    {
        using var scene = new Scene();
        var reply = await Call(activate: false);
        reply.Success.Should().BeFalse();
        reply.Error.Should().Contain("state").And.Contain("activate: true");
    }

    [Fact]
    public void Schema_requires_neither_state_nor_activate()
    {
        var tool = McpServerTool.Create(typeof(HexIdeTools).GetMethod(nameof(HexIdeTools.SetWindowStateAsync))!, new HexIdeTools(null!));
        var schema = tool.ProtocolTool.InputSchema;
        schema.GetProperty("properties").TryGetProperty("activate", out _).Should().BeTrue();
        var required = schema.TryGetProperty("required", out var value) ? value.EnumerateArray().Select(p => p.GetString()).ToArray() : [];
        required.Should().NotContain("state").And.NotContain("activate");
    }

    [AvaloniaFact]
    public async Task Click_on_inactive_window_keeps_pointer_route_and_names_active_window()
    {
        using var scene = new Scene();
        var pad = new Avalonia.Controls.Border { Background = Brushes.White };
        scene.Main.Content = pad;
        scene.Main.UpdateLayout();
        Active(scene.Other, true);
        var pressed = 0;
        var released = 0;
        pad.PointerPressed += (_, _) => pressed++;
        pad.PointerReleased += (_, _) => released++;
        var reply = await UiAutomationDriver.FinishAsync(await UiAutomationDriver.InteractAsync(pad, "click", "20,20"));
        reply.Success.Should().BeTrue(reply.Error);
        pressed.Should().Be(1);
        released.Should().Be(1);
        reply.Detail.Should().Contain("still active").And.Contain("Other").And.Contain("set_window_state").And.Contain("activate: true");
    }
}
