using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Integration.Tests.Views;

public class ToolbarDrawingTests
{
    [AvaloniaFact]
    public async Task All_styles_have_distinct_visuals_and_follow_model_changes()
    {
        var bar = new VBToolbar();
        for (var style = 0; style <= 5; style++)
            await bar.Buttons.CallAsync("Add", [Vb6Value.Missing, new Vb6Value("k" + style), new Vb6Value("Alpha"), new Vb6Value(style)]);
        var window = new Window { Content = bar };
        window.Show();
        window.UpdateLayout();
        var panel = bar.GetVisualDescendants().OfType<Canvas>().First();
        panel.Children.Should().HaveCount(6);
        panel.Children[0].Should().BeOfType<Button>();
        panel.Children[1].Should().BeOfType<ToggleButton>();
        panel.Children[2].Should().BeOfType<ToggleButton>();
        panel.Children[3].Should().BeOfType<Border>();
        panel.Children[4].Should().BeOfType<Border>();
        ((Border)panel.Children[4]).Width.Should().Be(0);
        panel.Children[5].GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain("▾");
        var check = (ToolbarItem)bar.Buttons.Item([new Vb6Value(2)]).Value!;
        check.TrySetProperty("Value", new Vb6Value(1));
        panel.Children.OfType<ToggleButton>().First().IsChecked.Should().BeTrue();
        check.TrySetProperty("Enabled", new Vb6Value(false));
        panel.Children[1].IsEnabled.Should().BeFalse();
        check.TrySetProperty("Caption", new Vb6Value("Changed"));
        ((ToggleButton)panel.Children[1]).Content.Should().Be("Changed");
        check.TrySetProperty("Visible", new Vb6Value(false));
        panel.Children.Should().HaveCount(5);
        var placeholder = (ToolbarItem)bar.Buttons.Item([new Vb6Value(5)]).Value!;
        placeholder.TrySetProperty("Width", new Vb6Value(1500));
        ((Border)panel.Children[3]).Width.Should().BeGreaterThan(0);
        bar.TrySetProperty("Style", new Vb6Value(1));
        panel.Children.OfType<Button>().Should().OnlyContain(b => b.Classes.Contains("flat"));
        var normal = (ToolbarItem)bar.Buttons.Item([new Vb6Value(1)]).Value!;
        normal.TrySetProperty("ToolTipText", new Vb6Value("Tip"));
        ToolTip.GetTip(panel.Children[0]).Should().Be("Tip");
        bar.TrySetProperty("ShowTips", new Vb6Value(false));
        ToolTip.GetTip(panel.Children[0]).Should().BeNull();
        normal.TrySetProperty("Value", new Vb6Value(1));
        ((ToggleButton)panel.Children[0]).IsChecked.Should().BeTrue();
        await bar.Buttons.CallAsync("Clear", []);
        panel.Children.Should().BeEmpty();
        window.Close();
    }
}
