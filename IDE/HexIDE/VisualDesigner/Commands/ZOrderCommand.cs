using System.Collections.Generic;

namespace HexIDE.VisualDesigner.Commands;

/// <summary>
/// A Bring to Front or Send to Back, kept as the canvas order before and after it. A container moves with what it holds,
/// so the move is a run of controls rather than one, and the two orders put back exactly what it changed (#1481). The
/// carried controls the target was stacked over are kept too, since the canvas order cannot hold them (#1498).
/// </summary>
internal class ZOrderCommand : IDesignerCommand
{
    private readonly ComponentInstanceViewModel _target;
    private readonly IReadOnlyList<ComponentInstanceViewModel> _before;
    private readonly IReadOnlyList<ComponentInstanceViewModel> _after;
    private readonly int _carriedBelowBefore;
    private readonly int _carriedBelowAfter;

    public string Description { get; }

    internal ZOrderCommand(ComponentInstanceViewModel target, IReadOnlyList<ComponentInstanceViewModel> before,
        IReadOnlyList<ComponentInstanceViewModel> after, int carriedBelowBefore, int carriedBelowAfter, string description)
    {
        _target = target;
        _before = before;
        _after = after;
        _carriedBelowBefore = carriedBelowBefore;
        _carriedBelowAfter = carriedBelowAfter;
        Description = description;
    }

    public void Execute(FormEditViewModel vm) => Apply(vm, _after, _carriedBelowAfter);

    public void Undo(FormEditViewModel vm) => Apply(vm, _before, _carriedBelowBefore);

    private void Apply(FormEditViewModel vm, IReadOnlyList<ComponentInstanceViewModel> order, int carriedBelow)
    {
        if (!vm.Components.Contains(_target)) return;
        vm.Arrange(order);
        _target.CarriedBelow = carriedBelow;
        vm.SelectedComponent = null;
        vm.SelectedComponent = _target;
    }
}
