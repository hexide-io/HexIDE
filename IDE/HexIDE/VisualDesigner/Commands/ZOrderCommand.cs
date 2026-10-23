using System.Collections.Generic;

namespace HexIDE.VisualDesigner.Commands;

/// <summary>
/// A Bring to Front or Send to Back, kept as the canvas order before and after it. A container moves with what it holds,
/// so the move is a run of controls rather than one, and the two orders put back exactly what it changed (#1481).
/// </summary>
internal class ZOrderCommand : IDesignerCommand
{
    private readonly ComponentInstanceViewModel _target;
    private readonly IReadOnlyList<ComponentInstanceViewModel> _before;
    private readonly IReadOnlyList<ComponentInstanceViewModel> _after;

    public string Description { get; }

    internal ZOrderCommand(ComponentInstanceViewModel target, IReadOnlyList<ComponentInstanceViewModel> before,
        IReadOnlyList<ComponentInstanceViewModel> after, string description)
    {
        _target = target;
        _before = before;
        _after = after;
        Description = description;
    }

    public void Execute(FormEditViewModel vm) => Apply(vm, _after);

    public void Undo(FormEditViewModel vm) => Apply(vm, _before);

    private void Apply(FormEditViewModel vm, IReadOnlyList<ComponentInstanceViewModel> order)
    {
        if (!vm.Components.Contains(_target)) return;
        vm.Arrange(order);
        vm.SelectedComponent = null;
        vm.SelectedComponent = _target;
    }
}
