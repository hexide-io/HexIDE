using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class FlexGridMouseTests
{
    private sealed class Root : Window, IModuleExecutionRoot, System.IDisposable
    {
        public VBMSFlexGrid Grid { get; } = new() { Width = 400, Height = 200 };
        public List<string> Log { get; } = [];
        public Root()
        {
            Content = Grid;
            VBProps.SetName(Grid, "Grid1");
            Grid.Model.Rows = 30;
            Grid.Model.Cols = 6;
            KeepingContext(Show);
        }
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) =>
            Log.Add($"{name[6..]}:{Grid.Model.Row},{Grid.Model.Col}:{Grid.Model.RowSel},{Grid.Model.ColSel}"
                + (args is { Count: > 0 } ? $":{args[0].Value}" : ""));
        public void Dispose() => KeepingContext(Close);
        public bool Handles(string name) => !name.EndsWith("MouseMove", System.StringComparison.Ordinal);
        public Point At(int row, int col) => Grid.TranslatePoint(new Point(col * 64 + 20, row * 16 + 8), this)!.Value;
        public void Click(int row, int col, MouseButton button = MouseButton.Left, RawInputModifiers mods = RawInputModifiers.None)
        {
            var point = At(row, col);
            KeepingContext(() =>
            {
                this.MouseDown(point, button, mods);
                this.MouseUp(point, button, mods);
            });
        }
    }

    // Headless input pumps dispatcher jobs, which can replace xUnit's ExecutionContext (#2194).
    private static void KeepingContext(System.Action action)
    {
        var context = ExecutionContext.Capture();
        try { action(); }
        finally { if (context is not null) ExecutionContext.Restore(context); }
    }

    [AvaloniaFact]
    public void Scrolling_preserves_headings_and_maps_mouse_input_to_visible_cells()
    {
        using var root = new Root();
        FlexGridMembers.Write(root.Grid, "Cols", [], new Vb6Value(10));
        KeepingContext(() => Avalonia.Threading.Dispatcher.UIThread.RunJobs());
        FlexGridMembers.Write(root.Grid, "TopRow", [], new Vb6Value(10));
        FlexGridMembers.Write(root.Grid, "LeftCol", [], new Vb6Value(3));
        root.Log.Should().Equal("Scroll:1,1:1,1", "Scroll:1,1:1,1");
        root.Click(2, 2);
        root.Grid.Model.Row.Should().Be(11);
        root.Grid.Model.Col.Should().Be(4);
        root.Click(0, 2);
        root.Grid.Model.Row.Should().Be(1, "a fixed column heading stays in the fixed row");
        root.Grid.Model.Col.Should().Be(4);
    }

    [AvaloniaFact]
    public void Click_moves_both_coordinates_before_MouseDown_and_changes_selection_after_it()
    {
        using var root = new Root();
        root.Click(2, 2);
        root.Log.Should().Equal("LeaveCell:1,1:1,1", "EnterCell:2,2:2,2", "RowColChange:2,2:2,2",
            "MouseDown:2,2:2,2:1", "SelChange:2,2:2,2", "MouseUp:2,2:2,2:1", "Click:2,2:2,2");
    }

    [AvaloniaFact]
    public void Clicking_current_cell_has_no_selection_events()
    {
        using var root = new Root();
        root.Click(1, 1);
        root.Log.Should().Equal("MouseDown:1,1:1,1:1", "MouseUp:1,1:1,1:1", "Click:1,1:1,1");
    }

    [AvaloniaFact]
    public void Shift_click_extends_before_MouseDown_without_moving_current_cell()
    {
        using var root = new Root();
        root.Click(4, 3, mods: RawInputModifiers.Shift);
        root.Log.Should().Equal("MouseDown:1,1:4,3:1", "SelChange:1,1:4,3", "MouseUp:1,1:4,3:1", "Click:1,1:4,3");
    }

    [AvaloniaFact]
    public void Control_click_is_a_plain_click()
    {
        using var root = new Root();
        root.Click(3, 1, mods: RawInputModifiers.Control);
        root.Grid.Model.Row.Should().Be(3);
        root.Grid.Model.RowSel.Should().Be(3);
        root.Log.Should().Contain("SelChange:3,1:3,1");
    }

    [AvaloniaFact]
    public void Right_click_raises_Click_without_moving_the_cell()
    {
        using var root = new Root();
        root.Click(5, 2, MouseButton.Right);
        root.Log.Should().Equal("MouseDown:1,1:1,1:2", "MouseUp:1,1:1,1:2", "Click:1,1:1,1");
    }

    [AvaloniaFact]
    public void AllowBigSelection_false_keeps_a_heading_click_to_one_cell()
    {
        using var root = new Root();
        FlexGridMembers.Read(root.Grid, "AllowBigSelection", []).Value.Should().Be(true);
        FlexGridMembers.Write(root.Grid, "AllowBigSelection", [], new Vb6Value(false));
        FlexGridMembers.Read(root.Grid, "AllowBigSelection", []).Value.Should().Be(false);
        root.Click(0, 3);
        root.Grid.Model.RowSel.Should().Be(1);
        root.Grid.Model.ColSel.Should().Be(3);
        root.Log.Should().Contain("SelChange:1,3:1,3");
    }

    [AvaloniaFact]
    public void Double_click_second_press_has_DblClick_and_SelChange_but_no_MouseDown_or_Click()
    {
        using var root = new Root();
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var point = root.At(2, 3);
        for (var count = 1; count <= 2; count++)
        {
            root.Grid.RaiseEvent(new PointerPressedEventArgs(root.Grid, pointer, root, point, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None, count));
            root.Grid.RaiseEvent(new PointerReleasedEventArgs(root.Grid, pointer, root, point, 1,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
                KeyModifiers.None, MouseButton.Left));
        }
        root.Log.Should().Equal("LeaveCell:1,1:1,1", "EnterCell:2,3:2,3", "RowColChange:2,3:2,3",
            "MouseDown:2,3:2,3:1", "SelChange:2,3:2,3", "MouseUp:2,3:2,3:1", "Click:2,3:2,3",
            "DblClick:2,3:2,3", "SelChange:2,3:2,3", "MouseUp:2,3:2,3:1");
    }

    [AvaloniaTheory]
    [InlineData(0, 2, 1, 2, 29, 2)]
    [InlineData(3, 0, 3, 1, 3, 5)]
    [InlineData(0, 0, 1, 1, 29, 5)]
    public void Headings_select_nonfixed_cells(int row, int col, int currentRow, int currentCol, int lastRow, int lastCol)
    {
        using var root = new Root();
        root.Click(row, col);
        root.Grid.Model.Row.Should().Be(currentRow);
        root.Grid.Model.Col.Should().Be(currentCol);
        root.Grid.Model.RowSel.Should().Be(lastRow);
        root.Grid.Model.ColSel.Should().Be(lastCol);
        root.Log.Should().Contain($"MouseDown:{currentRow},{currentCol}:{currentRow},{currentCol}:1");
        root.Log.Should().Contain($"SelChange:{currentRow},{currentCol}:{lastRow},{lastCol}");
    }
}
