using System;

namespace HexIDE.VisualDesigner.Commands;

internal class ZOrderCommand : IDesignerCommand
{
    private readonly ComponentInstanceViewModel _target;
    private readonly int _oldIndex;
    private readonly int _newIndex;

    public string Description { get; }

    internal ZOrderCommand(ComponentInstanceViewModel target, int oldIndex, int newIndex, string description)
    {
        _target = target;
        _oldIndex = oldIndex;
        _newIndex = newIndex;
        Description = description;
    }

    public void Execute(FormEditViewModel vm)
    {
        int current = vm.Components.IndexOf(_target);
        if (current == -1) return;
        int target = Math.Min(_newIndex, vm.Components.Count - 1);
        vm.Components.Move(current, target);
        vm.SelectedComponent = null;
        vm.SelectedComponent = _target;
    }

    public void Undo(FormEditViewModel vm)
    {
        int current = vm.Components.IndexOf(_target);
        if (current == -1) return;
        int target = Math.Min(_oldIndex, vm.Components.Count - 1);
        vm.Components.Move(current, target);
        vm.SelectedComponent = null;
        vm.SelectedComponent = _target;
    }
}
