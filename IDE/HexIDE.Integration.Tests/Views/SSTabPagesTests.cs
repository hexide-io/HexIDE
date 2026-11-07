using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class SSTabPagesTests
{
    private static Control Child(Canvas host, double left, double top = 0, double width = 101, double height = 26)
    {
        var child = new Control { Width = width, Height = height };
        Canvas.SetLeft(child, left);
        Canvas.SetTop(child, top);
        KeyboardNavigation.SetIsTabStop(child, true);
        host.Children.Add(child);
        return child;
    }

    [AvaloniaFact]
    public void SwitchingMovesChildrenBeforeNotificationAndRestoresTabStops()
    {
        var host = new Canvas();
        var command = Child(host, -4978);
        var text = Child(host, 32);
        var check = Child(host, -4958);
        var pages = new SSTabPages(host, 1, [[command], [text], [check]]);
        KeyboardNavigation.GetIsTabStop(command).Should().BeFalse();
        KeyboardNavigation.GetIsTabStop(text).Should().BeTrue();
        KeyboardNavigation.GetIsTabStop(check).Should().BeFalse();
        var calls = 0;
        pages.Changed += previous =>
        {
            previous.Should().Be(1);
            pages.Tab.Should().Be(0);
            Canvas.GetLeft(command).Should().Be(22);
            Canvas.GetLeft(text).Should().Be(-4968);
            KeyboardNavigation.GetIsTabStop(command).Should().BeTrue();
            KeyboardNavigation.GetIsTabStop(text).Should().BeFalse();
            calls++;
        };
        pages.Select(0);
        pages.Select(0);
        calls.Should().Be(1);
        text.IsVisible.Should().BeTrue();
        text.IsEnabled.Should().BeTrue();
    }

    [AvaloniaFact]
    public void HiddenControlMovedOnPageBelongsToBothPagesAndRecordedControlsMoveExactly()
    {
        var host = new Canvas();
        var command = Child(host, -4978);
        var pages = new SSTabPages(host, 1, [[command], [], []]);
        Canvas.SetLeft(command, 100d / 15);
        pages.Select(2);
        Canvas.GetLeft(command).Should().BeApproximately(-74900d / 15, 1e-9);
        pages.Select(0);
        Canvas.GetLeft(command).Should().BeApproximately(100d / 15, 1e-9);
        pages.Select(1);
        Canvas.GetLeft(command).Should().BeApproximately(100d / 15, 1e-9);
        Canvas.SetLeft(command, -74000d / 15);
        pages.Select(0);
        Canvas.GetLeft(command).Should().BeApproximately(1000d / 15, 1e-9);
    }

    [AvaloniaTheory]
    [InlineData(-101, 0, true)]
    [InlineData(-102, 0, false)]
    [InlineData(0, -26, true)]
    [InlineData(0, -27, false)]
    [InlineData(200000, 0, true)]
    public void CaptureUsesRightAndBottomEdgesWithoutFarBounds(double left, double top, bool moved)
    {
        var host = new Canvas();
        var child = Child(host, left, top);
        child.IsVisible = false;
        child.IsEnabled = false;
        var pages = new SSTabPages(host, 1, [[], [child], []]);
        pages.Select(2);
        pages.Tab.Should().Be(2);
        Canvas.GetLeft(child).Should().Be(left - (moved ? 5000 : 0));
        if (left < 5000)
        {
            pages.Select(1);
            Canvas.GetLeft(child).Should().Be(left);
        }
        child.IsVisible.Should().BeFalse();
        child.IsEnabled.Should().BeFalse();
    }

    [AvaloniaFact]
    public void OffPageChildIsLostAndNewlyContainedChildIsCaptured()
    {
        var host = new Canvas();
        var lost = Child(host, -74000d / 15);
        var pages = new SSTabPages(host, 1, [[], [lost], []]);
        var added = Child(host, 32);
        KeyboardNavigation.SetIsTabStop(added, false);
        pages.Select(0);
        Canvas.GetLeft(added).Should().Be(-4968);
        pages.Select(1);
        Canvas.GetLeft(lost).Should().Be(-74000d / 15);
        Canvas.GetLeft(added).Should().Be(32);
        KeyboardNavigation.GetIsTabStop(added).Should().BeFalse();
    }

    [AvaloniaFact]
    public void RefusedReductionStillSelectsZeroAndKeepsDetachedRecordedChildren()
    {
        var host = new Canvas();
        var child = Child(host, 42);
        var pages = new SSTabPages(host, 2, [[], [], [child]]);
        var calls = 0;
        pages.Changed += previous => { if (calls == 0) previous.Should().Be(2); calls++; };
        Action reduce = () => pages.Resize(2);
        reduce.Should().Throw<VBRunTimeException>().Which.Error.Should().Be(VBStandardError.InvalidPropertyValue);
        pages.Tab.Should().Be(0);
        pages.Tabs.Should().Be(3);
        calls.Should().Be(1);
        host.Children.Remove(child);
        reduce.Should().Throw<VBRunTimeException>().Which.Error.Should().Be(VBStandardError.InvalidPropertyValue);
        pages.Select(2);
        pages.Select(0);
        pages.Resize(2);
        pages.Tabs.Should().Be(2);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonpositiveCountWithRecordedTabZeroRaises380(int count)
    {
        var host = new Canvas();
        var child = Child(host, -4978);
        var pages = new SSTabPages(host, 1, [[child], [], []]);
        Action resize = () => pages.Resize(count);
        resize.Should().Throw<VBRunTimeException>().Which.Error.Should().Be(VBStandardError.InvalidPropertyValue);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UnmeasuredEmptyTabCountIsExplicitlyRefused(int count)
    {
        var pages = new SSTabPages(new Canvas(), 0, [[], [], []]);
        Action resize = () => pages.Resize(count);
        resize.Should().Throw<NotImplementedException>();
    }

    [AvaloniaFact]
    public void RemovingEmptyShowingPageSelectsZeroAndGrowthPreservesPages()
    {
        var host = new Canvas();
        var pages = new SSTabPages(host, 2, [[], [], []]);
        pages.Resize(2);
        pages.Tab.Should().Be(0);
        pages.Resize(99);
        pages.Tabs.Should().Be(99);
        Action tooMany = () => pages.Resize(100);
        tooMany.Should().Throw<VBRunTimeException>().Which.Error.Should().Be(VBStandardError.InvalidPropertyValue);
        Action badTab = () => pages.Select(-1);
        badTab.Should().Throw<VBRunTimeException>().Which.Error.Should().Be(VBStandardError.InvalidPropertyValue);
    }
}
