using System.Linq;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Forms.Views;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The Find dialog's boxes are named by their captions (#2717). Unnamed, a screen reader announced two edit boxes it
/// could not tell apart, and automation addressed them only by position.
/// </summary>
public class FindReplaceAccessibleNameTests
{
    [AvaloniaTheory]
    [InlineData("Fi_nd What:", "Re_place With:", "Search:", "Find What:", "Replace With:")]
    [InlineData("Suchen _nach:", "Ersetzen _durch:", "Suchen:", "Suchen nach:", "Ersetzen durch:")]
    [InlineData("検索する文字列:", "置換後の文字列:", "検索範囲:", "検索する文字列:", "置換後の文字列:")]
    public void Each_box_is_named_by_its_caption_without_the_access_key_marker(
        string findWhat, string replaceWith, string search, string findName, string replaceName)
    {
        var view = new FindReplaceView();
        view.Resources["Str.Dialog.FindReplace.Label.FindWhat"] = findWhat;
        view.Resources["Str.Dialog.FindReplace.Label.ReplaceWith"] = replaceWith;
        view.Resources["Str.Dialog.FindReplace.Label.Search"] = search;
        var window = new Window { Content = view, Width = 480, Height = 360 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            // The view's own boxes: a ComboBox's template holds one too, for editing, and that one is the ComboBox's.
            var boxes = view.GetVisualDescendants().OfType<TextBox>().Where(b => b.TemplatedParent is null).ToArray();
            boxes.Select(b => ControlAutomationPeer.CreatePeerForElement(b).GetName())
                .Should().Equal(findName, replaceName);
            ControlAutomationPeer.CreatePeerForElement(view.GetVisualDescendants().OfType<ComboBox>().Single()).GetName()
                .Should().Be(search);

            // And a caller reaches the Find What box by that name, not by its position.
            var tree = UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: true);
            var edit = UiAutomationDriver.Find(tree, findName, 10).Matches.Single(m => m.ControlType == "Edit");
            edit.Path.Should().EndWith($"Edit[{findName}]");
            UiAutomationDriver.Resolve(window, edit.Path).control.Should().BeSameAs(boxes[0]);
        }
        finally { window.Close(); }
    }
}
