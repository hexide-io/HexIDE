using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Views;

public class DialogWindowChromeTests
{
    // Dialog frame behaviour recorded in #3341; program dialogs are outside its scope.
    [AvaloniaTheory]
    [InlineData("Dialog", true)]
    [InlineData("Dialog", false)]
    [InlineData("Message", true)]
    [InlineData("Message", false)]
    [InlineData("Input", true)]
    [InlineData("Input", false)]
    [InlineData("Server", true)]
    [InlineData("Server", false)]
    public async Task IdeDialogDisablesMinimizeAndTaskbarEntry(string kind, bool checkMinimize)
    {
        var owner = new Window();
        owner.Show();
        try
        {
            Task? pending = null;
            Window window;
            if (kind == "Server")
            {
                pending = ServerMessagePrompt.Show(owner, "Server", new(3, "Message", []), CancellationToken.None);
                window = owner.OwnedWindows.Single();
            }
            else
            {
                window = kind == "Dialog" ? new DialogWindow()
                    : new WindowManager(Substitute.For<ILocalizationService>()).MakeDialogWindow("Title",
                        kind == "Message" ? new HexIDE.Runtime.Dialogs.MessageBox() : new HexIDE.Runtime.Dialogs.InputBox());
                pending = window.ShowDialog(owner);
            }
            try
            {
                if (checkMinimize) window.CanMinimize.Should().BeFalse();
                else window.ShowInTaskbar.Should().BeFalse();
            }
            finally { window.Close(); }
            await pending;
        }
        finally { owner.Close(); }
    }
}
