using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class ListViewMouseTests
{
    private sealed class Recorder : Decorator, IModuleExecutionRoot
    {
        public EventPump? Events { get; set; }
        public TaskCompletionSource? Pause { get; set; }
        public List<string> Calls { get; } = [];
        public List<object?> FocusItems { get; } = [];
        public List<object?> CheckedAtEvent { get; } = [];
        public List<IReadOnlyList<Vb6Value>?> Arguments { get; } = [];
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
        {
            if (Events is { } pump)
                pump.Raise(name, async () =>
                {
                    Record(name, args);
                    if (name.EndsWith("_ItemClick") && Pause is { } pause)
                    {
                        await pump.WhileSuspended(new ValueTask(pause.Task));
                        FocusItems.Add(Read((VBListView)Child!, "SelectedItem"));
                    }
                });
            else
                Record(name, args);
        }
        private void Record(string name, IReadOnlyList<Vb6Value>? args)
        {
            Calls.Add(name.Replace("ListView1_", ""));
            Arguments.Add(args);
            if (name.EndsWith("_ItemCheck") && args?[0].Value is ICSharpPropertyBag item)
                CheckedAtEvent.Add(Read(item, "Checked"));
            FocusItems.Add(Read((VBListView)Child!, "SelectedItem"));
        }
    }

    private static object? Read(ICSharpPropertyBag bag, string name)
    {
        bag.TryGetProperty(name, out var value);
        return value.Value;
    }

    private static async Task<(Window Window, VBListView List, Recorder Recorder)> Open(long view = 3)
    {
        var list = new VBListView();
        VBProps.SetName(list, "ListView1");
        list.TrySetProperty("View", new(view));
        await list.Columns.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Name")]);
        await list.Columns.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Detail")]);
        list.TrySetProperty("Checkboxes", new(true));
        for (var i = 0; i < 4; i++)
            await list.Items.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Item " + i)]);
        var recorder = new Recorder { Child = list };
        var window = new Window { Content = recorder, Width = 320, Height = 180 };
        window.Show();
        return (window, list, recorder);
    }

    private static void Click(Window window, Point point, MouseButton button = MouseButton.Left, KeyModifiers modifiers = KeyModifiers.None)
    {
        window.MouseDown(point, button, (RawInputModifiers)modifiers);
        window.MouseUp(point, button, (RawInputModifiers)modifiers);
    }

    [AvaloniaTheory]
    [InlineData(MouseButton.Left)]
    [InlineData(MouseButton.Right)]
    public async Task Label_selects_and_raises_ordered_events_with_twips(MouseButton button)
    {
        var (window, list, recorder) = await Open();
        try
        {
            Click(window, new Point(30, 47), button);
            recorder.Calls.Should().Equal("MouseDown", "ItemClick", "MouseUp", "Click");
            Read(list, "SelectedItem").Should().BeSameAs(list.Items.Entries[1]);
            recorder.Arguments[0]![0].Value.Should().Be(button == MouseButton.Left ? 1 : 2);
            recorder.Arguments[0]![2].Value.Should().Be(405f);
            recorder.Arguments[0]![3].Value.Should().Be(660f);
            recorder.Arguments[1]![0].Value.Should().BeSameAs(list.Items.Entries[1]);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1, 555f, 405f)]
    [InlineData(0, 600f, 450f)]
    public async Task Mouse_coordinates_start_inside_the_edge(int appearance, float x, float y)
    {
        var (window, list, recorder) = await Open();
        try
        {
            var fields = new VBSerializedComponent();
            fields.Properties["NumItems"] = 2;
            fields.Properties["BorderStyle"] = 1;
            fields.Properties["Appearance"] = appearance;
            var instance = new ComponentInstance(ListViewComponentClass.Instance, "ListView1");
            instance.SetProperty(ListViewComponentClass.FilePropertiesProperty, fields);
            list.LoadProperties(instance);
            window.MouseMove(new Point(40, 30));
            Click(window, new Point(40, 30));
            foreach (var name in new[] { "MouseMove", "MouseDown", "MouseUp" })
            {
                var index = recorder.Calls.IndexOf(name);
                index.Should().BeGreaterThanOrEqualTo(0);
                recorder.Arguments[index]![2].Value.Should().Be(x);
                recorder.Arguments[index]![3].Value.Should().Be(y);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(0)]
    public async Task Header_hit_testing_starts_inside_the_edge(int appearance)
    {
        foreach (var inside in new[] { false, true })
        {
            var (window, list, recorder) = await Open();
            try
            {
                var fields = new VBSerializedComponent();
                fields.Properties["NumItems"] = 2;
                fields.Properties["BorderStyle"] = 1;
                fields.Properties["Appearance"] = appearance;
                fields.Properties["View"] = 3;
                var instance = new ComponentInstance(ListViewComponentClass.Instance, "ListView1");
                instance.SetProperty(ListViewComponentClass.FilePropertiesProperty, fields);
                list.LoadProperties(instance);
                var inset = appearance == 1 ? 3 : 0;
                if (!inside && inset == 0)
                    continue;
                Click(window, new Point(inset + (inside ? 0.5 : -0.5), inset + 0.5));
                if (inside)
                {
                    recorder.Calls.Should().Equal("ColumnClick");
                    recorder.Arguments[0]![0].Value.Should().BeSameAs(list.Columns.Entries[0]);
                }
                else
                    recorder.Calls.Should().NotContain("ColumnClick");
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public async Task Header_double_click_raises_only_one_ColumnClick()
    {
        var (window, list, recorder) = await Open();
        try
        {
            Click(window, new Point(110, 10));
            Click(window, new Point(110, 10));
            recorder.Calls.Should().Equal("ColumnClick");
            recorder.Arguments[0]![0].Value.Should().BeSameAs(list.Columns.Entries[1]);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Checkbox_changes_value_without_selection_or_ItemClick()
    {
        var (window, list, recorder) = await Open();
        try
        {
            Click(window, new Point(8, 47));
            recorder.Calls.Should().Equal("MouseDown", "ItemCheck", "MouseUp", "Click");
            Read(list.Items.Entries[1], "Checked").Should().Be(true);
            recorder.CheckedAtEvent.Should().Equal(true);
            Read(list, "SelectedItem").Should().BeSameAs(list.Items.Entries[0]);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Modified_click_has_no_SelectedItem_inside_ItemClick(bool shift)
    {
        var (window, list, recorder) = await Open();
        try
        {
            list.TrySetProperty("MultiSelect", new(true));
            Click(window, new Point(30, 83), modifiers: shift ? KeyModifiers.Shift : Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers);
            recorder.Calls.Should().Equal(shift
                ? ["MouseDown", "ItemClick", "ItemClick", "ItemClick", "MouseUp", "Click"]
                : ["MouseDown", "ItemClick", "MouseUp", "Click"]);
            recorder.FocusItems[recorder.Calls.Count - 3].Should().BeNull();
            recorder.FocusItems[^1].Should().BeSameAs(list.Items.Entries[3]);
            Read(list.Items.Entries[0], "Selected").Should().Be(true);
            Read(list.Items.Entries[1], "Selected").Should().Be(shift);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Double_click_has_one_ItemClick_and_no_second_MouseDown_or_Click()
    {
        var (window, _, recorder) = await Open();
        try
        {
            Click(window, new Point(30, 47));
            Click(window, new Point(30, 47));
            recorder.Calls.Should().Equal("MouseDown", "ItemClick", "MouseUp", "Click", "DblClick", "MouseUp");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Empty_space_drops_other_selections_and_preserves_focus()
    {
        var (window, list, recorder) = await Open();
        try
        {
            list.TrySetProperty("MultiSelect", new(true));
            list.TrySetProperty("SelectedItem", new(list.Items.Entries[1]));
            Click(window, new Point(250, 150));
            recorder.Calls.Should().Equal("MouseDown", "MouseUp", "Click");
            Read(list.Items.Entries[0], "Selected").Should().Be(false);
            Read(list.Items.Entries[1], "Selected").Should().Be(true);
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(2L)]
    public async Task Other_views_hit_the_drawn_label(long view)
    {
        var (window, list, recorder) = await Open(view);
        try
        {
            var label = list.GetItemLabelBounds(0);
            label.Width.Should().BeGreaterThan(0);
            label.Height.Should().BeGreaterThan(0);
            Click(window, label.Center);
            recorder.Calls.Should().Equal("MouseDown", "ItemClick", "MouseUp", "Click");
            recorder.Arguments[1]![0].Value.Should().BeSameAs(list.Items.Entries[0]);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(2L)]
    public async Task Label_hit_target_matches_the_drawn_rectangle(long view)
    {
        foreach (var inside in new[] { true, false })
        {
            var (window, list, recorder) = await Open(view);
            try
            {
                var label = list.GetItemLabelBounds(0);
                var point = new Point(inside ? label.Right - 0.5 : label.Right + 0.5, label.Center.Y);
                Click(window, point);
                recorder.Calls.Should().Equal(inside
                    ? ["MouseDown", "ItemClick", "MouseUp", "Click"]
                    : ["MouseDown", "MouseUp", "Click"]);
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public async Task Code_changes_raise_no_mouse_or_item_events()
    {
        var (window, list, recorder) = await Open();
        try
        {
            list.TrySetProperty("SelectedItem", new(list.Items.Entries[1]));
            list.Items.Entries[0].TrySetProperty("Selected", new(true));
            list.TrySetProperty("SelectedItem", Vb6Value.Nothing);
            list.Items.Entries[0].TrySetProperty("Checked", new(true));
            list.Items.Entries[0].TrySetProperty("Text", new("Changed"));
            list.Columns.Entries[0].TrySetProperty("Width", new(2000f));
            await list.Items.CallAsync("Add", []);
            await list.Items.CallAsync("Remove", [new(3)]);
            list.TrySetProperty("Sorted", new(true));
            recorder.Calls.Should().BeEmpty();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Modified_ItemClick_keeps_its_focus_state_while_the_handler_is_suspended()
    {
        var (window, list, recorder) = await Open();
        try
        {
            recorder.Events = new EventPump();
            recorder.Pause = new TaskCompletionSource();
            list.TrySetProperty("MultiSelect", new(true));
            Click(window, new Point(30, 47), modifiers: Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers);
            recorder.Calls.Should().Equal("MouseDown", "ItemClick");
            recorder.FocusItems[^1].Should().BeNull();
            recorder.Pause.SetResult();
            await Task.Yield();
            recorder.Calls.Should().Equal("MouseDown", "ItemClick", "MouseUp", "Click");
            recorder.FocusItems[2].Should().BeNull();
            recorder.FocusItems[^1].Should().BeSameAs(list.Items.Entries[1]);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Report_row_beyond_the_label_does_not_select_until_FullRowSelect()
    {
        var (window, list, recorder) = await Open();
        try
        {
            Click(window, new Point(150, 47));
            recorder.Calls.Should().Equal("MouseDown", "MouseUp", "Click");
            foreach (var item in list.Items.Entries)
                Read(item, "Selected").Should().Be(false);
            recorder.Calls.Clear();
            list.TrySetProperty("FullRowSelect", new(true));
            Click(window, new Point(180, 65));
            recorder.Calls.Should().Equal("MouseDown", "ItemClick", "MouseUp", "Click");
            Read(list, "SelectedItem").Should().BeSameAs(list.Items.Entries[2]);
        }
        finally { window.Close(); }
    }

}
