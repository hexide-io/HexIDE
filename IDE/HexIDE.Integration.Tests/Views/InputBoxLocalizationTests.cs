using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Localization;
using HexIDE.Runtime.Dialogs;

namespace HexIDE.Integration.Tests.Views;

public class InputBoxLocalizationTests
{
    [AvaloniaTheory]
    [InlineData(0, "Ok")]
    [InlineData(1, "Cancel")]
    public void Buttons_use_the_active_pack(int index, string button)
    {
        var localization = new LocalizationService();
        localization.Apply("pseudo");
        var dialog = new Window { Content = new InputBox() };
        try
        {
            dialog.Show();
            Dispatcher.UIThread.RunJobs();
            var caption = localization.GetString($"Str.Dialog.MessageBox.Button.{button}");
            caption.Should().StartWith("⟦");
            dialog.GetVisualDescendants().OfType<Button>().ElementAt(index)
                .Content.Should().Be(caption);
        }
        finally
        {
            dialog.Close();
            localization.Apply("en");
        }
    }
}
