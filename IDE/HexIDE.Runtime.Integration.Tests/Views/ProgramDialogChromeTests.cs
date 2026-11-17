using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using MessageBox = HexIDE.Runtime.Dialogs.MessageBox;

namespace HexIDE.Runtime.Integration.Tests.Views;

public class ProgramDialogChromeTests
{
    // Caption requirements and measured Windows bits: hexide-io/HexIDE#4099.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Program_boxes_disable_caption_buttons(bool input)
    {
        var owner = new Window();
        owner.Show();
        var library = new StandaloneStandardLib(owner, HexIDE.IDE.Distribution.DisplayName);
        Task answer = input ? library.InputBox("probe", "caption", "")
            : library.MsgBox("probe", "caption", MessageBoxButtons.Ok, MessageBoxIcon.None);
        var window = owner.OwnedWindows.Single();
        try
        {
            window.CanMinimize.Should().BeFalse();
            window.CanMaximize.Should().BeFalse();
        }
        finally
        {
            window.Close();
            owner.Close();
        }
        await answer;
    }
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Caption_policy_follows_the_program_box_when_its_owner_is_hidden(bool visible)
    {
        var owner = new Window();
        if (visible)
            owner.Show();
        var box = new MessageBox();
        var original = new Window { Content = box };
        var answer = box.ShowAndWait(original, owner);
        var shown = (Window)TopLevel.GetTopLevel(box)!;
        try
        {
            shown.CanMinimize.Should().BeFalse();
            shown.CanMaximize.Should().BeFalse();
        }
        finally
        {
            shown.Close();
            owner.Close();
        }
        await answer;
    }
    [AvaloniaFact]
    public void An_embedded_program_input_box_does_not_change_its_hosts_caption()
    {
        var input = new HexIDE.Runtime.Dialogs.InputBox();
        var window = new Window { Content = new Border { Child = input } };
        try
        {
            window.Show();
            window.CanMinimize.Should().BeTrue();
            window.CanMaximize.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }
}
