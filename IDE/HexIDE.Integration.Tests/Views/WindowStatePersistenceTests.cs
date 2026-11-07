using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.IDE;

namespace HexIDE.Integration.Tests.Views;

public class WindowStatePersistenceTests
{
    [AvaloniaFact]
    public async Task Restoring_then_resizing_saves_the_settled_normal_bounds()
    {
        await Check(async (window, settings, path, service) =>
        {
            window.WindowState = WindowState.Maximized;
            window.Position = new PixelPoint(-11, -11);
            window.Width = 1280;
            window.Height = 649;
            window.WindowState = WindowState.Normal;
            window.Position = new PixelPoint(0, 0);
            window.Width = 860;
            window.Height = 640;
            await Task.Delay(700, TestContext.Current.CancellationToken);
            settings.Window.Should().Be(new WindowData("Normal", 0, 0, 860, 640));
        });
    }

    [AvaloniaFact]
    public async Task Maximizing_saves_normal_bounds_with_the_maximized_flag()
    {
        await Check(async (window, settings, path, service) =>
        {
            window.WindowState = WindowState.Maximized;
            window.Position = new PixelPoint(-11, -11);
            window.Width = 1280;
            window.Height = 649;
            await Task.Delay(700, TestContext.Current.CancellationToken);
            service.SaveWindowState(window);
            settings.Window.Should().Be(new WindowData("Maximized", 120, 80, 800, 600));
        });
    }

    [AvaloniaFact]
    public async Task Moving_is_on_disk_without_deactivation_or_closing()
    {
        await Check(async (window, settings, path, service) =>
        {
            window.Position = new PixelPoint(240, 160);
            await Task.Delay(700, TestContext.Current.CancellationToken);
            new SettingsService(path).Window.Should()
                .Be(new WindowData("Normal", 240, 160, 800, 600));
        });
    }

    [AvaloniaFact]
    public async Task A_maximized_launch_applies_normal_bounds_before_maximizing()
    {
        await Check(async (window, settings, path, service) =>
        {
            window.SetRenderScaling(1.5);
            settings.Window = new WindowData("Maximized", 240, 160, 860, 640);
            service.RestoreWindowState(window);
            await Task.Delay(700, TestContext.Current.CancellationToken);
            window.WindowState.Should().Be(WindowState.Maximized);
            (window.Width, window.Height, window.Position).Should().Be((860, 640, new PixelPoint(240, 160)));
        });
    }

    [AvaloniaFact]
    public async Task Native_maximize_events_before_the_state_change_preserve_normal_bounds()
    {
        await Check(async (window, settings, path, service) =>
        {
            window.SetRenderScaling(1.5);
            // Windows sends the maximized move/resize before reporting the new state (#3088).
            var resizedWhileNormal = false;
            window.Resized += (_, _) => resizedWhileNormal |= window.WindowState == WindowState.Normal;
            window.Position = new PixelPoint(-11, -11);
            window.Width = 1280;
            window.Height = 649;
            window.UpdateLayout();
            // Headless acknowledges resize asynchronously; deliver it before the state change.
            await Dispatcher.UIThread.InvokeAsync(() => window.WindowState = WindowState.Maximized,
                DispatcherPriority.Background);
            resizedWhileNormal.Should().BeTrue();
            await Task.Delay(700, TestContext.Current.CancellationToken);
            new SettingsService(path).Window.Should()
                .Be(new WindowData("Maximized", 120, 80, 800, 600));
        });
    }

