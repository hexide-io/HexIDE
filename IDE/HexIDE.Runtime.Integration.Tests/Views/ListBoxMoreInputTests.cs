using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using HexIDE.Integration.Tests;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

// #1424: docs/vb6-fidelity-oracle/a-listboxs-selected-selcount-and-text-with-multiselect-and-without.md,
// "More keys, Shift with a drag ...". SP6 native and p-code agree. Each case replays a table prefix.
public class ListBoxMoreInputTests : WindowTestFixture
{
    private sealed class Recorder : Decorator, IModuleExecutionRoot
    {
        internal int Clicks;
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
        {
            if (name == "List1_Click") Clicks++;
        }
    }

    private sealed record Running(Window Window, VBListBox List, Recorder Recorder,
        ModuleExecutionContext Ctx, ExecutionEnvironment Env)
    {
        private int offset;
        internal async Task Code(string code)
        {
            await ContainerRuntimeHarness.Run(Ctx, Env, code.ReplaceLineEndings("\r\n"));
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
        }
        internal void Click(int index, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            var point = Point(index);
            Window.MouseDown(point, MouseButton.Left, modifiers);
            Window.MouseUp(point, MouseButton.Left, modifiers);
            Dispatcher.UIThread.RunJobs();
        }
        internal Point Point(int index)
        {
            var item = List.ContainerFromIndex(index)!;
            return item.TranslatePoint(new Point(20 + 15 * (offset++ % 6), item.Bounds.Height / 2), Window)!.Value;
        }
        internal void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Window.KeyPress(key, modifiers, PhysicalKey.None, null);
            if (key >= Avalonia.Input.Key.A && key <= Avalonia.Input.Key.Z)
                Window.KeyTextInput(((char)('a' + key - Avalonia.Input.Key.A)).ToString());
            Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
        }
        internal void State(string selected, int focus, int? anchor = null)
        {
            string.Join(",", List.Selection.SelectedIndexes.Order()).Should().Be(selected);
            List.ListIndex.Should().Be(focus);
            // The oracle reads the native anchor directly too; no public API is added just to observe it.
            if (anchor is not null)
                typeof(VBListBox).GetField("anchorIndex", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(List).Should().Be(anchor.Value);
        }
    }

