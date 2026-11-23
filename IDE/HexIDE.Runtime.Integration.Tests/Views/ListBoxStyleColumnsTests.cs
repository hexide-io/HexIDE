using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media;
using HexIDE.Integration.Tests;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

// #4209's supervisor table and docs/vb6-fidelity-oracle/a-listboxs-page-up-checkboxes-and-columns.md.
public class ListBoxStyleColumnsTests : WindowTestFixture
{
    private sealed class Recorder : Decorator, IModuleExecutionRoot
    {
        internal VBListBox List { get; set; } = null!;
        internal List<string> Calls { get; } = [];
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
        {
            if (name is "List1_KeyDown" or "List1_KeyUp")
                Calls.Add(name == "List1_KeyDown" ? "KeyDown" : "KeyUp");
            else if (name == "List1_ItemCheck")
                Calls.Add($"{name} {args![0].Value}");
            else if (name is "List1_Click" or "List1_Scroll")
                Calls.Add($"{name} ({List.ListIndex}, {List.TopIndex})");
        }
    }

    private static async Task<(Recorder Recorder, ModuleExecutionContext Ctx, ExecutionEnvironment Env, Window Window)> Loaded(int style, int columns, int multi = 0, double scaling = 1)
    {
        var form = $$"""
            VERSION 5.00
            Begin VB.Form Form1
               Begin VB.ListBox List1
                  Left = 120
                  Top = 120
                  Width = 2400
                  Height = 2205
                  Style = {{style}}
                  Columns = {{columns}}
                  MultiSelect = {{multi}}
               End
            End
            Attribute VB_Name = "Form1"
            """;
        var (root, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(form);
        var list = (VBListBox)ContainerRuntimeHarness.Child(canvas, "List1");
        var recorder = new Recorder { Child = root, List = list };
        var window = new Window { Content = recorder, Width = 400, Height = 300 };
        ContainerRuntimeHarness.MergeAppResources(window);
        window.SetRenderScaling(scaling);
        window.Show();
        await ContainerRuntimeHarness.Run(ctx, env, "For i = 0 To 59\r\nList1.AddItem CStr(i)\r\nNext\r\n");
        Dispatcher.UIThread.RunJobs();
        recorder.Calls.Clear();
        return (recorder, ctx, env, window);
    }

    [AvaloniaTheory]
    [InlineData(1, 0)]
    [InlineData(0, 2)]
    public async Task Saved_properties_read_back_at_runtime(int style, int columns)
    {
        var (_, ctx, env, _) = await Loaded(style, columns);
        var values = await ContainerRuntimeHarness.Run(ctx, env, "Debug.Print List1.Style\r\nDebug.Print List1.Columns\r\n");
        values.Select(v => v.Value).Should().Equal(style, columns);
    }

    [Fact]
    public void ItemCheck_declares_its_Integer_item_argument()
    {
        var ev = ListBoxComponentClass.Instance.Events.Single(e => e.Name == "ItemCheck");
        ev.Arguments.Should().ContainSingle().Which.Should().BeEquivalentTo(new EventClassArgument("Item", "Integer"));
    }

    [AvaloniaTheory]
    [InlineData(1, 0, Key.Down, 27, 17, 28, 20)]
    [InlineData(1, 0, Key.PageDown, 28, 20, 36, 28)]
    [InlineData(1, 0, Key.PageUp, 36, 28, 28, 28)]
    [InlineData(1, 0, Key.End, 28, 28, 59, 51)]
    [InlineData(1, 0, Key.Home, 59, 51, 0, 0)]
    [InlineData(1, 0, Key.PageUp, 31, 28, 23, 23)]
    [InlineData(1, 0, Key.PageDown, 2, 0, 10, 2)]
    [InlineData(0, 2, Key.Down, 21, 9, 22, 9)]
    [InlineData(0, 2, Key.Right, 3, 0, 12, 0)]
    [InlineData(0, 2, Key.Right, 14, 0, 23, 9)]
    [InlineData(0, 2, Key.PageDown, 0, 0, 17, 0)]
    [InlineData(0, 2, Key.PageDown, 17, 0, 34, 18)]
    [InlineData(0, 2, Key.PageUp, 34, 18, 17, 9)]
    [InlineData(0, 2, Key.End, 17, 9, 59, 45)]
    [InlineData(0, 2, Key.Home, 59, 45, 0, 0)]
    public async Task Keys_follow_measured_selection_top_and_event_order(int style, int columns, Key key, int index, int top, int expectedIndex, int expectedTop)
    {
        var (recorder, ctx, env, window) = await Loaded(style, columns);
        var list = recorder.List;
        list.Focus().Should().BeTrue();
        await ContainerRuntimeHarness.Run(ctx, env, $"List1.ListIndex = {index}\r\nList1.TopIndex = {top}\r\n");
        if (list.ContainerFromIndex(index) is { } item)
            item.Focus(NavigationMethod.Pointer).Should().BeTrue();
        Dispatcher.UIThread.RunJobs();
        recorder.Calls.Clear();
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        list.ListIndex.Should().Be(expectedIndex);
        list.TopIndex.Should().Be(expectedTop);
        var expected = new List<string> { "KeyDown", $"List1_Click ({expectedIndex}, {expectedTop})" };
        if (top != expectedTop) expected.Add($"List1_Scroll ({expectedIndex}, {expectedTop})");
        expected.Add("KeyUp");
        recorder.Calls.Should().Equal(expected);
        window.FocusManager!.GetFocusedElement().Should().BeSameAs(list.ContainerFromIndex(expectedIndex));
    }

    [AvaloniaTheory]
    [InlineData(1, 0, 59, 51)]
    [InlineData(0, 2, 25, 18)]
    [InlineData(0, 2, 59, 45)]
    public async Task TopIndex_snaps_and_clamps_once(int style, int columns, int target, int expectedTop)
    {
        var (recorder, ctx, env, _) = await Loaded(style, columns);
        await ContainerRuntimeHarness.Run(ctx, env, $"List1.TopIndex = {target}\r\n");
        recorder.List.TopIndex.Should().Be(expectedTop);
        recorder.Calls.Should().Equal($"List1_Scroll ({(style == 1 ? 0 : -1)}, {expectedTop})");
        recorder.Calls.Clear();
        await ContainerRuntimeHarness.Run(ctx, env, $"List1.TopIndex = {target}\r\n");
        recorder.Calls.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(21, 9)]
    [InlineData(50, 36)]
    public async Task Code_selection_scrolls_columns_before_Click_without_Scroll(int index, int expectedTop)
    {
        var (recorder, ctx, env, _) = await Loaded(0, 2);
        await ContainerRuntimeHarness.Run(ctx, env, $"List1.ListIndex = {index}\r\n");
        Dispatcher.UIThread.RunJobs();
        recorder.List.TopIndex.Should().Be(expectedTop);
        recorder.Calls.Should().Equal($"List1_Click ({index}, {expectedTop})");
    }

    [AvaloniaFact]
    public async Task Space_toggles_check_with_Click_before_ItemCheck_through_real_keys()
    {
        var (recorder, ctx, env, window) = await Loaded(1, 0);
        await ContainerRuntimeHarness.Run(ctx, env, "List1.ListIndex = 0\r\n");
        recorder.List.ContainerFromIndex(0)!.Focus().Should().BeTrue();
        Dispatcher.UIThread.RunJobs();
        recorder.Calls.Clear();
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        Dispatcher.UIThread.RunJobs();
        recorder.Calls.Should().Equal("KeyDown", "List1_Click (0, 0)", "List1_ItemCheck 0", "KeyUp");
        var values = await ContainerRuntimeHarness.Run(ctx, env, "Debug.Print List1.Selected(0)\r\nDebug.Print List1.Selected(1)\r\n");
        values.Select(v => v.Value).Should().Equal(true, false);
        recorder.Calls.Clear();
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        Dispatcher.UIThread.RunJobs();
        recorder.Calls.Should().Equal("KeyDown", "List1_Click (0, 0)", "List1_ItemCheck 0", "KeyUp");
        recorder.List.IsItemSelected(0).Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData(5, 0)]
    [InlineData(50, 42)]
    public async Task Selected_checks_and_scrolls_with_ItemCheck_before_Click(int index, int top)
    {
        var (recorder, ctx, env, _) = await Loaded(1, 0);
        await ContainerRuntimeHarness.Run(ctx, env, $"List1.Selected({index}) = True\r\n");
        Dispatcher.UIThread.RunJobs();
        recorder.List.TopIndex.Should().Be(top);
        recorder.Calls.Should().Equal($"List1_ItemCheck {index}", $"List1_Click ({index}, {top})");
        var values = await ContainerRuntimeHarness.Run(ctx, env, $"Debug.Print List1.Selected({index})\r\nDebug.Print List1.Selected(0)\r\n");
        values.Select(v => v.Value).Should().Equal(true, false);
    }
    [AvaloniaTheory]
    [InlineData(0, 0, 11, 1.0)]
    [InlineData(0, 0, 11, 1.5)]
    [InlineData(1, 0, 9, 1.0)]
    [InlineData(1, 0, 9, 1.5)]
    [InlineData(0, 2, 9, 1.0)]
    [InlineData(0, 2, 9, 1.5)]
    public async Task Saved_box_size_shows_measured_rows_and_column_order(int style, int columns, int rows, double scaling)
    {
        var (recorder, _, _, _) = await Loaded(style, columns, scaling: scaling);
        var list = recorder.List;
        var first = list.ContainerFromIndex(0)!;
        ((int)Math.Floor(list.Scroll!.Viewport.Height / first.Bounds.Height)).Should().Be(rows);
        if (columns > 0)
        {
            var nextColumn = list.ContainerFromIndex(rows)!;
            nextColumn.Bounds.Y.Should().BeApproximately(first.Bounds.Y, 0.01);
            nextColumn.Bounds.X.Should().BeGreaterThan(first.Bounds.X);
            list.ContainerFromIndex(rows - 1)!.Bounds.Y.Should().BeGreaterThan(first.Bounds.Y);
            nextColumn.Bounds.Width.Should().BeApproximately(first.Bounds.Width, 0.01);
            // The measured pitch keeps the second column within the viewport.
            (nextColumn.Bounds.Right - first.Bounds.X).Should().BeLessThanOrEqualTo(list.Scroll.Viewport.Width + 0.01);
        }
    }

    // SP6 measurements: docs/vb6-fidelity-oracle/a-lists-saved-items-in-the-designer-and-at-load.md (#5133).
    private static (VBListBox List, Window Window) SavedLayout(bool designer, int style, int columns,
        double width = 129, double scaling = 1)
    {
        var instance = new ComponentInstance(ListBoxComponentClass.Instance, "List1");
        instance.UnknownRawProperties.Add(new("Style", [$"Style = {style}"]));
        instance.UnknownRawProperties.Add(new("Columns", [$"Columns = {columns}"]));
        var items = style == 1 ? new[] { "alpha", "beta" } : Enumerable.Range(1, designer ? 6 : 12).Select(i => $"c{i}").ToArray();
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
        {
            writer.Write((ushort)items.Length);
            writer.Write((ushort)5);
            foreach (var item in items) { writer.Write((ushort)item.Length); writer.Write(Encoding.ASCII.GetBytes(item)); }
        }
        instance.SetProperty(VBProperties.ListProperty, stream.ToArray());
        var list = (VBListBox)(designer
            ? ListBoxComponentClass.Instance.InstantiateForDesigner(instance)
            : ListBoxComponentClass.Instance.Instantiate(instance));
        list.Width = width;
        list.Height = 43;
        var window = new Window { Content = new Canvas { Children = { list } }, Width = 400, Height = 200 };
        ContainerRuntimeHarness.MergeAppResources(window);
        window.SetRenderScaling(scaling);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (list, window);
    }

    [AvaloniaTheory]
    [InlineData(129, 2, 63)]
    [InlineData(167, 2, 82)]
    [InlineData(167, 3, 54)]
    [InlineData(200, 2, 98)]
    [InlineData(200, 3, 65)]
    [InlineData(200, 4, 49)]
    [InlineData(198, 4, 48)]
    [InlineData(202, 4, 49)]
    [InlineData(2500.0 / 15, 2, 82)]
    public void Run_columns_use_measured_whole_pixel_pitch(double width, int columns, int pitch)
    {
        var (list, window) = SavedLayout(false, 0, columns, width);
        try
        {
            var first = list.ContainerFromIndex(0)!;
            var second = list.ContainerFromIndex(1)!;
            second.Bounds.Y.Should().Be(first.Bounds.Y);
            (second.Bounds.X - first.Bounds.X).Should().Be(pitch);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1, 1.0)]
    [InlineData(2, 1.0)]
    [InlineData(2, 1.5)]
    public void Designer_columns_run_down_then_across_at_120_DIPs(int columns, double scaling)
    {
        var (list, window) = SavedLayout(true, 0, columns, scaling: scaling);
        try
        {
            list.Items.Should().Equal("c1", "c2", "c3", "c4", "c5", "c6");
            var first = list.ContainerFromIndex(0)!;
            var second = list.ContainerFromIndex(1)!;
            second.Bounds.Y.Should().Be(first.Bounds.Y);
            (second.Bounds.X - first.Bounds.X).Should().Be(120);
            list.Scroll!.Extent.Width.Should().BeGreaterThan(list.Scroll.Viewport.Width);
            ScrollViewer.GetHorizontalScrollBarVisibility(list).Should().Be(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Designer_checkbox_items_draw_unchecked_glyphs()
    {
        var (list, window) = SavedLayout(true, 1, 0);
        try
        {
            list.Items.Should().Equal("alpha", "beta");
            foreach (var index in new[] { 0, 1 })
            {
                list.ContainerFromIndex(index)!.GetVisualDescendants().Should().Contain(c => c.GetType().Name == "CheckGlyph");
                list.IsItemSelected(index).Should().BeFalse();
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void Plain_designer_and_single_column_run_keep_full_width_rows(bool designer, int columns)
    {
        var (list, window) = SavedLayout(designer, 0, columns);
        try
        {
            var first = list.ContainerFromIndex(0)!;
            first.Bounds.Width.Should().BeApproximately(list.Scroll!.Viewport.Width - list.Padding.Left - list.Padding.Right, 0.01);
            if (designer)
            {
                var second = list.ContainerFromIndex(1)!;
                second.Bounds.X.Should().Be(first.Bounds.X);
                second.Bounds.Y.Should().BeGreaterThan(first.Bounds.Y);
            }
        }
        finally { window.Close(); }
    }

    // Drawing is a reasoned requirement in #4209, not a claim about VB6's checkbox pixels.
    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public async Task Checking_changes_the_checkbox_pixels_at_each_scale(double scaling)
    {
        var (recorder, ctx, env, window) = await Loaded(1, 0, scaling: scaling);
        var list = recorder.List;
        list.Foreground = Brushes.DarkRed;
        list.Background = Brushes.LightYellow;
        await ContainerRuntimeHarness.Run(ctx, env, "List1.ListIndex = 0\r\n");
        var before = CheckboxPixels(window, list);
        await ContainerRuntimeHarness.Run(ctx, env, "List1.Selected(0) = True\r\n");
        CheckboxPixels(window, list).Should().NotEqual(before);
    }

    private static byte[] CheckboxPixels(Window window, VBListBox list)
    {
        Dispatcher.UIThread.RunJobs();
        var glyph = list.ContainerFromIndex(0)!.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => c.GetType().Name == "CheckGlyph");
        glyph.Should().NotBeNull("a checkbox is drawn beside the item");
        var origin = glyph!.TranslatePoint(default, window)!.Value;
        var scale = window.RenderScaling;
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame");
        using var buffer = frame.Lock();
        var bytes = new List<byte>();
        for (var y = (int)Math.Ceiling(origin.Y * scale); y < (origin.Y + glyph.Bounds.Height) * scale; y++)
        for (var x = (int)Math.Ceiling(origin.X * scale); x < (origin.X + glyph.Bounds.Width) * scale; x++)
        for (var channel = 0; channel < 4; channel++)
            bytes.Add(Marshal.ReadByte(buffer.Address, y * buffer.RowBytes + x * 4 + channel));
        return bytes.ToArray();
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Style_one_with_MultiSelect_keeps_plain_multiselect_behaviour(int multi)
    {
        var (recorder, ctx, env, _) = await Loaded(1, 0, multi);
        await ContainerRuntimeHarness.Run(ctx, env, "List1.Selected(5) = True\r\nList1.Selected(7) = True\r\n");
        recorder.List.SelectedCount.Should().Be(2);
        recorder.List.IsItemSelected(5).Should().BeTrue();
        recorder.List.IsItemSelected(7).Should().BeTrue();
        recorder.Calls.Should().OnlyContain(c => c.StartsWith("List1_Click", StringComparison.Ordinal));
        recorder.List.GetVisualDescendants().Should().NotContain(c => c.GetType().Name == "CheckGlyph");
    }

    [AvaloniaTheory]
    [InlineData("List1.Selected(5) = False", false)]
    public async Task Unmeasured_checkbox_writes_refuse_without_changing_state(string statement, bool checkFirst)
    {
        var (recorder, ctx, env, _) = await Loaded(1, 0);
        if (checkFirst) await ContainerRuntimeHarness.Run(ctx, env, "List1.Selected(5) = True\r\n");
        recorder.Calls.Clear();
        Func<Task> run = async () => await ContainerRuntimeHarness.Run(ctx, env, statement + "\r\n");
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*#4209*");
        recorder.List.IsItemSelected(5).Should().Be(checkFirst);
        recorder.Calls.Should().BeEmpty();
    }

}
