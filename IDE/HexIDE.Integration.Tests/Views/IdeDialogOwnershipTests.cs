using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Integration.Tests.Automation;
using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Views;

public class IdeDialogOwnershipTests
{
    internal static void Activate(Window window) =>
        typeof(WindowBase).GetMethod("HandleActivated", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, null);

    // vb6.exe SP6 measurements (2026-11-16): https://github.com/hexide-io/HexIDE/issues/1297
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Runtime_error_holds_ide_without_activating_it_and_Debug_returns_to_ide(bool debug)
    {
        var activation = new IdeActivationProbe();
        var ide = activation.Window;
        var form = new Window();
        var disabled = new Window { IsEnabled = false };
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        desktop.MainWindow.Returns(ide);
        desktop.Windows.Returns(new[] { ide, form, disabled });
        var manager = new WindowManager(Substitute.For<ILocalizationService>());
        var locator = new ViewLocator();
        Application.Current!.DataTemplates.Add(locator);
        try
        {
            ide.Show();
            form.Show();
            Activate(form);
            var vm = new RuntimeErrorViewModel("Run-time error '5'", canDebug: true);
            var answers = new List<bool>();
            vm.CloseRequested += answers.Add;
            var pending = manager.ShowDialog(desktop, vm);
            Dispatcher.UIThread.RunJobs();
            var dialog = form.OwnedWindows.Single();
            dialog.Owner.Should().BeSameAs(form);
            activation.Requests.Should().Be(0);
            ide.IsEnabled.Should().BeFalse();
            disabled.IsEnabled.Should().BeFalse();
            Activate(dialog);
            var view = dialog.GetVisualDescendants().OfType<RuntimeErrorView>().Single();
            dialog.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var button = view.GetVisualDescendants().OfType<Button>().ElementAt(debug ? 2 : 1);
            dialog.IsLoaded.Should().BeTrue();
            button.Focus().Should().BeTrue();
            dialog.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            dialog.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            answers.Should().Equal(!debug);
            (await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Should().Be(!debug);
            ide.IsEnabled.Should().BeTrue();
            disabled.IsEnabled.Should().BeFalse();
            activation.Requests.Should().Be(debug ? 1 : 0);
        }
        finally
        {
            foreach (var dialog in form.OwnedWindows.ToArray()) dialog.Close();
            form.Close();
            ide.Close();
            disabled.Close();
            Application.Current!.DataTemplates.Remove(locator);
        }
    }

    // Owner selection for the Linux stacking regression measured in #5003.
    [AvaloniaTheory]
    [InlineData("modal")]
    [InlineData("modeless")]
    [InlineData("program")]
    public async Task Message_box_uses_active_ide_dialog_or_visible_main_window(string kind)
    {
        var ide = new Window();
        Window active = kind == "program" ? new Window() : new DialogWindow();
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        desktop.MainWindow.Returns(ide);
        desktop.Windows.Returns(new[] { ide, active });
        var manager = new WindowManager(Substitute.For<ILocalizationService>());
        var previousLifetime = Application.Current!.ApplicationLifetime;
        var lifetimeField = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(IApplicationLifetime));
        lifetimeField.SetValue(Application.Current, desktop);
        try
        {
            ide.Show();
            if (kind == "modal") _ = active.ShowDialog<bool>(ide);
            else active.Show();
            Activate(active);
            var pending = manager.MessageBox("IDE message", null, MessageBoxButtons.Ok, MessageBoxIcon.None, null);
            Dispatcher.UIThread.RunJobs();
            var box = ide.OwnedWindows.Concat(active.OwnedWindows).Single(window => window != active);
            box.Owner.Should().BeSameAs(kind == "program" ? ide : active);
            // A box over the modeless Find window holds the IDE too; elsewhere the box's owner is the IDE or modal over it.
            ide.IsEnabled.Should().Be(kind != "modeless");
            box.Close();
            (await pending).Should().Be(MessageBoxResult.None);
            ide.IsEnabled.Should().BeTrue();
        }
        finally
        {
            foreach (var box in active.OwnedWindows.Concat(ide.OwnedWindows).Where(window => window != active).ToArray()) box.Close();
            active.Close();
            ide.Close();
            lifetimeField.SetValue(Application.Current, previousLifetime);
        }
    }

