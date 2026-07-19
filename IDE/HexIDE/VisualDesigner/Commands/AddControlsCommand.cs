using System;
using System.Collections.Generic;

namespace HexIDE.VisualDesigner.Commands;

internal class AddControlsCommand : IDesignerCommand
{
    private readonly List<(ComponentInstanceViewModel Vm, int ComponentsIdx, int AllComponentsIdx)> _entries;

    public string Description { get; }

    internal AddControlsCommand(
        IReadOnlyList<(ComponentInstanceViewModel Vm, int ComponentsIdx, int AllComponentsIdx)> entries,
        string description)
    {
        _entries = [.. entries];
        Description = description;
    }

    public void Execute(FormEditViewModel vm)
    {
        foreach (var e in _entries)
        {
            int ci = Math.Min(e.ComponentsIdx, vm.Components.Count);
            vm.Components.Insert(ci, e.Vm);
            int ai = Math.Min(e.AllComponentsIdx, vm.AllComponents.Count);
            vm.AllComponents.Insert(ai, e.Vm);
        }
        if (_entries.Count > 0)
            vm.SelectedComponent = _entries[^1].Vm;
    }

    public void Undo(FormEditViewModel vm)
    {
        foreach (var e in _entries)
        {
            vm.Components.Remove(e.Vm);
            vm.AllComponents.Remove(e.Vm);
        }
        vm.SelectedComponent = vm.Form;
    }
}
