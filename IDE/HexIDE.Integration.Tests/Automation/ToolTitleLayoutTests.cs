using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HexIDE.Integration.Tests.Automation;

public class ToolTitleLayoutTests
{
    [AvaloniaTheory]
    [InlineData(1, 180)]
    [InlineData(1.5, 180)]
    [InlineData(1.5, 260)]
    public void Long_tool_title_leaves_its_buttons_inside_the_pane(double scaling, double width)
    {
        var layout = DragDockTests.Shown();
        try
        {
            layout.Window.SetRenderScaling(scaling);
            layout.Window.Width = width * 2;
            var title = "Project Group - " + new string('W', 60);
            layout.Beta.Title = title;
            Dispatcher.UIThread.RunJobs();
            layout.Window.UpdateLayout();
            var chrome = DragDockTests.ChromeOf(layout, layout.Right);
            var text = chrome.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Title");
            text.Text.Should().Be(title);
            var buttons = chrome.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible).ToArray();
            buttons.Should().Contain(b => b.Name == "PART_CloseButton");
            buttons.Should().Contain(b => b.Name == "PART_PinButton");
            foreach (var button in buttons.OrderBy(b => b.Name))
            {
                var origin = button.TranslatePoint(default, chrome)!.Value;
                origin.X.Should().BeGreaterThanOrEqualTo(0);
                (origin.X + button.Bounds.Width).Should().BeLessThanOrEqualTo(chrome.Bounds.Width,
                    button.Name + " must stay inside the pane");
            }
            text.TextTrimming.Should().Be(TextTrimming.CharacterEllipsis);
            var natural = new TextBlock { Text = title, FontFamily = text.FontFamily, FontSize = text.FontSize };
            natural.Measure(Size.Infinity);
            natural.DesiredSize.Width.Should().BeGreaterThan(text.Bounds.Width);
            ToolTip.GetTip(text).Should().Be(title);
            ControlAutomationPeer.CreatePeerForElement(chrome).GetName().Should().Be(title);
        }
        finally { layout.Window.Close(); }
    }
}
