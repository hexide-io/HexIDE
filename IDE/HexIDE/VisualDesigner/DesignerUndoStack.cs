using System.Collections.Generic;

namespace HexIDE.VisualDesigner;

public class DesignerUndoStack(FormEditViewModel vm)
{
    private readonly LinkedList<IDesignerCommand> _undo = new();
    private readonly LinkedList<IDesignerCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => CanUndo ? _undo.Last!.Value.Description : null;
    public string? RedoDescription => CanRedo ? _redo.First!.Value.Description : null;

    /// <summary>
    /// The stack's contents changed. Raised by every operation below, <see cref="Clear"/> included, because
    /// its only subscriber is the Undo/Redo CanExecute plumbing and that has to follow a clear too.
    /// </summary>
    /// <remarks>
    /// <b>Not the commit signal.</b> Anything that wants "the form was changed" wants
    /// <c>FormEditViewModel.CommitLayout</c>, which the three mutating operations call and
    /// <see cref="Clear"/> does not — a clear happens when the designer is rebuilt from a freshly-reloaded
    /// model, and treating that as a commit would re-render the form and replace the text just read from
    /// disk with a reproduction of it.
    /// </remarks>
    public event System.Action? Changed;

    public void Push(IDesignerCommand command)
    {
        // Discarded mid-drag on purpose: the drag writes the model on every pointer move and EndDrag pushes
        // one command for the whole gesture. That is also what makes this the right place to announce a
        // commit — once per gesture, not once per pixel.
        if (vm.IsDragging) return;
        _undo.AddLast(command);
        _redo.Clear();
        Changed?.Invoke();
        vm.CommitLayout();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var command = _undo.Last!.Value;
        _undo.RemoveLast();
        command.Undo(vm);
        _redo.AddFirst(command);
        Changed?.Invoke();
        vm.CommitLayout();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var command = _redo.First!.Value;
        _redo.RemoveFirst();
        command.Execute(vm);
        _undo.AddLast(command);
        Changed?.Invoke();
        vm.CommitLayout();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }
}
