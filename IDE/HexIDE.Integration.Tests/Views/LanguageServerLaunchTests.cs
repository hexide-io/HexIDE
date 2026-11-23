using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using HexIDE.Lsp;

using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Views;

public class LanguageServerLaunchTests
{
    // Keep the new cases runnable against the base before this UI exists.
    private static Type UiType(string name)
    {
        var type = typeof(WindowManager).Assembly.GetType("HexIDE." + name);
        type.Should().NotBeNull("the connection test dialog must exist");
        return type!;
    }

    [AvaloniaTheory]
    [InlineData("escape", false)]
    [InlineData("close", false)]
    [InlineData("cancel", false)]
    [InlineData("run", true)]
    [InlineData("enter", false)]
    public async Task OnlyRunAuthorizesThroughTheDialogInput(string action, bool expected)
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        var command = "env TOKEN=unchanged server --path \"long argument\" " + new string('x', 4096);
        var vmType = UiType("Forms.ViewModels.LanguageServerLaunchViewModel");
        var vm = (IDialog)Activator.CreateInstance(vmType, "probe", "Probe server", command, localization)!;
        var view = (UserControl)Activator.CreateInstance(UiType("Forms.Views.LanguageServerLaunchView"))!;
        view.DataContext = vm;
        var owner = new Window();
        owner.Show();
        var dialog = new DialogWindow { DataContext = vm, Content = view, Width = 620, Height = 480 };
        var result = dialog.ShowDialog<bool>(owner);
        try
        {
            Dispatcher.UIThread.RunJobs();
            view.FindControl<TextBox>("CommandText")!.Text.Should().Be(command);
            view.FindControl<TextBox>("CommandText")!.IsReadOnly.Should().BeTrue();
            view.FindControl<TextBox>("CommandText")!.TextWrapping.Should().Be(Avalonia.Media.TextWrapping.Wrap);
            view.FindControl<Button>("CancelButton")!.IsFocused.Should().BeTrue();
            if (action == "close") dialog.Close();
            else if (action is "escape" or "enter")
            {
                dialog.KeyPress(action == "escape" ? Key.Escape : Key.Enter, RawInputModifiers.None,
                    action == "escape" ? PhysicalKey.Escape : PhysicalKey.Enter, null);
                // The press closes the dialog; the owner receives the release.
                owner.KeyRelease(action == "escape" ? Key.Escape : Key.Enter, RawInputModifiers.None,
                    action == "escape" ? PhysicalKey.Escape : PhysicalKey.Enter, null);
            }
            else
            {
                var button = view.FindControl<Button>(action == "run" ? "RunButton" : "CancelButton")!;
                var peer = ControlAutomationPeer.CreatePeerForElement(button)!;
                ((IInvokeProvider)peer).Invoke();
            }
            Dispatcher.UIThread.RunJobs();
            (await result.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Should().Be(expected);
            ((bool)vmType.GetProperty("Agreed")!.GetValue(vm)!).Should().Be(expected);
        }
        finally { dialog.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task FailureToShowCannotAuthorize()
    {
        var windows = Substitute.For<IWindowManager>();
        windows.ShowDialog(Arg.Any<IDialog>()).Returns<Task<bool>>(_ => throw new InvalidOperationException("unavailable"));
        var confirmation = (ILanguageServerLaunchConfirmation)Activator.CreateInstance(
            UiType("IDE.LanguageServerLaunchConfirmation"), windows, new LocalizationService())!;
        (await confirmation.ConfirmLaunchAsync("probe", "Probe", "command", TestContext.Current.CancellationToken))
            .Should().BeFalse();
    }
}
