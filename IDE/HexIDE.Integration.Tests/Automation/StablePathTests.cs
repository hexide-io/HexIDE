using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using HexIDE.Automation;
using HexIDE.Forms.Views;

namespace HexIDE.Integration.Tests.Automation;

public class StablePathTests
{
    private static IEnumerable<UiNode> Nodes(UiNode node) => node.Children.SelectMany(Nodes).Prepend(node);
    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 640, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Text_area_path_survives_overflow(bool named)
    {
        var editor = new TextEditor { Name = named ? "Editor" : null, Text = "short" };
        var window = Show(editor);
        try
        {
            UiAutomationDriver.Inspect(editor, "Window").Providers.Should().NotContain("scroll");
            var before = Nodes(UiAutomationDriver.Dump(window, "Window", 60, false))
                .Single(n => n.ClassName == "TextArea").Path;
            editor.Text = string.Join(Environment.NewLine, Enumerable.Repeat(new string('x', 300), 100));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            UiAutomationDriver.Inspect(editor, "Window").Providers.Should().Contain("scroll");
            var after = Nodes(UiAutomationDriver.Dump(window, "Window", 60, false))
                .Single(n => n.ClassName == "TextArea").Path;
            after.Should().Be(before);
            UiAutomationDriver.Resolve(window, before).control.Should().BeSameAs(editor.TextArea);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Missed_segment_names_the_wrapper_and_the_complete_corrected_path()
    {
        var button = new Button { Name = "Go", Content = "Go" };
        var wrapper = new Border { Name = "Editor", Child = button };
        var window = Show(wrapper);
        try
        {
            var saved = Nodes(UiAutomationDriver.Dump(window, "Window", 60, false)).Single(n => n.Name == "Go").Path;
            saved.Should().Be("Window/Button[Go]");
            AutomationProperties.SetAutomationId(wrapper, "Editor");
            var (control, error) = UiAutomationDriver.Resolve(window, saved);
            control.Should().BeNull();
            error.Should().Be("'Window' now holds 'None[Editor]'; the path continues under it: 'Window/None[Editor]/Button[Go]'");
            UiAutomationDriver.Resolve(window, "Window/None[Editor]/Button[Go]").control.Should().BeSameAs(button);
        }
        finally { window.Close(); }
    }

    private sealed class RangeWrapper : Decorator
    {
        public double Maximum { get; set; }
        protected override AutomationPeer OnCreateAutomationPeer() => new RangePeer(this);
    }

    private sealed class RangePeer(RangeWrapper owner) : ControlAutomationPeer(owner), IRangeValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.None;
        public bool IsReadOnly => false;
        public double LargeChange => 1;
        public double SmallChange => 1;
        public double Minimum => 0;
        public double Maximum => ((RangeWrapper)Owner).Maximum;
        public double Value => 0;
        public void SetValue(double value) { }
    }

    [AvaloniaFact]
    public void Range_capability_keeps_a_wrapper_even_when_its_range_is_not_currently_usable()
    {
        var range = new RangeWrapper { Name = "Range", Child = new Button { Name = "Go" } };
        var window = Show(range);
        try
        {
            var before = Nodes(UiAutomationDriver.Dump(window, "Window", 60, false)).Single(n => n.Name == "Go").Path;
            UiAutomationDriver.Inspect(range, "Window").Providers.Should().NotContain("rangeValue");
            range.Maximum = 100;
            UiAutomationDriver.Inspect(range, "Window").Providers.Should().Contain("rangeValue");
            Nodes(UiAutomationDriver.Dump(window, "Window", 60, false)).Single(n => n.Name == "Go").Path.Should().Be(before);
            before.Should().Be("Window/None[Range]/Button[Go]");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Wrapper_hint_preserves_remaining_segments_and_does_not_guess_between_wrappers()
    {
        var wrapper = new Border { Child = new UserControl { Name = "Body", Content = new Button { Name = "Go" } } };
        AutomationProperties.SetAutomationId(wrapper, "Editor");
        var window = Show(wrapper);
        try
        {
            const string corrected = "Window/None[Editor]/Custom[Body]/Button[Go]";
            UiAutomationDriver.Resolve(window, "Window/Custom[Body]/Button[Go]").error.Should()
                .Be($"'Window' now holds 'None[Editor]'; the path continues under it: '{corrected}'");
            UiAutomationDriver.Resolve(window, corrected).error.Should().BeNull();
            UiAutomationDriver.Resolve(window, "Window/Custom[Body]/Button[Missing]").error.Should()
                .Be("no child matching 'Custom[Body]' under 'Window'");

            var other = new Border { Child = new UserControl { Name = "Body", Content = new Button { Name = "Go" } } };
            AutomationProperties.SetAutomationId(other, "Other");
            window.Content = null;
            window.Content = new StackPanel { Children = { wrapper, other } };
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            UiAutomationDriver.Resolve(window, "Window/Custom[Body]/Button[Go]").error.Should()
                .Be("no child matching 'Custom[Body]' under 'Window'");
        }
        finally { window.Close(); }
    }

}
