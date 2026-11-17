using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using HexIDE.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Utils;

namespace HexIDE.Integration.Tests.Views;

/// <summary>The single-view handle preserves the host behaviour extracted in #4068.</summary>
public class HostedFormTests
{
    private static IHostedForm Create(out TaskCompletionSource closed)
    {
        var completion = closed = new TaskCompletionSource();
        var windows = Substitute.For<IWindowManager>();
        windows.ShowManagedWindow(Arg.Any<MDIWindow>(), false).Returns(call =>
        {
            call.Arg<MDIWindow>().CloseCommand = new DelegateCommand(() => completion.TrySetResult(), () => true);
            return completion.Task;
        });
        return new ProgramHost(windows, Substitute.For<ILocalizationService>())
            .CreateForm(new HostedFormRequest("Form", 300, 200, 0, 0));
    }

    [AvaloniaFact]
    public void A_held_window_stops_real_pointer_and_key_input_and_reports_the_press()
    {
        var hosted = Create(out _);
        var button = new Button { Content = "button", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch };
        hosted.SetContent(button);
        var window = new Window { Width = 320, Height = 240, Content = hosted.Window };
        // The IDE inlines its MDI theme into App's markup. Supply the two named parts the real MDIWindow requires;
        // pointer and keyboard events still travel through that window's actual tunnel handlers.
        ((MDIWindow)hosted.Window).Template = new FuncControlTemplate<MDIWindow>((owner, scope) =>
        {
            var title = new Border { Height = 20 };
            var presenter = new ContentPresenter { Content = owner.Content };
            scope.Register("PART_TitleBar", title);
            scope.Register("PART_ContentPresenter", presenter);
            DockPanel.SetDock(title, Avalonia.Controls.Dock.Top);
            return new DockPanel { Background = Brushes.Transparent, Children = { title, presenter } };
        });
        var pressed = 0;
        var keys = 0;
        var clicks = 0;
        hosted.HeldPressed += () => pressed++;
        button.Click += (_, _) => clicks++;
        button.KeyDown += (_, _) => keys++;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            button.Focus();
            hosted.IsHeld = true;
            window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, null);
            window.MouseDown(new Point(100, 100), MouseButton.Left);
            window.MouseUp(new Point(100, 100), MouseButton.Left);
            pressed.Should().Be(1);
            keys.Should().Be(0);
            clicks.Should().Be(0);
            button.IsHitTestVisible.Should().BeFalse();
            hosted.IsHeld = false;
            button.IsHitTestVisible.Should().BeTrue();
            button.Focus();
            window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, null);
            keys.Should().Be(1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_close_box_can_be_refused_but_the_program_can_close_without_asking()
    {
        var hosted = Create(out var closed);
        var shown = hosted.Show(false);
        var requests = 0;
        hosted.Closing = () => { requests++; return true; };
        ((MDIWindow)hosted.Window).CloseCommand.Execute(null);
        requests.Should().Be(1);
        shown.IsCompleted.Should().BeFalse();
        hosted.Close();
        closed.Task.IsCompleted.Should().BeTrue();
        requests.Should().Be(1);
    }

    [AvaloniaFact]
    public void A_minimised_extent_is_empty_even_with_a_pending_size_assignment()
    {
        // Preserves Extent's measured Resize contract: docs/vb6-fidelity-oracle/a-forms-resize-activate-and-deactivate.md.
        var hosted = Create(out _);
        hosted.Width = 420;
        hosted.Height = 230;
        hosted.Extent.Should().Be((420, 230, HostedFormState.Normal));
        MDIHostPanel.SetWindowState((MDIWindow)hosted.Window, WindowState.Minimized);
        hosted.Extent.Should().Be((0, 0, HostedFormState.Minimized));
        MDIHostPanel.SetWindowState((MDIWindow)hosted.Window, WindowState.Normal);
        hosted.Extent.Should().Be((420, 230, HostedFormState.Normal));
    }
}
