using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.LogicalTree;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.BuiltinControls;

internal sealed partial class VBTreeView
{
    private LabelEditor? editor;
    private TreeNode? editOnRelease;
    private System.IDisposable? pendingLabelEdit;

    private void CancelPendingLabelEdit()
    {
        pendingLabelEdit?.Dispose();
        pendingLabelEdit = null;
        editOnRelease = null;
    }

    protected override void OnLostFocus(Avalonia.Input.FocusChangedEventArgs e)
    {
        CancelPendingLabelEdit();
        base.OnLostFocus(e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelPendingLabelEdit();
        base.OnDetachedFromVisualTree(e);
    }

    internal async Task StartLabelEditAsync()
    {
        CancelPendingLabelEdit();
        if (editor is not null) throw new System.NotImplementedException();
        if (Nodes.SelectedItem is not { } node) return;
        _ = LabelBounds(node);
        var pending = new LabelEditor(this, node.Text, async text =>
        {
            var args = new[] { new Vb6Value((short)0), new Vb6Value(text) };
            await RaiseEditAsync("AfterLabelEdit", args);
            return Cancelled(args[0]) ? null : TreeNodes.TextValue(args[1]);
        }, text => node.Text = text, () =>
        {
            if (editor is { } current) labelHost.Children.Remove(current);
            editor = null;
        });
        editor = pending;
        labelHost.Children.Add(pending);
        var before = new[] { new Vb6Value((short)0) };
        await RaiseEditAsync("BeforeLabelEdit", before);
        if (Cancelled(before[0]))
        {
            await pending.FinishAsync(discard: true);
            return;
        }
        pending.Open(LabelBounds(node), 2);
    }

    private Rect LabelBounds(TreeNode node)
    {
        var row = 0;
        foreach (var (visible, depth) in VisibleNodes())
        {
            if (ReferenceEquals(visible, node))
            {
                if (row >= GetVisibleCount()) throw new System.NotImplementedException();
                return new Rect(ClientEdge + (depth + 1) * indentation * 96 / 2540
                    + (Nodes.Checkboxes ? 16 : 0), ClientEdge + row * RowHeight, 0, RowHeight);
            }
            row++;
        }
        // Positioning an invisible selected node has not been measured.
        throw new System.NotImplementedException();
    }

    private static bool Cancelled(Vb6Value value) =>
        System.Convert.ToDouble(value.Value, CultureInfo.InvariantCulture) != 0;

    private async Task RaiseEditAsync(string name, Vb6Value[] arguments)
    {
        foreach (var handler in ControlEvents.Handlers(this, this.FindLogicalAncestorOfType<IModuleExecutionRoot>(), name))
        {
            var indexed = handler.Own && VBProps.GetIndex(this) is { };
            var args = new System.Collections.Generic.List<Vb6Value>();
            if (indexed) args.Add(new Vb6Value(VBProps.GetIndex(this)!.Value));
            args.AddRange(arguments);
            await handler.Root.ExecuteEditEventAsync(handler.Name, args);
            for (var i = 0; i < arguments.Length; i++) arguments[i] = args[i + (indexed ? 1 : 0)];
        }
    }
}
