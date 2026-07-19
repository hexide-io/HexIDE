using HexIDE.Runtime.Components;

namespace HexIDE.VisualDesigner.Commands;

internal class SetPropertyCommand : IDesignerCommand
{
    private readonly ComponentInstance _target;
    private readonly PropertyClass _property;
    private readonly object? _before;
    private readonly object? _after;

    internal SetPropertyCommand(ComponentInstance target, PropertyClass property, object? before, object? after)
    {
        _target = target;
        _property = property;
        _before = before;
        _after = after;
        Description = $"{property.Name}: {target.GetPropertyOrDefault(VBProperties.NameProperty) ?? ""}";
    }

    public string Description { get; }

    public void Execute(FormEditViewModel vm) => _target.SetUntypedProperty(_property, _after);
    public void Undo(FormEditViewModel vm) => _target.SetUntypedProperty(_property, _before);
}