    [AvaloniaFact]
    public async Task Maximized_relaunch_arranges_the_saved_size_before_maximizing()
    {
        await Check(async (window, settings, path, service) =>
        {
            window.SetRenderScaling(1.5);
            Size? sizeAtMaximize = null;
            window.PropertyChanged += (_, e) =>
            {
                if (e.Property == Window.WindowStateProperty && window.WindowState == WindowState.Maximized)
                    sizeAtMaximize = window.ClientSize;
            };
            settings.Window = new WindowData("Maximized", 240, 160, 860, 640);
            service.RestoreWindowState(window);
            service.TrackWindowState(window);
            await Task.Delay(700, TestContext.Current.CancellationToken);
            // Headless has no native restore rectangle: observe the client size at the maximize request.
            sizeAtMaximize.Should().Be(new Size(860, 640));
            settings.Window.Should().Be(new WindowData("Maximized", 240, 160, 860, 640));
            window.WindowState = WindowState.Normal;
            window.UpdateLayout();
            window.ClientSize.Should().Be(new Size(860, 640));
            await Task.Delay(700, TestContext.Current.CancellationToken);
            new SettingsService(path).Window.Should()
                .Be(new WindowData("Normal", 240, 160, 860, 640));
        }, track: false);
    }

    [AvaloniaFact]
    public async Task A_disconnected_monitor_position_restores_on_the_primary_screen()
    {
        await Check((window, settings, path, service) =>
        {
            window.SetRenderScaling(1.5);
            settings.Window = new WindowData("Normal", 100000, 100000, 860, 640);
            service.RestoreWindowState(window);
            window.Position.Should().Be(window.Screens.Primary!.WorkingArea.Position);
            return Task.CompletedTask;
        });
    }

    [AvaloniaTheory]
    [InlineData(true, 1280, 800, 100, 100, 1280, 720, 0, 0)]
    [InlineData(false, 1600, 1000, 100, 100, 1280, 720, 0, 0)]
    [InlineData(false, 860, 640, 0, 0, 860, 640, 0, 0)]
    public async Task Restored_bounds_fit_the_working_area(bool fresh, int width, int height, int x, int y,
        int expectedWidth, int expectedHeight, int expectedX, int expectedY)
    {
        await Check(async (window, settings, path, service) =>
        {
            window.SetRenderScaling(1.5);
            window.MinWidth = 640;
            window.MinHeight = 480;
            settings.Window = fresh ? WindowData.Default : new WindowData("Maximized", x, y, width, height);
            service.RestoreWindowState(window, new PixelRect(0, 0, 1920, 1080), 1.5);
            await Task.Delay(700, TestContext.Current.CancellationToken);
            window.WindowState.Should().Be(WindowState.Maximized);
            window.WindowState = WindowState.Normal;
            window.UpdateLayout();
            window.ClientSize.Should().Be(new Size(expectedWidth, expectedHeight));
            window.Position.Should().Be(new PixelPoint(expectedX, expectedY));
        }, track: false);
    }

    [AvaloniaTheory]
    [InlineData(640, 640, 0)]
    [InlineData(1000, 640, 0)]
    public async Task Restoring_fits_the_drawn_frame_without_invisible_borders(int savedHeight,
        int expectedHeight, int expectedY)
    {
        await Check((window, settings, path, service) =>
        {
            window.SetRenderScaling(1.5);
            settings.Window = new WindowData("Normal", 0, savedHeight > 640 ? 100 : 0, 860, savedHeight);
            // The outer frame includes 8 physical pixels on the left, right and bottom.
            service.RestoreWindowState(window, new PixelRect(0, 0, 1920, 1008), 1.5,
                (new Size(810 + 2.0 / 3, 637 + 1.0 / 3),
                    new PixelRect(0, 0, 1216, 956), new PixelRect(8, 0, 1200, 948)));
            window.Width.Should().Be(860);
            window.Height.Should().Be(expectedHeight);
            window.Position.Should().Be(new PixelPoint(0, expectedY));
            if (savedHeight > 640)
                (window.Position.Y + (window.Height + 32) * 1.5).Should().Be(1008);
            return Task.CompletedTask;
        }, track: false);
    }

    private static async Task Check(Func<Window, SettingsService, string, WindowStateService, Task> check, bool track = true)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-window-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var settings = new SettingsService(Path.Combine(directory, "settings.json"));
        var window = new Window { Width = 800, Height = 600, Position = new PixelPoint(120, 80),
            WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            window.Show();
            var service = new WindowStateService(settings);
            if (track)
                service.TrackWindowState(window);
            await check(window, settings, Path.Combine(directory, "settings.json"), service);
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, true);
        }
    }
}