    private static async Task<Running> Start(int multi, bool keys = false)
    {
        var form = $$"""
            VERSION 5.00
            Begin VB.Form Form1
               Begin VB.ListBox List1
                  Left = 120
                  Top = 120
                  Width = 2400
                  Height = 3000
                  MultiSelect = {{multi}}
               End
            End
            Attribute VB_Name = "Form1"
            """;
        var (root, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(form);
        var list = (VBListBox)ContainerRuntimeHarness.Child(canvas, "List1");
        var recorder = new Recorder { Child = root };
        var window = new Window { Content = recorder, Width = 400, Height = 400 };
        ContainerRuntimeHarness.MergeAppResources(window);
        window.SetRenderScaling(keys ? 1 : 1.5);
        window.Show();
        var run = new Running(window, list, recorder, ctx, env);
        var items = keys
            ? "alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima mike november oscar mango quebec romeo sierra tango"
            : "a1 b1 c1 a2 b2 c2 d1 e1 f1 d2 e2 f2";
        await run.Code(string.Join("\n", items.Split(' ').Select(s => $"List1.AddItem \"{s}\"")));
        if (keys)
        {
            // Match the probe's six visible rows, independently of theme padding.
            var row = list.ContainerFromIndex(0)!.Bounds.Height;
            var viewport = list.Scroll!.Viewport.Height;
            list.Height -= viewport - 6 * row;
            window.UpdateLayout();
            Math.Floor(list.Scroll.Viewport.Height / row).Should().Be(6);
        }
        return run;
    }

    private static RawInputModifiers Command =>
        (RawInputModifiers)Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers;

    // A null modifier uses the platform's command modifier for the oracle's Ctrl rows.
    private static readonly (Key Key, RawInputModifiers? Mods, int Focus, string Selected, int Anchor)[] Keys =
    [
        (Key.Home, 0, 0, "0", 0),
        (Key.End, 0, 19, "19", 19),
        (Key.PageUp, 0, 14, "14", 14),
        (Key.PageUp, 0, 9, "9", 9),
        (Key.PageDown, 0, 14, "14", 14),
        (Key.M, 0, 15, "15", 15),
        (Key.M, 0, 12, "12", 12),
        (Key.D, 0, 3, "3", 3),
        (Key.Home, RawInputModifiers.Shift, 0, "0,1,2,3", 3),
        (Key.End, RawInputModifiers.Shift, 19, "3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19", 3),
        (Key.PageUp, RawInputModifiers.Shift, 14, "3,4,5,6,7,8,9,10,11,12,13,14", 3),
        (Key.Home, null, 0, "0", 0),
        (Key.End, null, 19, "19", 19),
        (Key.PageUp, null, 14, "14", 14),
        (Key.Space, 0, 14, "14", 14),
        (Key.K, RawInputModifiers.Shift, 10, "10,11,12,13,14", 14)
    ];

    public static IEnumerable<object[]> KeyRows() =>
        from multi in new[] { 1, 2 } from row in Enumerable.Range(0, Keys.Length) select new object[] { multi, row };

    [AvaloniaTheory]
    [MemberData(nameof(KeyRows))]
    public async Task Keys_match_each_oracle_row(int multi, int row)
    {
        var run = await Start(multi, true);
        run.Click(2);
        for (var i = 0; i <= row; i++) run.Key(Keys[i].Key, Keys[i].Mods ?? Command);
        var expected = Keys[row];
        run.State(multi == 2 ? expected.Selected : row >= 14 ? "2,14" : "2", expected.Focus,
            multi == 2 ? expected.Anchor : row >= 14 ? 14 : null);
        run.Recorder.Clicks.Should().Be(row + 2);
        if (row == 1) run.List.TopIndex.Should().Be(14); // End's measured top row on both six-row lists.
        // Check the anchor through input too: the measured Shift rule ranges from it and leaves it there.
        if (multi == 2)
        {
            run.Key(Key.Home, RawInputModifiers.Shift);
            run.State(string.Join(",", Enumerable.Range(0, expected.Anchor + 1)), 0, expected.Anchor);
        }
    }

    [AvaloniaTheory]
    [InlineData(2, 3, 6, false, "1,2,3,4,5,6", 1)]
    [InlineData(2, 6, 3, false, "1,2,3", 1)]
    [InlineData(2, 5, 8, true, "3,4,5,6,7,8", 3)]
    [InlineData(1, 3, 6, false, "1,3", 3)]
    public async Task Shift_drag_uses_the_anchor_not_the_pressed_item(int multi, int from, int to, bool ctrl,
        string selected, int anchor)
    {
        var run = await Start(multi);
        run.Click(1);
        if (ctrl) run.Click(3, Command);
        run.Window.MouseDown(run.Point(from), MouseButton.Left, RawInputModifiers.Shift);
        var end = run.Point(to);
        run.Window.MouseMove(end, RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift);
        run.Window.MouseUp(end, MouseButton.Left, RawInputModifiers.Shift);
        run.State(selected, to, anchor);
    }

    [AvaloniaTheory]
    [InlineData("0,8,5", 3, true, "0,3,4,5,8", 5)]
    [InlineData("0,8,5", 3, false, "3,4,5", 5)]
    [InlineData("0,2,6", 8, true, "0,2,6,7,8", 6)]
    [InlineData("6,1,6", 9, true, "1", 6)]
    public async Task Ctrl_Shift_click_keeps_outside_selection_and_uses_the_anchor_state(
        string clicks, int target, bool ctrl, string selected, int anchor)
    {
        var run = await Start(2);
        var indexes = clicks.Split(',').Select(int.Parse).ToArray();
        run.Click(indexes[0]);
        foreach (var index in indexes.Skip(1)) run.Click(index, Command);
        run.Click(target, RawInputModifiers.Shift | (ctrl ? Command : 0));
        run.State(selected, target, anchor);
    }

    [AvaloniaTheory]
    [InlineData(0, "1,5", 5, 5)]
    [InlineData(1, "5,6", 6, 5)]
    [InlineData(2, "5,6,7,8", 8, 5)]
    [InlineData(3, "", 1, 1)]
    [InlineData(4, "2,3,4", 4, 1)]
    [InlineData(5, "3,7,9", 9, 9)]
    [InlineData(6, "10", 10, 10)]
    public async Task Selected_from_code_moves_focus_and_anchor_only_on_True(int row, string selected, int focus, int anchor)
    {
        var run = await Start(2);
        run.Click(row >= 5 ? 3 : 1);
        await run.Code(row >= 5 ? "List1.Selected(7) = True\nList1.Selected(9) = True" :
            row >= 3 ? "List1.Selected(1) = False" : "List1.Selected(5) = True");
        if (row == 1) run.Key(Key.Down, RawInputModifiers.Shift);
        if (row == 2) run.Click(8, RawInputModifiers.Shift);
        if (row == 4) run.Click(4, RawInputModifiers.Shift);
        if (row == 6) run.Key(Key.Down);
        run.State(selected, focus, anchor);
    }

    public static IEnumerable<object[]> EditRows() =>
        from multi in new[] { 0, 1, 2 } from row in Enumerable.Range(0, 5) select new object[] { multi, row };

    [AvaloniaTheory]
    [MemberData(nameof(EditRows))]
    public async Task Items_keep_selection_and_move_focus_as_measured(int multi, int row)
    {
        var run = await Start(multi);
        run.Click(4);
        run.Recorder.Clicks = 0;
        await run.Code("List1.AddItem \"zz\", 1");
        if (row >= 1) await run.Code("List1.RemoveItem 0");
        if (row >= 2) await run.Code("List1.AddItem \"yy\"");
        if (row < 3)
        {
            var index = row == 0 ? 5 : 4;
            run.State(index.ToString(), multi == 0 ? index : 4, multi == 0 ? null : row == 0 ? 4 : -1);
        }
        else
        {
            await run.Code("List1.RemoveItem 4");
            if (row == 4)
            {
                run.Recorder.Clicks.Should().Be(0);
                run.Click(11);
                run.Recorder.Clicks = 0;
                await run.Code("List1.RemoveItem 11");
            }
            run.State("", multi == 0 ? -1 : row == 3 ? 3 : 10, multi == 0 ? null : -1);
        }
        run.Recorder.Clicks.Should().Be(0);
    }

    [AvaloniaTheory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task List_replacement_preserves_selection_and_moves_multi_focus_and_anchor(int multi, bool two)
    {
        var run = await Start(multi);
        if (two) run.Click(2);
        run.Click(4, two ? Command : 0);
        run.Recorder.Clicks = 0;
        await run.Code($"List1.List({(two ? 2 : 4)}) = \"{(two ? "new2" : "new")}\"");
        run.State(two ? "2,4" : "4", two ? 2 : 4, multi == 0 ? null : two ? 2 : 4);
        run.Recorder.Clicks.Should().Be(0);
    }
}