    // Native Wayland keeps the main window as owner until AvaloniaUI/Avalonia#22318 ships: a dialog disabled while a key
    // is down repeats that key into the box it raised, and Enter on Discard answered Yes by itself (#5021).
    [AvaloniaFact]
    public async Task Message_box_keeps_main_window_owner_on_native_wayland()
    {
        var ide = new Window();
        var active = new DialogWindow();
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        desktop.MainWindow.Returns(ide);
        desktop.Windows.Returns(new Window[] { ide, active });
        var manager = new WindowManager(Substitute.For<ILocalizationService>());
        var previousLifetime = Application.Current!.ApplicationLifetime;
        var lifetimeField = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(IApplicationLifetime));
        lifetimeField.SetValue(Application.Current, desktop);
        NativeWayland.ForTest = _ => true;
        try
        {
            ide.Show();
            _ = active.ShowDialog<bool>(ide);
            Activate(active);
            var pending = manager.MessageBox("IDE message", null, MessageBoxButtons.Ok, MessageBoxIcon.None, null);
            Dispatcher.UIThread.RunJobs();
            var box = ide.OwnedWindows.Concat(active.OwnedWindows).Single(window => window != active);
            box.Owner.Should().BeSameAs(ide);
            box.Close();
            (await pending).Should().Be(MessageBoxResult.None);
        }
        finally
        {
            NativeWayland.ForTest = null;
            foreach (var box in active.OwnedWindows.Concat(ide.OwnedWindows).Where(window => window != active).ToArray()) box.Close();
            active.Close();
            ide.Close();
            lifetimeField.SetValue(Application.Current, previousLifetime);
        }
    }

    [AvaloniaTheory]
    [InlineData("modal")]
    [InlineData("modeless")]
    public async Task Ide_dialog_uses_active_ide_dialog(string kind)
    {
        var ide = new Window();
        var active = new DialogWindow();
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        desktop.MainWindow.Returns(ide);
        desktop.Windows.Returns(new Window[] { ide, active });
        var manager = new WindowManager(Substitute.For<ILocalizationService>());
        try
        {
            ide.Show();
            if (kind == "modal") _ = active.ShowDialog<bool>(ide);
            else active.Show(ide);
            Activate(active);
            var dialog = Substitute.For<IDialog>();
            dialog.Title.Returns("Nested IDE dialog");
            var pending = manager.ShowDialog(desktop, dialog);
            Dispatcher.UIThread.RunJobs();
            var shown = ide.OwnedWindows.Concat(active.OwnedWindows).Single(window => window != active);
            shown.Owner.Should().BeSameAs(active);
            ide.IsEnabled.Should().Be(kind == "modal");
            shown.Close(true);
            (await pending).Should().BeTrue();
            ide.IsEnabled.Should().BeTrue();
        }
        finally
        {
            foreach (var dialog in active.OwnedWindows.Concat(ide.OwnedWindows).Where(window => window != active).ToArray()) dialog.Close();
            active.Close();
            ide.Close();
        }
    }

    // The Find window is shown modeless. Owned by the IDE, it stays above it, and so does a box it raises (#5003).
    // Avalonia refuses a hidden owner, so with the IDE hidden and nothing active it opens unowned rather than throwing.
    [AvaloniaTheory]
    [InlineData("visible")]
    [InlineData("hidden")]
    public async Task Modeless_ide_window_is_owned_by_visible_main_window(string state)
    {
        var ide = new Window();
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        desktop.MainWindow.Returns(ide);
        desktop.Windows.Returns(new[] { ide });
        var manager = new WindowManager(Substitute.For<ILocalizationService>());
        var previousLifetime = Application.Current!.ApplicationLifetime;
        var lifetimeField = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(IApplicationLifetime));
        lifetimeField.SetValue(Application.Current, desktop);
        try
        {
            if (state == "visible")
            {
                ide.Show();
                Activate(ide);
            }
            var dialog = Substitute.For<IDialog>();
            dialog.Title.Returns("Find");
            var pending = manager.ShowWindow(dialog);
            Dispatcher.UIThread.RunJobs();
            pending.IsFaulted.Should().BeFalse();
            if (state == "visible")
            {
                var shown = ide.OwnedWindows.Should().ContainSingle().Which;
                shown.Should().BeOfType<DialogWindow>();
                shown.Owner.Should().BeSameAs(ide);
                ide.IsEnabled.Should().BeTrue();
            }
            else
            {
                ide.OwnedWindows.Should().BeEmpty();
            }
            dialog.CloseRequested += Raise.Event<Action<bool>>(true);
            Dispatcher.UIThread.RunJobs();
            await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            foreach (var window in ide.OwnedWindows.ToArray()) window.Close();
            ide.Close();
            lifetimeField.SetValue(Application.Current, previousLifetime);
        }
    }

    [AvaloniaTheory]
    [InlineData("visible")]
    [InlineData("hidden")]
    [InlineData("minimized")]
    public async Task Ide_dialog_prefers_visible_main_window(string state)
    {
        var ide = new Window();
        var form = new Window();
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        desktop.MainWindow.Returns(ide);
        desktop.Windows.Returns(new[] { ide, form });
        var manager = new WindowManager(Substitute.For<ILocalizationService>());
        try
        {
            if (state != "hidden") ide.Show();
            if (state == "minimized") ide.WindowState = WindowState.Minimized;
            form.Show();
            Activate(form);
            // Runtime errors deliberately keep the active-window owner; an ordinary IDialog does not.
            var dialog = Substitute.For<IDialog>();
            dialog.Title.Returns("IDE state");
            var pending = manager.ShowDialog(desktop, dialog);
            Dispatcher.UIThread.RunJobs();
            var owner = state == "visible" ? ide : form;
            var shown = ide.OwnedWindows.Concat(form.OwnedWindows).Single();
            shown.Owner.Should().BeSameAs(owner);
            shown.Close(true);
            (await pending).Should().BeTrue();
        }
        finally
        {
            foreach (var dialog in ide.OwnedWindows.Concat(form.OwnedWindows).ToArray()) dialog.Close();
            form.Close();
            ide.Close();
        }
    }
}

// Headless Activate is a no-op. Observe the actual platform activation request while retaining headless rendering/input.
internal sealed class IdeActivationProbe
{
    public Window Window { get; }
    public int Requests { get; private set; }

    public IdeActivationProbe()
    {
        var inner = new Window().PlatformImpl!;
        var platform = PopupBoundsTests.ForwardingPlatform<IWindowImpl>.Wrap((method, args) =>
        {
            if (method.Name == "Activate") Requests++;
            return method.Invoke(inner, args);
        });
        Window = new Window(platform) { Width = 1600, Height = 900 };
    }
}
