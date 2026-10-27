using System.Linq;
using System.IO;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Integration.Tests.Views;

public class SSTabDrawingTests : WindowTestFixture
{
    private Window? hostWindow;
    private void Arrange(VBSSTab tab)
    {
        if (hostWindow is null)
        {
            hostWindow = new Window { Content = tab, Width = 360, Height = 240 };
            hostWindow.Show();
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBlock[] Captions(VBSSTab tab) => tab.GetVisualDescendants().OfType<TextBlock>().ToArray();
    private static Border Header(TextBlock caption) => (Border)caption.Parent!;

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActiveTabAndFocusRectangleChangeTheRenderedFrame(bool focusRectangle)
    {
        var tab = new VBSSTab(new Canvas());
        var window = new Window { Content = tab, Width = 360, Height = 240 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            if (focusRectangle)
            {
                tab.Focus();
                Dispatcher.UIThread.RunJobs();
                tab.IsFocused.Should().BeTrue();
            }
            using var before = window.CaptureRenderedFrame();
            before.Should().NotBeNull();
            using var beforeBytes = new MemoryStream();
            before!.Save(beforeBytes);
            if (focusRectangle) tab.ShowFocusRect = false;
            else tab.Tab = 1;
            Dispatcher.UIThread.RunJobs();
            using var after = window.CaptureRenderedFrame();
            after.Should().NotBeNull();
            using var afterBytes = new MemoryStream();
            after!.Save(afterBytes);
            afterBytes.ToArray().Should().NotEqual(beforeBytes.ToArray());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DefaultStripSharesWidthAndSwitchingUpdatesTheBoldCaption()
    {
        var tab = new VBSSTab(new Canvas());
        Arrange(tab);
        var captions = Captions(tab);
        captions.Select(c => c.Text).Should().Equal("Tab 0", "Tab 1", "Tab 2");
        captions.Select(c => Header(c).Bounds.Width).Distinct().Should().ContainSingle();
        captions[0].FontWeight.Should().Be(FontWeight.Bold);
        captions[0].Foreground.Should().BeAssignableTo<ISolidColorBrush>().Which.Color.Should().Be(Colors.Black);
        captions[1].FontWeight.Should().Be(FontWeight.Normal);
        tab.Tab = 1;
        Arrange(tab);
        Captions(tab).Select(c => c.FontWeight).Should().Equal(FontWeight.Normal, FontWeight.Bold, FontWeight.Normal);
    }

    [AvaloniaFact]
    public void PropertyPageStyleSizesCaptionsAndDoesNotBoldTheActiveTab()
    {
        var tab = new VBSSTab(new Canvas()) { VbStyle = 1 };
        tab.Caption(0, "Short");
        tab.Caption(1, "A much longer caption");
        Arrange(tab);
        var captions = Captions(tab);
        captions.Should().HaveCount(3);
        captions.Should().OnlyContain(c => c.FontWeight == FontWeight.Normal);
        Header(captions[1]).Bounds.Width.Should().BeGreaterThan(Header(captions[0]).Bounds.Width);
        Header(captions[1]).Bounds.Left.Should().Be(Header(captions[0]).Bounds.Right);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void SelectingABackRowBringsItNextToThePage(int style)
    {
        var tab = new VBSSTab(new Canvas()) { Tabs = 5, VbStyle = style };
        Arrange(tab);
        var captions = Captions(tab);
        captions.Should().HaveCount(5);
        Header(captions[0]).Bounds.Top.Should().BeGreaterThan(Header(captions[3]).Bounds.Top);
        tab.Tab = 4;
        Arrange(tab);
        captions = Captions(tab);
        Header(captions[3]).Bounds.Top.Should().BeGreaterThan(Header(captions[0]).Bounds.Top);
        Header(captions[0]).Bounds.Left.Should().BeGreaterThan(Header(captions[3]).Bounds.Left);
    }

    [AvaloniaFact]
    public void HiddenTabsKeepTheirShareAndMaxWidthCapsStandardTabs()
    {
        var tab = new VBSSTab(new Canvas());
        tab.TabVisible(1, false);
        Arrange(tab);
        var captions = Captions(tab);
        captions.Select(c => c.Text).Should().Equal("Tab 0", "Tab 2");
        Header(captions[1]).Bounds.Width.Should().Be(120);
        Header(captions[1]).Bounds.Right.Should().Be(240);
        tab.TabMaxWidth = 900d / 15;
        Arrange(tab);
        Captions(tab).Should().OnlyContain(c => Header(c).Bounds.Width == 60);
    }

    [AvaloniaTheory]
    [InlineData(2)]
    [InlineData(3)]
    public void SideCaptionsStayHorizontalAndClipToTheStrip(int side)
    {
        var tab = new VBSSTab(new Canvas()) { TabOrientation = side };
        Arrange(tab);
        var captions = Captions(tab);
        captions.Should().HaveCount(3);
        foreach (var caption in captions)
        {
            caption.RenderTransform.Should().BeNull();
            Header(caption).ClipToBounds.Should().BeTrue();
            Header(caption).Bounds.Width.Should().Be(System.Math.Round(tab.TabHeight));
        }
        var x = Header(captions[0]).Bounds.X;
        if (side == 2) x.Should().Be(0);
        else x.Should().BeGreaterThan(300);
    }

    [AvaloniaFact]
    public void CaptionsWrapWithinTheTabHeight()
    {
        var tab = new VBSSTab(new Canvas()) { TabMaxWidth = 60, TabHeight = 40 };
        tab.Caption(0, "A long caption that wraps");
        Arrange(tab);
        var caption = Captions(tab)[0];
        caption.TextLayout.TextLines.Count.Should().BeGreaterThan(1);
        Header(caption).ClipToBounds.Should().BeTrue();
    }

    [AvaloniaFact]
    public void HeightAndDisabledStateUpdateCaptionsWithoutMovingChildren()
    {
        var host = new Canvas();
        var child = new Control { Width = 80, Height = 20 };
        host.Children.Add(child);
        Canvas.SetTop(child, 40);
        var tab = new VBSSTab(host);
        tab.TabEnabled(1, false);
        tab.TabHeight = 40;
        Arrange(tab);
        var captions = Captions(tab);
        captions.Should().HaveCount(3);
        captions.Should().OnlyContain(c => Header(c).Bounds.Height == 40);
        captions[1].IsEffectivelyEnabled.Should().BeFalse();
        var disabledBrush = captions[1].Foreground;
        tab.IsEnabled = false;
        Arrange(tab);
        Captions(tab).Should().OnlyContain(c => !c.IsEffectivelyEnabled);
        Captions(tab).Select(c => c.Foreground).Should().OnlyContain(brush => brush!.Equals(disabledBrush));
        Canvas.GetTop(child).Should().Be(40);
        host.Bounds.Position.Should().Be(default(Point));
    }
}
