using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

// Real-input measurements: #1088, issuecomment-6428845889.
public class ToolbarEventTests
{
    private sealed class RecordingRoot : Decorator, IModuleExecutionRoot
    {
        public readonly List<(string Name, IReadOnlyList<Vb6Value>? Args)> Calls = [];
        public readonly List<long> ValuesAtButtonClick = [];
        public bool HasButtonHandler = true;
        public bool Handles(string name) => HasButtonHandler || name != "Toolbar1_ButtonClick";
        public void ExecuteSubIf(string name, IReadOnlyList<Vb6Value>? args, Func<bool> when)
        {
            if (Handles(name) && when()) ExecuteSub(name, args);
        }
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
        {
            Calls.Add((name, args));
            if (name == "Toolbar1_ButtonClick")
            {
                ((ToolbarItem)args![0].Value!).TryGetProperty("Value", out var value);
                ValuesAtButtonClick.Add((long)value.Value!);
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public async Task Face_clicks_follow_the_measured_order(int style, bool buttonClick)
    {
        var bar = new VBToolbar();
        VBProps.SetName(bar, "Toolbar1");
        var item = (ToolbarItem)(await bar.Buttons.CallAsync("Add", [Vb6Value.Missing, new("a"), new("Alpha"), new(style)]))!.Value.Value!;
        if (style == 4) item.TrySetProperty("Width", new(1500));
        var root = new RecordingRoot { Child = bar, HasButtonHandler = buttonClick };
        var window = new Window { Content = root, Width = 400, Height = 100 };
        ContainerRuntimeHarness.MergeAppResources(window);
        try
        {
            window.Show();
            window.UpdateLayout();
            var visual = bar.GetVisualDescendants().OfType<Canvas>().First().Children[0];
            var p = visual.TranslatePoint(new Point(2, visual.Bounds.Height / 2), window)!.Value;
            window.MouseDown(p, MouseButton.Left);
            window.MouseUp(p, MouseButton.Left);
            window.UpdateLayout();
            root.Calls.Select(c => c.Name).Should().Equal(buttonClick
            ? ["Toolbar1_MouseDown", "Toolbar1_MouseUp", "Toolbar1_Click", "Toolbar1_ButtonClick"]
            : ["Toolbar1_MouseDown", "Toolbar1_MouseUp", "Toolbar1_Click"]);
            root.Calls[0].Args![0].Value.Should().Be(1);
            var local = window.TranslatePoint(p, bar)!.Value;
            Convert.ToDouble(root.Calls[0].Args![2].Value).Should().BeApproximately(local.X * 15, 0.001);
            Convert.ToDouble(root.Calls[0].Args![3].Value).Should().BeApproximately(local.Y * 15, 0.001);
            if (buttonClick) root.Calls.Last().Args![0].Value.Should().BeSameAs(item);
            if (style is 1 or 2)
            {
                item.TryGetProperty("Value", out var value);
                value.Value.Should().Be(1L);
                root.ValuesAtButtonClick.Should().Equal(buttonClick ? [1L] : []);
            }
            root.Calls.Clear();
            window.MouseDown(p, MouseButton.Right);
            window.MouseUp(p, MouseButton.Right);
            window.UpdateLayout();
            root.Calls.Select(c => c.Name).Should().Equal("Toolbar1_MouseDown", "Toolbar1_MouseUp", "Toolbar1_Click");
            root.Calls[0].Args![0].Value.Should().Be(2);
            root.Calls.Clear();
            window.MouseDown(p, MouseButton.Left);
            window.MouseUp(p, MouseButton.Left);
            window.UpdateLayout();
            root.Calls.Select(c => c.Name).Should().Equal(buttonClick && style != 2
            ? ["Toolbar1_MouseDown", "Toolbar1_MouseUp", "Toolbar1_Click", "Toolbar1_ButtonClick"]
            : ["Toolbar1_MouseDown", "Toolbar1_MouseUp", "Toolbar1_Click"]);
            if (style == 1)
            {
                item.TryGetProperty("Value", out var value);
                value.Value.Should().Be(0L);
                root.ValuesAtButtonClick.Should().Equal(buttonClick ? [1L, 0L] : []);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Double_click_has_only_ButtonClick_on_the_second_press()
    {
        var bar = new VBToolbar();
        VBProps.SetName(bar, "Toolbar1");
        await bar.Buttons.CallAsync("Add", [Vb6Value.Missing, new("a"), new("Alpha")]);
        var root = new RecordingRoot { Child = bar };
        var window = new Window { Content = root, Width = 400, Height = 100 };
        ContainerRuntimeHarness.MergeAppResources(window);
        try
        {
            window.Show();
            window.UpdateLayout();
            var p = bar.TranslatePoint(new Point(5, 5), window)!.Value;
            for (var i = 0; i < 2; i++)
            {
                window.MouseDown(p, MouseButton.Left);
                window.MouseUp(p, MouseButton.Left);
                window.UpdateLayout();
            }
            root.Calls.Select(c => c.Name).Should().Equal("Toolbar1_MouseDown", "Toolbar1_MouseUp", "Toolbar1_Click", "Toolbar1_ButtonClick", "Toolbar1_ButtonClick");
            root.Calls.Clear();
            var empty = bar.TranslatePoint(new Point(350, 5), window)!.Value;
            window.MouseDown(empty, MouseButton.Right);
            window.MouseUp(empty, MouseButton.Right);
            root.Calls.Select(c => c.Name).Should().Equal("Toolbar1_MouseDown", "Toolbar1_MouseUp", "Toolbar1_Click");
            var item = (ToolbarItem)bar.Buttons.Item([new(1)]).Value!;
            item.TrySetProperty("Enabled", new(false));
            root.Calls.Clear();
            window.MouseDown(p, MouseButton.Left);
            window.MouseUp(p, MouseButton.Left);
            window.UpdateLayout();
            root.Calls.Select(c => c.Name).Should().Equal("Toolbar1_MouseDown", "Toolbar1_MouseUp", "Toolbar1_Click");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("pointer")]
    [InlineData("keyboard")]
    [InlineData("dismiss")]
    public async Task Dropdown_arrow_and_menu_dispatch_only_their_measured_events(string action)
    {
        var bar = new VBToolbar();
        VBProps.SetName(bar, "Toolbar1");
        var item = (ToolbarItem)(await bar.Buttons.CallAsync("Add", [Vb6Value.Missing, new("a"), new("Alpha"), new(5)]))!.Value.Value!;
        item.TryGetProperty("ButtonMenus", out var menus);
        var collection = (ToolbarItems)menus.Value!;
        var menu = (ToolbarItem)(await collection.CallAsync("Add", [Vb6Value.Missing, new("m"), new("First")]))!.Value.Value!;
        var hidden = (ToolbarItem)(await collection.CallAsync("Add", [Vb6Value.Missing, new("h"), new("Hidden")]))!.Value.Value!;
        hidden.TrySetProperty("Visible", new(false));
        var root = new RecordingRoot { Child = bar };
        var window = new Window { Content = root, Width = 400, Height = 100 };
        ContainerRuntimeHarness.MergeAppResources(window);
        try
        {
            window.Show();
            window.UpdateLayout();
            var arrow = bar.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "▾");
            var p = arrow.TranslatePoint(new Point(arrow.Bounds.Width / 2, arrow.Bounds.Height / 2), window)!.Value;
            window.MouseDown(p, MouseButton.Left);
            window.MouseUp(p, MouseButton.Left);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            root.Calls.Select(c => c.Name).Should().Equal("Toolbar1_MouseDown", "Toolbar1_ButtonDropDown");
            root.Calls.Last().Args![0].Value.Should().BeSameAs(item);
            var flyout = bar.GetVisualDescendants().OfType<Control>().Select(FlyoutBase.GetAttachedFlyout).OfType<MenuFlyout>().Single();
            flyout.Items.Should().ContainSingle();
            var entry = (MenuItem)flyout.Items[0]!;
            var popup = TopLevel.GetTopLevel(entry)!;
            var centre = entry.TranslatePoint(new Point(entry.Bounds.Width / 2, entry.Bounds.Height / 2), popup)!.Value;
            root.Calls.Clear();
            if (action == "dismiss")
            {
                popup.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                popup.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                root.Calls.Should().BeEmpty();
                return;
            }
            if (action == "keyboard")
            {
                popup.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                popup.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                popup.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                popup.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }
            else
            {
                popup.MouseDown(centre, MouseButton.Left);
                popup.MouseUp(centre, MouseButton.Left);
            }
            root.Calls.Select(c => c.Name).Should().Equal("Toolbar1_ButtonMenuClick");
            root.Calls.Single().Args![0].Value.Should().BeSameAs(menu);
            menu.TryGetProperty("Parent", out var parent);
            parent.Value.Should().BeSameAs(item);
        }
        finally { window.Close(); }
    }
}
