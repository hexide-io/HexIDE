using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Layout;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Integration.Tests;
using HexIDE.Integration.Tests.Views;

namespace HexIDE.Runtime.Integration.Tests.Views;

/// <summary>Measurements in docs/vb6-fidelity-oracle/a-listboxs-scroll-event.md (#3275, measuring PR #3286).</summary>
public class ListBoxScrollTests : WindowTestFixture
{
    [Fact]
    public void ListBox_declares_the_measured_Scroll_event()
    {
        HexIDE.Runtime.Components.ListBoxComponentClass.Instance.Events.Should().Contain(e => e.Name == "Scroll");
    }

    private const string FormWithList = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.ListBox List1
              Left            =   120
              Top             =   120
              Width           =   2400
              Height          =   1200
           End
        End
        Attribute VB_Name = "Form1"
        """;

    /// <summary>Records each handler a control raises, with List1's TopIndex at that moment.</summary>
    private sealed class Recorder : Decorator, IModuleExecutionRoot
    {
        public VBListBox? List { get; set; }
        public List<string> Calls { get; } = new();
        public List<int> ScrollListIndices { get; } = new();
        public List<string> States { get; } = new();
        private int Top() => ListMembers.TryGet(List!, "TopIndex", out var value) ? (int)value.Value! : -999;
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
        {
            if (name is "List1_Click" or "List1_Scroll")
                States.Add($"{name} index={List!.ListIndex} top={Top()}");
            Calls.Add(name is "List1_Scroll" or "List1_Click" ? $"{name} top={Top()}" : name);
            if (name == "List1_Scroll")
                ScrollListIndices.Add(List!.ListIndex);
        }
    }

    private static async Task<(Recorder Recorder, ModuleExecutionContext Ctx, ExecutionEnvironment Env)> Loaded(string setup, double? fontSize = null, int multiSelect = 0)
    {
        var form = FormWithList.Replace("Begin VB.ListBox List1",
            $"Begin VB.ListBox List1\n              MultiSelect = {multiSelect}");
        var (root, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(form);
        var recorder = new Recorder { Child = root, List = (VBListBox)ContainerRuntimeHarness.Child(canvas, "List1") };
        var window = new Window { Content = recorder, Width = 400, Height = 300 };
        ContainerRuntimeHarness.MergeAppResources(window);
        window.Show();
        await ContainerRuntimeHarness.Run(ctx, env, setup);
        Dispatcher.UIThread.RunJobs();
        var list = recorder.List!;
        if (fontSize is not null)
        {
            list.FontSize = fontSize.Value;
            list.UpdateLayout();
        }
        var rowHeight = list.ContainerFromIndex(0)!.Bounds.Height;
        list.Height += 11 * rowHeight - list.Scroll!.Viewport.Height;
        window.Height = System.Math.Max(window.Height, list.Height + 60);
        list.UpdateLayout();
        list.Scroll.Viewport.Height.Should().BeApproximately(11 * rowHeight, 0.01);
        recorder.Calls.Clear();
        return (recorder, ctx, env);
    }

    private static async Task<List<string>> After(Recorder recorder, ModuleExecutionContext ctx, ExecutionEnvironment env, string code)
    {
        await ContainerRuntimeHarness.Run(ctx, env, code);
        Dispatcher.UIThread.RunJobs();
        return recorder.Calls.Where(c => c.StartsWith("List1_", System.StringComparison.Ordinal)).ToList();
    }

    private const string SixtyItems = "For i = 0 To 59\r\nList1.AddItem CStr(i)\r\nNext\r\n";

    [AvaloniaTheory]
    [InlineData(0, 5, 5, true)]
    [InlineData(5, 0, 0, true)]
    [InlineData(5, 5, 5, false)]
    public async Task TopIndex_assignment_source_changes_raise_once_and_same_value_raises_none(int initial, int target, int expected, bool raises)
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        await After(recorder, ctx, env, $"List1.TopIndex = {initial}\r\n");
        recorder.Calls.Clear();
        var calls = await After(recorder, ctx, env, $"List1.TopIndex = {target}\r\nDebug.Print List1.TopIndex\r\n");
        calls.Should().Equal(raises ? new[] { $"List1_Scroll top={expected}" } : System.Array.Empty<string>());
        var printed = await ContainerRuntimeHarness.Run(ctx, env, "Debug.Print List1.TopIndex\r\n");
        printed.Select(v => VbDisplay.ToText(v, System.Globalization.CultureInfo.InvariantCulture)).Should().Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static (Window Window, ScrollBar Bar, Point Point) BarPoint(VBListBox list, double fraction)
    {
        var window = (Window)TopLevel.GetTopLevel(list)!;
        var bar = list.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == Orientation.Vertical);
        return (window, bar, bar.TranslatePoint(new Point(bar.Bounds.Width / 2, bar.Bounds.Height * fraction), window)!.Value);
    }

    [AvaloniaTheory]
    [InlineData(6, 0.98, 7, null)]
    [InlineData(6, 0.98, 7, 24.0)]
    [InlineData(7, 0.8, 17, null)]
    [InlineData(0, 0.02, 0, null)]
    public async Task Scrollbar_source_arrow_track_and_top_boundary(int top, double fraction, int expected, double? fontSize)
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems, fontSize);
        await After(recorder, ctx, env, $"List1.TopIndex = {top}\r\n");
        recorder.List!.Focus();
        Dispatcher.UIThread.RunJobs();
        recorder.Calls.Clear();
        var (window, _, point) = BarPoint(recorder.List!, fraction);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        recorder.Calls.Should().Equal(expected == top ? System.Array.Empty<string>() : new[] { $"List1_Scroll top={expected}" });
    }

    [AvaloniaFact]
    public async Task Scrollbar_source_thumb_moves_raise_each_time_and_release_raises_none()
    {
        var (recorder, _, _) = await Loaded(SixtyItems);
        var list = recorder.List!;
        var (window, bar, _) = BarPoint(list, 0);
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
        var point = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        recorder.Calls.Clear();
        window.MouseMove(point + new Vector(0, 20));
        Dispatcher.UIThread.RunJobs();
        recorder.Calls.Where(c => c != "List1_MouseMove").Should().ContainSingle().Which.Should().Be($"List1_Scroll top={list.TopIndex}");
        var first = list.TopIndex;
        recorder.Calls.Clear();
        window.MouseMove(point + new Vector(0, 40));
        Dispatcher.UIThread.RunJobs();
        list.TopIndex.Should().BeGreaterThan(first);
        recorder.Calls.Where(c => c != "List1_MouseMove").Should().ContainSingle().Which.Should().Be($"List1_Scroll top={list.TopIndex}");
        recorder.Calls.Clear();
        window.MouseUp(point + new Vector(0, 40), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        recorder.Calls.Where(c => c != "List1_MouseMove").Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(Key.Down, 27, 17, 18)]
    [InlineData(Key.PageDown, 27, 18, 27)]
    [InlineData(Key.Home, 37, 27, 0)]
    [InlineData(Key.End, 0, 0, 49)]
    public async Task Key_source_moves_top_row_with_Click_then_Scroll(Key key, int selection, int top, int expected)
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        await After(recorder, ctx, env, $"List1.ListIndex = {selection}\r\nList1.TopIndex = {top}\r\n");
        recorder.List!.Focus();
        Dispatcher.UIThread.RunJobs();
        recorder.Calls.Clear();
        var window = (Window)TopLevel.GetTopLevel(recorder.List)!;
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        // #2325's key-events oracle: KeyDown now precedes the existing Click/Scroll path.
        recorder.Calls.Should().Equal("List1_KeyDown", $"List1_Click top={expected}", $"List1_Scroll top={expected}");
    }

    [AvaloniaTheory]
    [InlineData(Key.Up, 27, 17, true)]
    [InlineData(Key.Down, 59, 49, false)]
    public async Task Key_source_without_top_row_change_raises_no_Scroll(Key key, int selection, int top, bool click)
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        await After(recorder, ctx, env, $"List1.ListIndex = {selection}\r\nList1.TopIndex = {top}\r\n");
        recorder.List!.Focus();
        Dispatcher.UIThread.RunJobs();
        recorder.Calls.Clear();
        ((Window)TopLevel.GetTopLevel(recorder.List)!).KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        recorder.Calls.Should().Equal(click ? new[] { "List1_KeyDown", $"List1_Click top={top}" } : new[] { "List1_KeyDown" });
    }

    [AvaloniaTheory]
    [InlineData(Key.Down, 27, 17, 28, 18)]
    [InlineData(Key.PageDown, 27, 18, 37, 27)]
    [InlineData(Key.Home, 37, 27, 0, 0)]
    [InlineData(Key.End, 0, 0, 59, 49)]
    [InlineData(Key.Up, 27, 17, 26, 17)]
    [InlineData(Key.Down, 59, 49, 59, 49)]
    public async Task Key_source_with_item_focus_matches_oracle(Key key, int selection, int top, int expectedSelection, int expectedTop)
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        await After(recorder, ctx, env, $"List1.ListIndex = {selection}\r\nList1.TopIndex = {top}\r\n");
        var list = recorder.List!;
        list.ContainerFromIndex(selection)!.Focus(NavigationMethod.Pointer).Should().BeTrue();
        Dispatcher.UIThread.RunJobs();
        recorder.Calls.Clear();
        ((Window)TopLevel.GetTopLevel(list)!).KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        list.SelectedIndex.Should().Be(expectedSelection);
        var expected = new List<string>();
        if (selection != expectedSelection)
            expected.Add($"List1_Click top={expectedTop}");
        if (top != expectedTop)
            expected.Add($"List1_Scroll top={expectedTop}");
        recorder.Calls.Where(c => c.StartsWith("List1_Click", System.StringComparison.Ordinal)
            || c.StartsWith("List1_Scroll", System.StringComparison.Ordinal)).Should().Equal(expected);
    }

    // #3275's third-round review: the next arrow starts from Page Down's selection.
    [AvaloniaTheory]
    [InlineData(3)]
    [InlineData(27)]
    public async Task PageDown_source_with_item_focus_moves_focus_so_Up_follows_selection(int selection)
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        await After(recorder, ctx, env, $"List1.ListIndex = {selection}\r\n");
        var list = recorder.List!;
        list.ContainerFromIndex(selection)!.Focus(NavigationMethod.Pointer).Should().BeTrue();
        Dispatcher.UIThread.RunJobs();
        var window = (Window)TopLevel.GetTopLevel(list)!;
        window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        var pagedSelection = list.SelectedIndex;
        pagedSelection.Should().BeGreaterThan(selection);
        window.FocusManager!.GetFocusedElement().Should().BeSameAs(list.ContainerFromIndex(pagedSelection));
        recorder.Calls.Clear();
        window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        list.SelectedIndex.Should().Be(pagedSelection - 1);
        window.FocusManager.GetFocusedElement().Should().BeSameAs(list.ContainerFromIndex(pagedSelection - 1));
        recorder.Calls.Where(c => c.StartsWith("List1_Click", System.StringComparison.Ordinal))
            .Should().Equal($"List1_Click top={list.TopIndex}");
    }

    // #3986's 2026-11-16 measurements, oracle PR #4208: a-listboxs-page-up-checkboxes-and-columns.md. Both keys move
    // the selection a page, the rows shown less one, from wherever it is, and the view scrolls the least it can. The
    // first four Page Up rows can't tell that from "top row first"; the rows after them can (mid-view, one below the
    // top row, out of view). Keys enter through the window, with item focus where the item has a container.
    [AvaloniaTheory]
    [InlineData(Key.PageUp, 37, 27, 27, 27)]
    [InlineData(Key.PageUp, 30, 30, 20, 20)]
    [InlineData(Key.PageUp, 5, 0, 0, 0)]
    [InlineData(Key.PageUp, 0, 0, 0, 0)]
    [InlineData(Key.PageUp, 32, 27, 22, 22)]
    [InlineData(Key.PageUp, 28, 27, 18, 18)]
    [InlineData(Key.PageUp, 5, 20, 0, 0)]
    [InlineData(Key.PageUp, 50, 20, 40, 30)]
    [InlineData(Key.PageDown, 4, 0, 14, 4)]
    [InlineData(Key.PageDown, 10, 0, 20, 10)]
    [InlineData(Key.PageDown, 40, 0, 50, 40)]
    [InlineData(Key.PageDown, 2, 20, 12, 12)]
    public async Task Page_keys_move_a_page_from_the_selection_as_measured(Key key, int selection, int top, int expectedSelection, int expectedTop)
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        // As vb6.exe was driven: focus on the list first, then the selection and the top row, then the key.
        recorder.List!.Focus(NavigationMethod.Pointer).Should().BeTrue();
        await After(recorder, ctx, env, $"List1.ListIndex = {selection}\r\nList1.TopIndex = {top}\r\n");
        var list = recorder.List!;
        list.TopIndex.Should().Be(top);
        var rows = (int)System.Math.Floor(list.Scroll!.Viewport.Height / list.ContainerFromIndex(top)!.Bounds.Height);
        rows.Should().Be(11, "the expected values are vb6.exe's, measured on a list showing 11 rows");
        // An item in view takes the focus, as after a click; one out of view leaves it on the list, since focusing
        // the item would scroll it into view.
        if (list.ContainerFromIndex(selection) is { IsEffectivelyVisible: true } item && selection >= top && selection < top + rows)
            item.Focus(NavigationMethod.Pointer).Should().BeTrue();
        Dispatcher.UIThread.RunJobs();
        recorder.States.Clear();
        // Observe the real input route around the VB handlers, including handled presses.
        recorder.AddHandler(InputElement.KeyDownEvent, (_, _) => recorder.States.Add($"KeyDown index={list.ListIndex} top={list.TopIndex}"), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        recorder.AddHandler(InputElement.KeyUpEvent, (_, _) => recorder.States.Add($"KeyUp index={list.ListIndex} top={list.TopIndex}"), handledEventsToo: true);
        var window = (Window)TopLevel.GetTopLevel(list)!;
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        list.ListIndex.Should().Be(expectedSelection);
        list.TopIndex.Should().Be(expectedTop);
        window.FocusManager!.GetFocusedElement().Should().BeSameAs(list.ContainerFromIndex(expectedSelection));
        var expected = new List<string> { $"KeyDown index={selection} top={top}" };
        if (selection != expectedSelection)
            expected.Add($"List1_Click index={expectedSelection} top={expectedTop}");
        if (top != expectedTop)
            expected.Add($"List1_Scroll index={expectedSelection} top={expectedTop}");
        expected.Add($"KeyUp index={expectedSelection} top={expectedTop}");
        recorder.States.Should().Equal(expected);
    }

    // #4229, measured on vb6.exe SP6; oracle measuring PR #4208.
    [AvaloniaTheory]
    [InlineData(2, 20, false)]
    [InlineData(50, 20, false)]
    [InlineData(40, 0, false)]
    [InlineData(2, 20, true)]
    [InlineData(50, 20, true)]
    [InlineData(40, 0, true)]
    public async Task TopIndex_after_code_selection_survives_the_dispatcher(int selection, int top, bool selected)
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        var assignment = selected ? $"List1.Selected({selection}) = True" : $"List1.ListIndex = {selection}";
        await After(recorder, ctx, env, $"{assignment}\r\nList1.TopIndex = {top}\r\n");
        recorder.List!.TopIndex.Should().Be(top);
        recorder.List.ListIndex.Should().Be(selection);
        recorder.Calls.Where(c => c.StartsWith("List1_Scroll", System.StringComparison.Ordinal))
            .Should().Equal($"List1_Scroll top={top}");
        recorder.Calls.Count(c => c.StartsWith("List1_Click", System.StringComparison.Ordinal)).Should().Be(1);
    }

    // Preserve the explicit focus scrolling used by the measured Simple/Extended arrow-key paths (#1413).
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Multiselect_arrows_keep_the_focused_item_in_view(int multiSelect)
    {
        var (recorder, _, _) = await Loaded(SixtyItems, multiSelect: multiSelect);
        var list = recorder.List!;
        list.Focus().Should().BeTrue();
        var window = (Window)TopLevel.GetTopLevel(list)!;
        for (var i = 0; i < 11; i++)
        {
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Dispatcher.UIThread.RunJobs();
        }
        list.ListIndex.Should().Be(11);
        list.TopIndex.Should().BeGreaterThan(0);
        window.FocusManager!.GetFocusedElement().Should().BeSameAs(list.ContainerFromIndex(11));
    }

    // Selected(i) = True on a MultiSelect list scrolls the least it can to show the item, as on a single-select one, with
    // no Scroll (measured on vb6.exe SP6, native and p-code, #4229; the oracle's
    // selected-on-a-multiselect-listbox-scrolls-the-item-into-view.md).
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Multiselect_Selected_brings_the_item_into_view_without_Scroll(int multiSelect)
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems, multiSelect: multiSelect);
        var list = recorder.List!;

        var below = await After(recorder, ctx, env, "List1.Selected(50) = True\r\n");
        list.TopIndex.Should().Be(40);
        list.ListIndex.Should().Be(50);
        below.Should().ContainSingle(c => c.StartsWith("List1_Click", System.StringComparison.Ordinal))
            .And.NotContain(c => c.StartsWith("List1_Scroll", System.StringComparison.Ordinal));

        await After(recorder, ctx, env, "List1.TopIndex = 45\r\n");
        recorder.Calls.Clear();
        var above = await After(recorder, ctx, env, "List1.Selected(2) = True\r\n");
        list.TopIndex.Should().Be(2);
        list.ListIndex.Should().Be(2);
        list.SelectedCount.Should().Be(2);
        above.Should().NotContain(c => c.StartsWith("List1_Scroll", System.StringComparison.Ordinal));
    }

    // A list's members, #821: selection follows its item without Click. No new TopIndex value is assumed here.
    [AvaloniaTheory]
    [InlineData("List1.AddItem \"inserted\", 0", 6)]
    [InlineData("List1.RemoveItem 0", 4)]
    [InlineData("List1.RemoveItem 5", -1)]
    public async Task Item_changes_preserve_selection_without_Click_or_Scroll(string statement, int selection)
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        await After(recorder, ctx, env, "List1.ListIndex = 5\r\n");
        recorder.Calls.Clear();
        (await After(recorder, ctx, env, statement + "\r\n")).Should().BeEmpty();
        recorder.List!.ListIndex.Should().Be(selection);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Selection_in_Form_Load_is_in_view_unless_TopIndex_follows(bool assignTop, bool insert)
    {
        // #4229: item 50 has TopIndex 40 on the measured 11-row list. Fit the host font as in Loaded.
        var (recorder, _, _) = await Loaded(SixtyItems);
        var height = (recorder.List!.Height * 15).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var source = FormWithList.Replace("1200", height) + "\r\nPrivate Sub Form_Load()\r\n"
            + SixtyItems + "List1.ListIndex = 50\r\n"
            + (insert ? "List1.AddItem \"inserted\", 0\r\n" : "")
            + (assignTop ? "List1.TopIndex = 20\r\n" : "") + "End Sub\r\n";
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source.ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form);
        using var cancel = new CancellationTokenSource();
        try
        {
            _ = VBLoader.RunForm(form, cancel.Token, out var window);
            Dispatcher.UIThread.RunJobs();
            window.IsVisible.Should().BeTrue();
            var list = window.GetVisualDescendants().OfType<VBListBox>().Single();
            list.ListIndex.Should().Be(insert ? 51 : 50);
            if (insert)
                list.TopIndex.Should().BeInRange(list.ListIndex - 10, list.ListIndex, "the selection still follows its item into view");
            else
                list.TopIndex.Should().Be(assignTop ? 20 : 40);
            if (!assignTop)
                list.ContainerFromIndex(list.ListIndex).Should().NotBeNull();
        }
        finally
        {
            cancel.Cancel();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string message) => throw new System.InvalidOperationException(message);
    }

    // #3986: code selection scrolls before Click, without Scroll.
    [AvaloniaTheory]
    [InlineData("List1.Selected(50) = True", 50)]
    [InlineData("List1.Text = \"item 45\"", 45)]
    [InlineData("List1.Text = \"item 05\"", 5)]
    public async Task Selected_and_Text_bring_item_into_view_with_Click_only(string assignment, int target)
    {
        var (recorder, ctx, env) = await Loaded("For i = 0 To 59\r\nList1.AddItem \"item \" & Format(i, \"00\")\r\nNext\r\n");
        await After(recorder, ctx, env, $"List1.ListIndex = {target}\r\n");
        var expectedTop = recorder.List!.TopIndex;
        await After(recorder, ctx, env, "List1.ListIndex = -1\r\nList1.TopIndex = 0\r\n");
        recorder.Calls.Clear();
        recorder.States.Clear();
        await After(recorder, ctx, env, assignment + "\r\n");
        recorder.List.ListIndex.Should().Be(target);
        recorder.List.TopIndex.Should().Be(expectedTop);
        recorder.Calls.Should().Equal($"List1_Click top={expectedTop}");
        recorder.States.Should().Equal($"List1_Click index={target} top={expectedTop}");
    }

    [AvaloniaTheory]
    [InlineData(-1)]
    [InlineData(60)]
    public async Task TopIndex_assignment_source_outside_the_list_raises_380_without_Scroll(int target)
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        var printed = await ContainerRuntimeHarness.Run(ctx, env,
            $"On Error Resume Next\r\nList1.TopIndex = {target}\r\nDebug.Print Err.Number\r\n");
        printed.Select(v => VbDisplay.ToText(v, System.Globalization.CultureInfo.InvariantCulture)).Should().Equal("380");
        recorder.Calls.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task TopIndex_assignment_source_last_item_clamps_to_last_page_and_repeat_raises_none()
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        var calls = await After(recorder, ctx, env, "List1.TopIndex = 59\r\n");
        calls.Should().Equal("List1_Scroll top=49");
        recorder.Calls.Clear();
        (await After(recorder, ctx, env, "List1.TopIndex = 59\r\n")).Should().BeEmpty();
    }
    [AvaloniaTheory]
    [InlineData(-1)]
    [InlineData(-2)]
    public async Task Wheel_source_moves_the_list_without_Scroll(int notches)
    {
        var (recorder, _, _) = await Loaded(SixtyItems);
        var list = recorder.List!;
        var window = (Window)TopLevel.GetTopLevel(list)!;
        var before = list.Scroll!.Offset.Y;
        window.MouseWheel(list.TranslatePoint(new Point(20, 20), window)!.Value, new Vector(0, notches));
        Dispatcher.UIThread.RunJobs();
        list.Scroll.Offset.Y.Should().BeGreaterThan(before);
        recorder.Calls.Should().NotContain(c => c.StartsWith("List1_Scroll", System.StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task ListIndex_assignment_source_scrolls_with_Click_only()
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        var before = recorder.List!.Scroll!.Offset.Y;
        var calls = await After(recorder, ctx, env, "List1.ListIndex = 50\r\n");
        calls.Should().ContainSingle().Which.Should().StartWith("List1_Click");
        recorder.List.Scroll.Offset.Y.Should().BeGreaterThan(before);
    }

    [AvaloniaTheory]
    [InlineData("Dim target As Long\r\ntarget = 5")]
    [InlineData("Dim target As Variant\r\ntarget = 5&")]
    public async Task TopIndex_assignment_uses_Integer_store_coercion(string declaration)
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        var calls = await After(recorder, ctx, env, declaration + "\r\nList1.TopIndex = target\r\n");
        calls.Should().Equal("List1_Scroll top=5");
    }

    [AvaloniaFact]
    public async Task TopIndex_assignment_Integer_store_overflow_raises_6_without_Scroll()
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        var printed = await ContainerRuntimeHarness.Run(ctx, env,
            "On Error Resume Next\r\nList1.TopIndex = 32768&\r\nDebug.Print Err.Number\r\nDebug.Print List1.TopIndex\r\n");
        printed.Select(v => VbDisplay.ToText(v, System.Globalization.CultureInfo.InvariantCulture)).Should().Equal("6", "0");
        recorder.Calls.Should().BeEmpty();
    }

    // #3986's supervisor comment records native/p-code measurements (oracle measuring PR #4021).
    [AvaloniaTheory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task TopIndex_in_hidden_and_multiselect_lists_uses_the_same_rows(int multiSelect, bool shown)
    {
        var form = FormWithList.Replace("Begin VB.ListBox List1",
            $"Begin VB.ListBox List1\n              MultiSelect = {multiSelect}");
        var (root, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(form);
        var list = (VBListBox)ContainerRuntimeHarness.Child(canvas, "List1");
        var recorder = new Recorder { Child = root, List = list };
        var window = new Window { Content = recorder, Width = 400, Height = 300 };
        ContainerRuntimeHarness.MergeAppResources(window);
        if (shown)
            window.Show();
        await ContainerRuntimeHarness.Run(ctx, env, "For i = 0 To 19\r\nList1.AddItem CStr(i)\r\nNext\r\n");
        list.TopIndex.Should().Be(0);
        // As in Loaded, fit whole rows using the host font rather than assuming its row height.
        var rowHeight = list.ContainerFromIndex(0)!.Bounds.Height;
        list.Height += 7 * rowHeight - list.Scroll!.Viewport.Height;
        if (!shown)
        {
            list.Measure(new Size(list.Width, list.Height));
            list.Arrange(new Rect(list.Bounds.Position, list.DesiredSize));
        }
        list.UpdateLayout();
        list.Scroll.Viewport.Height.Should().BeApproximately(7 * rowHeight, 0.01);
        if (multiSelect != 0)
            list.ListIndex.Should().Be(0);
        recorder.Calls.Clear();
        list.SetTopIndexFromCode(1);
        recorder.Calls.Should().Equal("List1_Scroll top=1");
        recorder.Calls.Clear();
        list.SetTopIndexFromCode(18);
        var lastPage = System.Math.Max(0, list.ItemCount - 7);
        list.TopIndex.Should().Be(lastPage);
        recorder.Calls.Should().Equal($"List1_Scroll top={lastPage}");
        recorder.Calls.Clear();
        list.SetTopIndexFromCode(18);
        foreach (var invalid in new[] { -1, 20 })
        {
            var printed = await ContainerRuntimeHarness.Run(ctx, env,
                $"On Error Resume Next\r\nList1.TopIndex = {invalid}\r\nDebug.Print Err.Number\r\n");
            printed.Select(v => VbDisplay.ToText(v, System.Globalization.CultureInfo.InvariantCulture)).Should().Equal("380");
            list.TopIndex.Should().Be(lastPage);
        }
        recorder.Calls.Should().BeEmpty();
        if (multiSelect != 0)
            recorder.ScrollListIndices.Should().Equal(0, 0);
        window.IsVisible.Should().Be(shown);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        list.TopIndex.Should().Be(lastPage);
        if (multiSelect != 0)
            list.ListIndex.Should().Be(0);
    }

}
