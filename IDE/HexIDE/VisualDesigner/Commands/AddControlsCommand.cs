using System;
using System.Collections.Generic;
using System.Linq;

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
        // Each list is put back in ascending order of where its entries ended up, so every slot below an insert is final
        // when it lands. The two orders differ: a paste draws a container's contents in its own order and makes them in
        // the file's (#1481).
        foreach (var e in _entries.OrderBy(e => e.ComponentsIdx))
            vm.Components.Insert(Math.Min(e.ComponentsIdx, vm.Components.Count), e.Vm);
        foreach (var e in _entries.OrderBy(e => e.AllComponentsIdx))
            vm.AllComponents.Insert(Math.Min(e.AllComponentsIdx, vm.AllComponents.Count), e.Vm);
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
