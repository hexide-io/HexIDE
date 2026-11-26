using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The notice HexIDE shows when the hourly check revokes a loaded add-in opens by itself, maybe while the user is typing in a
/// code window, so no key meant for the editor may start closing HexIDE (#5577).
/// </summary>
public class AddinRevokedNoticeKeyboardTests
{
    [AvaloniaTheory]
    [InlineData("Enter")]
    [InlineData("Space")]
    [InlineData("Escape")]
    public void A_key_pressed_as_the_notice_opens_answers_Later_and_never_closes_HexIDE(string key)
    {
        WithNotice((window, notice) =>
        {
            var answers = new List<bool>();
            notice.CloseRequested += answers.Add;

            UiAutomationDriver.PressKey(window, key, null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();

            answers.Should().Equal([false], "Later is the notice's default and its cancel, and Close HexIDE is neither");
        });
    }

    private static void WithNotice(Action<Window, AddinRevokedNoticeViewModel> check)
    {
        var notice = new AddinRevokedNoticeViewModel(["Probe"], Substitute.For<ILocalizationService>());
        var view = new AddinRevokedNotice { DataContext = notice };
        var window = new WindowManager(Substitute.For<ILocalizationService>()).MakeDialogWindow("Add-in revoked", view);
        ContainerRuntimeHarness.MergeAppResources(window);
        using (var strings = AssetLoader.Open(new Uri("avares://HexIDE/Localization/Packs/en.json")))
            foreach (var (name, value) in JsonSerializer.Deserialize(strings, LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!)
                window.Resources[name] = value;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            check(window, notice);
        }
        finally { window.Close(); }
    }
}
