using System;
using System.Collections.Generic;
using System.Linq;

namespace HexIDE.VisualDesigner.Commands;

internal class RemoveControlsCommand : IDesignerCommand
{
    private readonly List<(ComponentInstanceViewModel Vm, int ComponentsIdx, int AllComponentsIdx)> _entries;

    public string Description { get; }

    internal RemoveControlsCommand(
        IReadOnlyList<(ComponentInstanceViewModel Vm, int ComponentsIdx, int AllComponentsIdx)> entries,
        string description)
    {
        _entries = [.. entries];
        Description = description;
    }

    public void Undo(FormEditViewModel vm)
    {
        // Re-insert in ASCENDING original-index order (per list) so each stored index is valid against the
        // progressively-restored list. Inserting in the stored selection order scrambles z-order: a high-index
        // insert into a shortened list lands too early, then a later low-index insert shifts it. (Components and
        // AllComponents are independent lists with their own orderings, so each is restored on its own index.)
        foreach (var e in _entries.OrderBy(e => e.ComponentsIdx))
            vm.Components.Insert(Math.Min(e.ComponentsIdx, vm.Components.Count), e.Vm);
        foreach (var e in _entries.OrderBy(e => e.AllComponentsIdx))
            vm.AllComponents.Insert(Math.Min(e.AllComponentsIdx, vm.AllComponents.Count), e.Vm);
        if (_entries.Count > 0)
            vm.SelectedComponent = _entries[0].Vm;
    }

    public void Execute(FormEditViewModel vm)
    {
        foreach (var e in _entries)
        {
            vm.Components.Remove(e.Vm);
            vm.AllComponents.Remove(e.Vm);
        }
        vm.SelectedComponent = vm.Form;
    }
}
