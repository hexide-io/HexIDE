using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Localization;
using MessageBox = HexIDE.Runtime.Dialogs.MessageBox;

namespace HexIDE.Integration.Tests.Views;

public class MessageBoxLocalizationTests
{
    [AvaloniaTheory]
    [InlineData(MessageBoxButtons.Ok, "D’accord", MessageBoxResult.Ok)]
    [InlineData(MessageBoxButtons.OkCancel, "D’accord|Annuler", MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButtons.AbortRetryIgnore, "Abandonner|Réessayer|Ignorer", MessageBoxResult.Ignore)]
    [InlineData(MessageBoxButtons.YesNoCancel, "Oui|Non|Annuler", MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButtons.YesNo, "Oui|Non", MessageBoxResult.No)]
    [InlineData(MessageBoxButtons.RetryCancel, "Réessayer|Annuler", MessageBoxResult.Cancel)]
    public void IDE_boxes_use_the_active_pack_and_keep_their_results(
        MessageBoxButtons buttons, string captions, MessageBoxResult expected)
    {
        var app = Application.Current!;
        var localization = new LocalizationService();
        localization.Apply("fr");
        // Make OK distinguishable too, as a user translation can.
        app.Resources["Str.Dialog.MessageBox.Button.Ok"] = "D’accord";
        var owner = new Window();
        owner.Show();
        var box = new MessageBox { Text = "probe", Buttons = buttons };
        var dialog = new Window { Content = box };
        try
        {
            var answer = box.ShowAndWait(dialog, owner);
            Dispatcher.UIThread.RunJobs();
            var controls = dialog.GetVisualDescendants().OfType<Button>().ToArray();
            controls.Select(b => b.Content).Should().Equal(captions.Split('|'));
            controls.Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            answer.IsCompleted.Should().BeTrue();
            answer.Result.Should().Be(expected);
        }
        finally
        {
            dialog.Close();
            owner.Close();
            app.Resources.Remove("Str.Dialog.MessageBox.Button.Ok");
            localization.Apply("en");
        }
    }
}
