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

    public event System.Action? Changed;

    public void Push(IDesignerCommand command)
    {
        if (vm.IsDragging) return;
        _undo.AddLast(command);
        _redo.Clear();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var command = _undo.Last!.Value;
        _undo.RemoveLast();
        command.Undo(vm);
        _redo.AddFirst(command);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var command = _redo.First!.Value;
        _redo.RemoveFirst();
        command.Execute(vm);
        _undo.AddLast(command);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }
}
