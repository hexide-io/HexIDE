using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using HexIDE.Runtime;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class FlexGridKeyboardTests
{
    private sealed class Root : Window, IModuleExecutionRoot, IDisposable
    {
        public VBMSFlexGrid Grid { get; } = new() { Width = 333, Height = 200 };
        public Button Before { get; } = new();
        public Button After { get; } = new();
        public List<string> Log { get; } = [];
        public Root()
        {
            Content = new StackPanel { Children = { Before, Grid, After } };
            VBProps.SetName(Grid, "Grid1");
            Grid.Model.Rows = 30;
            Grid.Model.Cols = 6;
            KeepingContext(() => { Show(); Grid.Focus(); });
        }
        public bool Handles(string name) => true;
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) =>
            Log.Add(name == "Grid1_Scroll" ? "Scroll" : $"{name[6..]}:{Grid.Model.Row},{Grid.Model.Col}:{Grid.Model.RowSel},{Grid.Model.ColSel}"
                + (args is { Count: > 0 } ? ":" + string.Join(",", args.Select(a => a.Value)) : ""));
        public void Key(Key key, RawInputModifiers mods = RawInputModifiers.None, string? text = null) => KeepingContext(() =>
        {
            this.KeyPress(key, mods, PhysicalKey.None, null);
            if (text is not null) this.KeyTextInput(text);
            this.KeyRelease(key, mods, PhysicalKey.None, null);
        });
        public void Dispose() => KeepingContext(Close);
    }

    private static void KeepingContext(Action action)
    {
        var context = ExecutionContext.Capture();
        try { action(); }
        finally { if (context is not null) ExecutionContext.Restore(context); }
    }

    // The oracle's keyboard table, native and p-code agreeing (#1099). KeyUp is pinned per key.
    [AvaloniaTheory]
    [InlineData(Key.Down, 3, 2)]
    [InlineData(Key.Up, 1, 2)]
    [InlineData(Key.Left, 2, 1)]
    [InlineData(Key.Right, 2, 3)]
    [InlineData(Key.Home, 2, 1, 36)]
    [InlineData(Key.End, 2, 5, 35)]
    public void Navigation_has_SelChange_before_EnterCell_and_only_the_recorded_KeyUp(Key key, int row, int col, int keyUp = 0)
    {
        using var root = new Root();
        root.Grid.Model.Row = 2;
        root.Grid.Model.Col = 2;
        root.Log.Clear();
        root.Key(key);
        var expected = new List<string> { "LeaveCell:2,2:2,2", $"SelChange:{row},{col}:{row},{col}",
            $"EnterCell:{row},{col}:{row},{col}", $"RowColChange:{row},{col}:{row},{col}" };
        if (key == Key.End) expected.Insert(0, "Scroll");
        if (keyUp != 0) expected.Add($"KeyUp:{row},{col}:{row},{col}:{keyUp},0");
        root.Log.Should().Equal(expected);
    }

    [AvaloniaFact]
    public void Right_scrolls_before_any_cell_event_when_the_column_is_not_wholly_visible()
    {
        using var root = new Root();
        root.Grid.Model.Col = 3;
        root.Log.Clear();
        root.Key(Key.Right);
        root.Grid.Model.LeftCol.Should().Be(2);
        root.Log.Should().Equal("Scroll", "LeaveCell:1,3:1,3", "SelChange:1,4:1,4",
            "EnterCell:1,4:1,4", "RowColChange:1,4:1,4");
    }

    [AvaloniaFact]
    // Control is the measured VB6 modifier here; this is not a platform command shortcut.
    public void Control_End_and_Home_scroll_rows_then_columns_before_moving_both_coordinates()
    {
        using var root = new Root();
        root.Key(Key.End, RawInputModifiers.Control);
        root.Grid.Model.TopRow.Should().BeGreaterThan(1);
        root.Grid.Model.LeftCol.Should().BeGreaterThan(1);
        root.Log.Should().Equal("Scroll", "Scroll", "LeaveCell:1,1:1,1",
            "SelChange:29,5:29,5", "EnterCell:29,5:29,5", "RowColChange:29,5:29,5", "KeyUp:29,5:29,5:35,2");
        root.Log.Clear();
        root.Key(Key.Home, RawInputModifiers.Control);
        root.Grid.Model.TopRow.Should().Be(1);
        root.Grid.Model.LeftCol.Should().Be(1);
        root.Log.Should().Equal("Scroll", "Scroll", "LeaveCell:29,5:29,5",
            "SelChange:1,1:1,1", "EnterCell:1,1:1,1", "RowColChange:1,1:1,1", "KeyUp:1,1:1,1:36,2");
    }

    [AvaloniaFact]
    public void Page_keys_move_a_page_and_scroll_before_the_cell_events()
    {
        using var root = new Root();
        root.Key(Key.PageDown);
        root.Grid.Model.TopRow.Should().Be(2);
        root.Log.Should().Equal("Scroll", "LeaveCell:1,1:1,1", "SelChange:11,1:11,1",
            "EnterCell:11,1:11,1", "RowColChange:11,1:11,1", "KeyUp:11,1:11,1:34,0");
        root.Log.Clear();
        root.Key(Key.PageUp);
        root.Grid.Model.TopRow.Should().Be(1);
        root.Log.Should().Equal("Scroll", "LeaveCell:11,1:11,1", "SelChange:1,1:1,1",
            "EnterCell:1,1:1,1", "RowColChange:1,1:1,1", "KeyUp:1,1:1,1:33,0");
    }

    [AvaloniaTheory]
    [InlineData(Key.Down, 2, 1, 40)]
    [InlineData(Key.Right, 1, 2, 39)]
    public void Shift_arrows_extend_the_selection_with_only_SelChange_and_KeyUp(Key key, int rowSel, int colSel, int code)
    {
        using var root = new Root();
        root.Key(key, RawInputModifiers.Shift);
        root.Log.Should().Equal($"SelChange:1,1:{rowSel},{colSel}", $"KeyUp:1,1:{rowSel},{colSel}:{code},1");
    }

    [AvaloniaTheory]
    [InlineData(Key.Enter, "\r", 13)]
    [InlineData(Key.A, "a", 65)]
    public void Enter_and_a_letter_raise_key_events_without_editing_or_moving(Key key, string text, int code)
    {
        using var root = new Root();
        root.Grid.Model.Text = "unchanged";
        root.Key(key, text: text);
        root.Log.Should().Equal($"KeyDown:1,1:1,1:{code},0", $"KeyPress:1,1:1,1:{(int)text[0]}",
            $"KeyUp:1,1:1,1:{code},0");
        root.Grid.Model.Text.Should().Be("unchanged");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tab_leaves_the_grid_and_the_opposite_Tab_returns(bool backwards)
    {
        using var root = new Root();
        root.Key(Key.Tab, backwards ? RawInputModifiers.Shift : RawInputModifiers.None);
        (backwards ? root.Before : root.After).IsFocused.Should().BeTrue();
        root.Key(Key.Tab, backwards ? RawInputModifiers.None : RawInputModifiers.Shift);
        root.Grid.IsFocused.Should().BeTrue();
        root.Grid.Model.Row.Should().Be(1);
        root.Grid.Model.Col.Should().Be(1);
    }

    [AvaloniaTheory]
    [InlineData(Key.Up, RawInputModifiers.None)]
    [InlineData(Key.Down, RawInputModifiers.Control)]
    public void Unmeasured_navigation_modifiers_refuse(Key key, RawInputModifiers mods)
    {
        using var root = new Root();
        var act = () => root.Key(key, mods);
        act.Should().Throw<NotImplementedException>();
        root.Grid.Model.Row.Should().Be(1);
        root.Grid.Model.Col.Should().Be(1);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Keyboard_selection_without_a_nonfixed_current_cell_refuses(bool empty)
    {
        using var root = new Root();
        if (empty) root.Grid.Model.Rows = 0;
        else root.Grid.Model.Row = 0;
        Action input = () => root.Key(Key.Down);
        input.Should().Throw<NotImplementedException>();
    }

    [AvaloniaFact]
    public void Text_outside_the_recorded_letter_refuses()
    {
        using var root = new Root();
        Action input = () => root.Key(Key.B, text: "b");
        input.Should().Throw<NotImplementedException>();
    }

    [AvaloniaFact]
    public void Unmeasured_input_is_reported_as_a_run_fault_even_without_a_key_handler()
    {
        var grid = new VBMSFlexGrid { Width = 333, Height = 200 };
        grid.Model.Row = 0;
        var window = new VBFormRuntime { Content = grid };
        var faults = new List<Exception>();
        void Faulted(VBWindowContext context, Exception error)
        {
            if (ReferenceEquals(context, window.Context)) faults.Add(error);
        }
        VBWindowContext.Faulted += Faulted;
        try
        {
            KeepingContext(() => { window.Show(); grid.Focus(); });
            Action input = () => KeepingContext(() => window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null));
            input.Should().NotThrow("an unsupported grid input must stop the run, not escape Avalonia's input dispatch");
            KeepingContext(() => Avalonia.Threading.Dispatcher.UIThread.RunJobs());
            faults.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>();
            grid.Model.Row.Should().Be(0);
        }
        finally
        {
            VBWindowContext.Faulted -= Faulted;
            KeepingContext(window.Close);
        }
    }

    [AvaloniaTheory]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(2, false)]
    public void Row_and_column_modes_follow_the_measured_keyboard_sequence(int mode, bool big)
    {
        using var root = new Root();
        var model = root.Grid.Model;
        FlexGridMembers.Write(root.Grid, "SelectionMode", [], new Vb6Value(mode));
        FlexGridMembers.Read(root.Grid, "SelectionMode", []).Value.Should().Be(mode);
        model.AllowBigSelection = big;
        void Start()
        {
            root.Grid.SetFirstCell(1, 1);
            model.Row = 3;
            model.Col = 2;
            model.RowSel = 3;
            model.ColSel = 2;
            root.Log.Clear();
        }
        void Check(Key key, RawInputModifiers mods, int row, int col, int endRow, int endCol, int changes)
        {
            root.Log.Clear();
            root.Key(key, mods);
            (model.Row, model.Col, model.RowSel, model.ColSel).Should().Be((row, col, endRow, endCol));
            root.Log.Count(e => e.StartsWith("SelChange:", StringComparison.Ordinal)).Should().Be(changes);
            if (mode == 1) model.LeftCol.Should().Be(1);
            else model.TopRow.Should().Be(1);
        }
        Start();
        (model.RowSel, model.ColSel).Should().Be((3, 2), "code selection is not forced to the mode");
        Check(Key.Down, RawInputModifiers.Shift, 3, 2, mode == 1 ? 4 : 29, mode == 1 ? 5 : 2, 1);
        Check(Key.Down, RawInputModifiers.Shift, 3, 2, mode == 1 ? 5 : 29, mode == 1 ? 5 : 2, mode == 1 ? 1 : 0);
        Check(Key.Right, RawInputModifiers.Shift, 3, 2, mode == 1 ? 5 : 29, mode == 1 ? 5 : 3, mode == 1 ? 0 : 1);
        for (var r = 4; r >= 2; r--)
            Check(Key.Up, RawInputModifiers.Shift, 3, 2, mode == 1 ? r : 29, mode == 1 ? 5 : 3, mode == 1 ? 1 : 0);
        Check(Key.Left, RawInputModifiers.Shift, 3, 2, mode == 1 ? 2 : 29, mode == 1 ? 5 : 2, mode == 1 ? 0 : 1);
        Check(Key.Left, RawInputModifiers.Shift, 3, 2, mode == 1 ? 2 : 29, mode == 1 ? 5 : 1, mode == 1 ? 0 : 1);
        Start();
        Check(Key.Home, RawInputModifiers.Shift, 3, 2, mode == 1 ? 3 : 29, mode == 1 ? 5 : 1, 1);
        Check(Key.End, RawInputModifiers.Shift, 3, 2, mode == 1 ? 3 : 29, 5, mode == 1 ? 0 : 1);
        Check(Key.Home, RawInputModifiers.Shift, 3, 2, mode == 1 ? 3 : 29, mode == 1 ? 5 : 1, mode == 1 ? 0 : 1);
        Start();
        Check(Key.PageDown, RawInputModifiers.Shift, 3, 2, mode == 1 ? 13 : 29, mode == 1 ? 5 : 2, 1);
        if (mode == 1) model.TopRow.Should().Be(4);
        Check(Key.PageUp, RawInputModifiers.Shift, 3, 2, mode == 1 ? 3 : 29, mode == 1 ? 5 : 2, mode == 1 ? 1 : 0);
        if (mode == 1) model.TopRow.Should().Be(3);
        Start();
        Check(Key.Home, RawInputModifiers.Control, mode == 1 ? 1 : 3, mode == 1 ? 2 : 1,
            mode == 1 ? 1 : 29, mode == 1 ? 5 : 1, 2);
        Check(Key.End, RawInputModifiers.Control, mode == 1 ? 29 : 3, mode == 1 ? 2 : 5, 29, 5, 2);
        if (mode == 1) model.TopRow.Should().Be(20);
        else model.LeftCol.Should().Be(3);
        Start();
        Check(Key.End, RawInputModifiers.Control | RawInputModifiers.Shift, 3, 2, 29, 5, 1);
        Check(Key.Home, RawInputModifiers.Control | RawInputModifiers.Shift, 3, 2, mode == 1 ? 1 : 29, mode == 1 ? 5 : 1, 1);
        Start();
        Check(Key.Down, RawInputModifiers.None, mode == 1 ? 4 : 3, 2, mode == 1 ? 4 : 29, mode == 1 ? 5 : 2, mode == 1 ? 2 : 1);
        Check(Key.Right, RawInputModifiers.None, mode == 1 ? 4 : 3, mode == 1 ? 2 : 3, mode == 1 ? 4 : 29, mode == 1 ? 5 : 3, 2);
        Check(Key.Home, RawInputModifiers.None, mode == 1 ? 4 : 3, mode == 1 ? 2 : 1, mode == 1 ? 4 : 29, mode == 1 ? 5 : 1, 2);
        Check(Key.End, RawInputModifiers.None, mode == 1 ? 4 : 3, mode == 1 ? 2 : 5, mode == 1 ? 4 : 29, 5, 2);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public void Nondefault_mode_mouse_selection_remains_refused(int mode)
    {
        using var root = new Root();
        FlexGridMembers.Write(root.Grid, "SelectionMode", [], new Vb6Value(mode));
        var point = root.Grid.TranslatePoint(new Point(148, 40), root)!.Value;
        Action click = () => KeepingContext(() => root.MouseDown(point, MouseButton.Left));
        click.Should().Throw<NotImplementedException>().WithMessage("*SelectionMode*#1099*");
        (root.Grid.Model.Row, root.Grid.Model.Col).Should().Be((1, 1));
    }

    [AvaloniaFact]
    public void SelectionMode_keeps_code_selection_and_ignores_the_measured_invalid_enum_values()
    {
        using var root = new Root();
        root.Grid.Model.Row = 3;
        root.Grid.Model.Col = 2;
        root.Log.Clear();
        foreach (var mode in new[] { 1, 2, 0 })
        {
            FlexGridMembers.Write(root.Grid, "SelectionMode", [], new Vb6Value(mode));
            foreach (var invalid in new[] { -1, 5 })
            {
                FlexGridMembers.Write(root.Grid, "SelectionMode", [], new Vb6Value(invalid));
                FlexGridMembers.Read(root.Grid, "SelectionMode", []).Value.Should().Be(mode);
            }
            (root.Grid.Model.Row, root.Grid.Model.Col, root.Grid.Model.RowSel, root.Grid.Model.ColSel)
                .Should().Be((3, 2, 3, 2));
        }
        root.Log.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(1, Key.Up)]
    [InlineData(1, Key.PageDown)]
    [InlineData(1, Key.PageUp)]
    [InlineData(2, Key.Up)]
    [InlineData(2, Key.Down)]
    [InlineData(2, Key.PageDown)]
    [InlineData(2, Key.PageUp)]
    public void Modes_refuse_unmeasured_plain_vertical_event_counts(int mode, Key key)
    {
        using var root = new Root();
        FlexGridMembers.Write(root.Grid, "SelectionMode", [], new Vb6Value(mode));
        root.Grid.Model.Row = 3;
        root.Grid.Model.Col = 2;
        root.Key(Key.Down, RawInputModifiers.Shift);
        root.Log.Clear();
        Action input = () => root.Key(key);
        input.Should().Throw<NotImplementedException>().WithMessage("*SelectionMode*#1099*");
        root.Log.Should().BeEmpty();
        (root.Grid.Model.Row, root.Grid.Model.Col, root.Grid.Model.RowSel, root.Grid.Model.ColSel)
            .Should().Be(mode == 1 ? (3, 2, 4, 5) : (3, 2, 29, 2));
    }

    [AvaloniaFact]
    public void Column_mode_refuses_an_unmeasured_plain_horizontal_key_that_moves_nothing()
    {
        using var root = new Root();
        FlexGridMembers.Write(root.Grid, "SelectionMode", [], new Vb6Value(2));
        root.Grid.Model.Row = 3;
        root.Log.Clear();
        Action input = () => root.Key(Key.Home);
        input.Should().Throw<NotImplementedException>().WithMessage("*SelectionMode*#1099*");
        root.Log.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(Key.Up, RawInputModifiers.Shift, 2, 2)]
    [InlineData(Key.Left, RawInputModifiers.Shift, 3, 1)]
    [InlineData(Key.Home, RawInputModifiers.Shift, 3, 1)]
    [InlineData(Key.End, RawInputModifiers.Shift, 3, 5)]
    [InlineData(Key.PageDown, RawInputModifiers.Shift, 13, 2)]
    [InlineData(Key.PageUp, RawInputModifiers.Shift, 3, 2)]
    [InlineData(Key.End, RawInputModifiers.Control | RawInputModifiers.Shift, 29, 5)]
    [InlineData(Key.Home, RawInputModifiers.Control | RawInputModifiers.Shift, 1, 1)]
    public void Free_selection_extends_from_its_endpoint_and_keeps_its_anchor(Key key, RawInputModifiers mods, int row, int col)
    {
        using var root = new Root();
        root.Grid.Model.Row = 3;
        root.Grid.Model.Col = 2;
        if (key == Key.PageUp) root.Key(Key.PageDown, RawInputModifiers.Shift);
        root.Log.Clear();
        root.Key(key, mods);
        root.Grid.Model.Row.Should().Be(3);
        root.Grid.Model.Col.Should().Be(2);
        root.Grid.Model.RowSel.Should().Be(row);
        root.Grid.Model.ColSel.Should().Be(col);
        root.Log.Where(e => e.StartsWith("SelChange:", StringComparison.Ordinal)).Should().Equal($"SelChange:3,2:{row},{col}");
        if (key == Key.PageDown) root.Grid.Model.TopRow.Should().Be(4);
        if (key == Key.PageUp) root.Grid.Model.TopRow.Should().Be(3);
    }

    [AvaloniaFact]
    public void Free_selection_crosses_the_anchor_and_a_plain_key_collapses_it()
    {
        using var root = new Root();
        root.Grid.Model.Row = 3;
        root.Grid.Model.Col = 2;
        root.Key(Key.Down, RawInputModifiers.Shift);
        root.Key(Key.Down, RawInputModifiers.Shift);
        root.Key(Key.Right, RawInputModifiers.Shift);
        for (var i = 0; i < 3; i++) root.Key(Key.Up, RawInputModifiers.Shift);
        root.Grid.Model.RowSel.Should().Be(2);
        root.Grid.Model.ColSel.Should().Be(3);
        root.Key(Key.Down);
        root.Grid.Model.Row.Should().Be(4);
        root.Grid.Model.RowSel.Should().Be(4);
        root.Grid.Model.ColSel.Should().Be(2);
    }

    [AvaloniaFact]
    public void AllowBigSelection_false_does_not_disable_keyboard_selection()
    {
        using var root = new Root();
        root.Grid.Model.AllowBigSelection = false;
        root.Key(Key.Down);
        root.Key(Key.Right, RawInputModifiers.Shift);
        root.Grid.Model.Row.Should().Be(2);
        root.Grid.Model.Col.Should().Be(1);
        root.Grid.Model.RowSel.Should().Be(2);
        root.Grid.Model.ColSel.Should().Be(2);
    }
}
