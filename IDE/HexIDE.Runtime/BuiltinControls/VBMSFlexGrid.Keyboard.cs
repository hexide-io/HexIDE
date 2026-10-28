using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Avalonia.LogicalTree;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public sealed partial class VBMSFlexGrid
{
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !ReferenceEquals(e.Source, this) || e.Key == Key.Tab) return;
        try { TakeKey(e); }
        catch (NotImplementedException error)
        {
            e.Handled = true;
            RefuseInput(error);
        }
    }

    private void TakeKey(KeyEventArgs e)
    {
        var navigation = e.Key is Key.Down or Key.Up or Key.Left or Key.Right or Key.Home or Key.End or Key.PageDown or Key.PageUp;
        if (!navigation)
        {
            if (e.Key is Key.Enter or Key.A && e.KeyModifiers == KeyModifiers.None)
            {
                RaiseKey(ComponentBaseClass.KeyDownEvent, e.Key == Key.Enter ? 13 : 65, 0);
                if (e.Key == Key.Enter) RaiseKey(ComponentBaseClass.KeyPressEvent, 13);
                e.Handled = true;
            }
            return;
        }
        var extend = e.KeyModifiers == KeyModifiers.Shift
            || e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key is Key.Home or Key.End;
        var control = (e.KeyModifiers == KeyModifiers.Control || e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
            && e.Key is Key.Home or Key.End;
        if (e.KeyModifiers != KeyModifiers.None && !extend && !control)
            throw FlexGridMembers.Refused("keyboard modifiers");
        if (Model.Row < Model.FixedRows || Model.Col < Model.FixedCols || Model.Rows <= Model.FixedRows || Model.Cols <= Model.FixedCols)
            throw FlexGridMembers.Refused("keyboard selection without a non-fixed current cell");
        var row = extend ? Model.RowSel : Model.Row;
        var col = extend ? Model.ColSel : Model.Col;
        switch (e.Key)
        {
            case Key.Down: row++; break;
            case Key.Up: row--; break;
            case Key.Left: col--; break;
            case Key.Right: col++; break;
            case Key.Home: col = Model.FixedCols; if (control) row = Model.FixedRows; break;
            case Key.End: col = Model.Cols - 1; if (control) row = Model.Rows - 1; break;
            case Key.PageDown: row += PageRows(); break;
            case Key.PageUp: row -= PageRows(); break;
        }
        if (Model.SelectionMode == 1)
        {
            if (!extend && e.Key is Key.Up or Key.PageUp or Key.PageDown)
                throw FlexGridMembers.Refused(nameof(Model.SelectionMode));
            col = Model.Col;
            if (!control && e.Key is Key.Left or Key.Right or Key.Home or Key.End)
                row = extend && e.Key is Key.Left or Key.Right ? Model.RowSel : Model.Row;
        }
        else if (Model.SelectionMode == 2)
        {
            row = Model.Row;
            if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown)
            {
                col = extend ? Model.ColSel : Model.Col;
                if (!extend && (e.Key != Key.Down || Model.RowSel == Model.Rows - 1))
                    throw FlexGridMembers.Refused(nameof(Model.SelectionMode));
            }
            else if (!extend && col == Model.Col)
                throw FlexGridMembers.Refused(nameof(Model.SelectionMode));
        }
        if (row < Model.FixedRows || row >= Model.Rows || col < Model.FixedCols || col >= Model.Cols)
            throw FlexGridMembers.Refused("keyboard selection at the grid boundary");
        var firstRow = Model.SelectionMode == 2 ? Model.TopRow
            : KeepVisible(row, Model.TopRow, Model.FixedRows, viewport.Height - FixedHeight, RowSize);
        var firstCol = Model.SelectionMode == 1 ? Model.LeftCol
            : KeepVisible(col, Model.LeftCol, Model.FixedCols, viewport.Width - FixedWidth, ColSize);
        Model.ScrollTo(firstRow, firstCol);
        Model.SelectKeyboardCell(row, col, extend);
        RefreshView();
        e.Handled = true;
    }

    private int PageRows() => Math.Max(1, Axis(Model.Rows, Model.FixedRows, Model.TopRow, viewport.Height, RowSize)
        .Count(r => r.Index >= Model.FixedRows && r.Position + r.Size <= viewport.Height));

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Handled || !ReferenceEquals(e.Source, this)) return;
        // These are the recorded keys, not a general rule for navigation KeyUp (#1099).
        var code = (e.Key, e.KeyModifiers) switch
        {
            (Key.Home, KeyModifiers.None or KeyModifiers.Control) => 36,
            (Key.End, KeyModifiers.None or KeyModifiers.Control) => 35,
            (Key.PageUp, KeyModifiers.None) => 33,
            (Key.PageDown, KeyModifiers.None) => 34,
            (Key.Down, KeyModifiers.Shift) => 40,
            (Key.Right, KeyModifiers.Shift) => 39,
            (Key.Enter, KeyModifiers.None) => 13,
            (Key.A, KeyModifiers.None) => 65,
            _ => 0,
        };
        if (code == 0) return;
        RaiseKey(ComponentBaseClass.KeyUpEvent, code, e.KeyModifiers == KeyModifiers.Shift ? 1 : e.KeyModifiers == KeyModifiers.Control ? 2 : 0);
        e.Handled = true;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || !ReferenceEquals(e.Source, this)) return;
        if (e.Text is not ("a" or "\r"))
        {
            e.Handled = true;
            RefuseInput(FlexGridMembers.Refused("keyboard text input"));
            return;
        }
        if (e.Text == "a") RaiseKey(ComponentBaseClass.KeyPressEvent, e.Text[0]);
        e.Handled = true;
    }

    private void RefuseInput(NotImplementedException error)
    {
        if (this.FindLogicalAncestorOfType<VBFormRuntime>() is { } form)
            form.Context.RefuseInput(error);
        else
            throw error;
    }

    private void RaiseKey(EventClass ev, params int[] values)
    {
        foreach (var handler in ControlEvents.Handlers(this, this.FindLogicalAncestorOfType<IModuleExecutionRoot>(), ev.Name))
            handler.Root.ExecuteEvent(handler.Name, () =>
            {
                var args = new List<Vb6Value>();
                if (handler.Own && VBProps.GetIndex(this) is { } index) args.Add(new Vb6Value(index));
                foreach (var value in values) args.Add(new Vb6Value(value));
                return args;
            });
    }
}
