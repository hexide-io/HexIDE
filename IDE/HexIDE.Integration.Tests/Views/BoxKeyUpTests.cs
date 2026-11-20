using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Views;

// Headless input rows from https://github.com/hexide-io/HexIDE/issues/5021.
public class BoxKeyUpTests
{
    [AvaloniaTheory]
    [InlineData(MessageBoxLayout.None, Key.Enter, 3, false, MessageBoxResult.None)]
    [InlineData(MessageBoxLayout.SecondButtonDefault, Key.Enter, 1, false, MessageBoxResult.None)]
    [InlineData(MessageBoxLayout.None, Key.Enter, 1, true, MessageBoxResult.Yes)]
    [InlineData(MessageBoxLayout.SecondButtonDefault, Key.Enter, 1, true, MessageBoxResult.No)]
    [InlineData(MessageBoxLayout.None, Key.Enter, 0, true, MessageBoxResult.None)]
    [InlineData(MessageBoxLayout.None, Key.Space, 1, false, MessageBoxResult.None)]
    [InlineData(MessageBoxLayout.None, Key.Space, 0, true, MessageBoxResult.None)]
    [InlineData(MessageBoxLayout.None, Key.Space, 1, true, MessageBoxResult.Yes)]
    public Task Ide_box_answers_only_after_a_complete_press(MessageBoxLayout layout, Key key,
        int presses, bool release, MessageBoxResult expected) =>
        WithDesktopBox((manager, _, ide) =>
            manager.MessageBox("IDE message", null, MessageBoxButtons.YesNo, MessageBoxIcon.None, ide, layout: layout),
            async (box, pending) =>
            {
                if (key == Key.Space)
                    box.GetVisualDescendants().OfType<Button>().First(button => button.IsDefault).Focus();
                SendKeys(box, pending, key, presses, release);
                if (expected == MessageBoxResult.None)
                    pending.IsCompleted.Should().BeFalse();
                else
                    (await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Should().Be(expected);
            });

    [AvaloniaTheory]
    [InlineData(Key.Enter, 1, false, false)]
    [InlineData(Key.Escape, 1, false, false)]
    [InlineData(Key.Enter, 1, true, true)]
    [InlineData(Key.Escape, 1, true, true)]
    [InlineData(Key.Enter, 0, true, false)]
    [InlineData(Key.Space, 0, true, false)]
    public Task Save_changes_answers_only_after_a_complete_press(Key key, int presses, bool release, bool answers)
    {
        var dialog = new SaveChangesViewModel();
        var responses = new List<bool>();
        dialog.CloseRequested += responses.Add;
        return WithDesktopBox((manager, desktop, _) => manager.ShowDialog(desktop, dialog),
            async (box, pending) =>
            {
                SendKeys(box, pending, key, presses, release);
                if (answers)
                {
                    var saves = key == Key.Enter;
                    (await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Should().Be(saves);
                    responses.Should().ContainSingle().Which.Should().Be(saves);
                    dialog.SaveChanges.Should().Be(saves);
                }
                else
                {
                    pending.IsCompleted.Should().BeFalse();
                    responses.Should().BeEmpty();
                }
            });
    }

    [AvaloniaTheory]
    [InlineData(MessageBoxButtons.YesNo, false)]
    [InlineData(MessageBoxButtons.YesNo, true)]
    [InlineData(MessageBoxButtons.OkCancel, false)]
    [InlineData(MessageBoxButtons.OkCancel, true)]
    public Task Escape_leaves_an_ide_box_open(MessageBoxButtons buttons, bool release) =>
        WithDesktopBox((manager, _, ide) => manager.MessageBox("IDE message", null, buttons, MessageBoxIcon.None, ide),
            (box, pending) =>
            {
                SendKeys(box, pending, Key.Escape, 1, release);
                pending.IsCompleted.Should().BeFalse();
                return Task.CompletedTask;
            });

    [AvaloniaFact]
    public Task Program_box_answers_on_enter_down() =>
        WithDesktopBox((manager, _, ide) =>
            manager.MessageBox("Program message", null, MessageBoxButtons.OkCancel, MessageBoxIcon.None, ide, isProgramDialog: true),
            async (box, pending) =>
            {
                box.GetVisualDescendants().OfType<Button>().First().Focus().Should().BeTrue();
                SendKeys(box, pending, Key.Enter, 1, false);
                (await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Should().Be(MessageBoxResult.Ok);
            });

    private static void SendKeys(Window box, Task pending, Key key, int presses, bool release)
    {
        var physicalKey = key switch
        {
            Key.Enter => PhysicalKey.Enter,
            Key.Escape => PhysicalKey.Escape,
            _ => PhysicalKey.Space
        };
        for (var i = 0; i < presses && !pending.IsCompleted; i++)
        {
            box.KeyPress(key, RawInputModifiers.None, physicalKey, null);
            Dispatcher.UIThread.RunJobs();
        }
        if (release && !pending.IsCompleted)
        {
            box.KeyRelease(key, RawInputModifiers.None, physicalKey, null);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task WithDesktopBox<T>(
        Func<WindowManager, IClassicDesktopStyleApplicationLifetime, Window, Task<T>> show,
        Func<Window, Task<T>, Task> check)
    {
        var ide = new Window();
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        desktop.MainWindow.Returns(ide);
        desktop.Windows.Returns(new[] { ide });
        var manager = new WindowManager(Substitute.For<ILocalizationService>());
        var app = Application.Current!;
        var previousLifetime = app.ApplicationLifetime;
        var lifetimeField = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(IApplicationLifetime));
        var locator = new ViewLocator();
        app.DataTemplates.Add(locator);
        lifetimeField.SetValue(app, desktop);
        try
        {
            ide.Show();
            IdeDialogOwnershipTests.Activate(ide);
            var pending = show(manager, desktop, ide);
            Dispatcher.UIThread.RunJobs();
            var box = ide.OwnedWindows.Single();
            IdeDialogOwnershipTests.Activate(box);
            box.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            await check(box, pending);
        }
        finally
        {
            foreach (var box in ide.OwnedWindows.ToArray()) box.Close();
            ide.Close();
            lifetimeField.SetValue(app, previousLifetime);
            app.DataTemplates.Remove(locator);
        }
    }
}
