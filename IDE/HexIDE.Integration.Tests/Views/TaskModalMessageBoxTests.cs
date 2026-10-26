using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Dialogs;

namespace HexIDE.Integration.Tests.Views;

public class TaskModalMessageBoxTests
{
    [AvaloniaTheory]
    [InlineData(false, MessageBoxResult.Yes)]
    [InlineData(false, MessageBoxResult.No)]
    [InlineData(true, MessageBoxResult.Yes)]
    [InlineData(true, MessageBoxResult.No)]
    public async Task HoldsEveryWindowAndRestoresItsState(bool minimized, MessageBoxResult answer)
    {
        var ide = new Window();
        var form = new Window();
        var disabled = new Window { IsEnabled = false };
        var hidden = new Window();
        var windows = new List<Window> { ide, form, disabled, hidden };
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        desktop.Windows.Returns(windows);
        desktop.MainWindow.Returns(ide);
        try
        {
            ide.Show();
            form.Show();
            disabled.Show();
            if (minimized) ide.WindowState = WindowState.Minimized;
            var owner = minimized ? form : ide;
            var other = minimized ? ide : form;
            var manager = new WindowManager(Substitute.For<ILocalizationService>());
            var pending = manager.TaskModalMessageBox(desktop, "Reset?", null, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, owner);
            Dispatcher.UIThread.RunJobs();
            var prompt = owner.OwnedWindows.Single();
            windows.Add(prompt);

            pending.IsCompleted.Should().BeFalse();
            other.IsEnabled.Should().BeFalse("the prompt must hold both the IDE and the running program");
            hidden.IsEnabled.Should().BeFalse("hidden windows must stay held if shown under the question");
            disabled.IsEnabled.Should().BeFalse();
            prompt.IsEnabled.Should().BeTrue();
            var box = (MessageBox)prompt.Content!;
            var button = box.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, answer.ToString()));
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            (await pending).Should().Be(answer);
            other.IsEnabled.Should().BeTrue();
            hidden.IsEnabled.Should().BeTrue();
            disabled.IsEnabled.Should().BeFalse("a pre-existing disabled state must survive the prompt");
        }
        finally
        {
            foreach (var window in windows.ToArray()) window.Close();
        }
    }
}
