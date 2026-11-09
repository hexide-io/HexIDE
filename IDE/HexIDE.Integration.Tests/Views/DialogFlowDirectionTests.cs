using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Dialogs;

namespace HexIDE.Integration.Tests.Views;

public class DialogFlowDirectionTests
{
    [AvaloniaTheory]
    [InlineData("ar", "About")]
    [InlineData("ar", "Input")]
    [InlineData("ar", "Message")]
    [InlineData("ar", "Font")]
    [InlineData("ar", "Color")]
    [InlineData("ar", "Print")]
    [InlineData("he", "About")]
    [InlineData("he", "Input")]
    [InlineData("he", "Message")]
    [InlineData("he", "Font")]
    [InlineData("he", "Color")]
    [InlineData("he", "Print")]
    public async Task DialogFollowsActiveLanguage(string language, string dialog)
        => await CheckDialogDirection(language, dialog, false, FlowDirection.RightToLeft);

    [AvaloniaTheory]
    [InlineData("Message")]
    [InlineData("Input")]
    public async Task ProgramDialogStaysLeftToRightUnderHebrew(string dialog)
        => await CheckDialogDirection("he", dialog, true, FlowDirection.LeftToRight);

    private static async Task CheckDialogDirection(string language, string dialog, bool isProgramDialog, FlowDirection expectedDirection)
    {
        var localization = new LocalizationService();
        var previousLanguage = localization.ActiveLanguage;
        var owner = new Window();
        try
        {
            localization.Apply(language);
            owner.FlowDirection = localization.FlowDirection;
            owner.Show();
            Control content = dialog switch
            {
                "About" => new AboutDialog { DataContext = new AboutDialogViewModel(new AboutDialogOptions { Title = "HexIDE" }, localization) },
                "Input" => isProgramDialog ? new HexIDE.Runtime.Dialogs.InputBox { IsProgramDialog = true } : new InputBox(),
                "Message" => isProgramDialog ? new HexIDE.Runtime.Dialogs.MessageBox { IsProgramDialog = true } : new MessageBox(),
                "Font" => new FontDialog { DataContext = new FontDialogViewModel(null) },
                "Color" => new ColorDialog(0),
                "Print" => new PrintDialog(),
                _ => throw new ArgumentOutOfRangeException(nameof(dialog))
            };
            var window = new WindowManager(localization).MakeDialogWindow(dialog, content);
            var pending = window.ShowDialog(owner);
            Dispatcher.UIThread.RunJobs();
            try
            {
                pending.IsCompleted.Should().BeFalse();
                window.FlowDirection.Should().Be(expectedDirection);
                ((Control)window.Content!).FlowDirection.Should().Be(expectedDirection);
            }
            finally { window.Close(); }
            await pending;
        }
        finally
        {
            foreach (var window in owner.OwnedWindows.ToArray()) window.Close();
            owner.Close();
            localization.Apply(previousLanguage);
        }
    }
}
