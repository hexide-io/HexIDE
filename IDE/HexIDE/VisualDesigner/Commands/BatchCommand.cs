using System.Collections.Generic;

namespace HexIDE.VisualDesigner.Commands;

internal class BatchCommand : IDesignerCommand
{
    private readonly IReadOnlyList<IDesignerCommand> _inner;

    internal BatchCommand(string description, IReadOnlyList<IDesignerCommand> inner)
    {
        Description = description;
        _inner = inner;
    }

    public string Description { get; }

    public void Execute(FormEditViewModel vm)
    {
        foreach (var c in _inner)
            c.Execute(vm);
    }

    public void Undo(FormEditViewModel vm)
    {
        for (int i = _inner.Count - 1; i >= 0; i--)
            _inner[i].Undo(vm);
    }
}
