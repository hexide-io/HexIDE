using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Rendering;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.BuiltinControls;

public sealed partial class VBMSFlexGrid : Control, ICustomHitTest
{
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    internal FlexGridModel Model { get; } = new();
    private static readonly EventClass SelChange = new("SelChange");
    private bool selectionChanged;
    private bool secondPress;
    private (int Row, int Col)? pressedCell;

    public VBMSFlexGrid()
    {
        Focusable = true;
        Model.CellEvent = name =>
        {
            if (name == "RowColChange") RefreshView(true);
            this.ExecuteSub(new EventClass(name));
        };
        InitializeDrawing();
    }

    static VBMSFlexGrid()
    {
        // Selection changes before MouseDown, but SelChange follows it (#1099).
        PointerPressedEvent.AddClassHandler<VBMSFlexGrid>((grid, e) => grid.SelectAtPress(e));
        AttachedEvents.AttachMouse<VBMSFlexGrid>(AttachedEvents.DblClick.InPlaceOfMouseDown, mouseMove: false);
        PointerPressedEvent.AddClassHandler<VBMSFlexGrid>((grid, _) => grid.FinishPress());
        AttachedEvents.AttachClick<VBMSFlexGrid>();
    }

    private void SelectAtPress(PointerPressedEventArgs e)
    {
        selectionChanged = false;
        pressedCell = null;
        secondPress = e.ClickCount % 2 == 0;
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        if (CellAt(e.GetPosition(this)) is not { } cell) return;
        if (Model.SelectionMode != 0)
        {
            RefuseInput(AvaloniaInterop.FlexGridMembers.Refused(nameof(Model.SelectionMode)));
            return;
        }
        pressedCell = cell;
        if (!secondPress)
            selectionChanged = Model.SelectMouseCell(cell.Row, cell.Col, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
    }

    private void FinishPress()
    {
        if (pressedCell is not { } cell) return;
        if (Model.AllowBigSelection)
        {
            if (cell.Row < Model.FixedRows)
            {
                selectionChanged |= Model.RowSel != Model.Rows - 1;
                Model.RowSel = Model.Rows - 1;
            }
            if (cell.Col < Model.FixedCols)
            {
                selectionChanged |= Model.ColSel != Model.Cols - 1;
                Model.ColSel = Model.Cols - 1;
            }
        }
        if (selectionChanged || secondPress) this.ExecuteSub(SelChange);
        InvalidateVisual();
    }

    private (int Row, int Col)? CellAt(Point point)
    {
        foreach (var cell in VisibleCells())
            if (cell.Bounds.Contains(point)) return (cell.Row, cell.Col);
        return null;
    }
}
