using System.Collections.Generic;
using Avalonia.Controls;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Components;

public class UserControlComponentClass : ComponentBaseClass
{
    private readonly string _typeName;
    private readonly FormDefinition _formPart;
    private readonly IReadOnlyDictionary<string, ComponentBaseClass> _registry;

    private UserControlComponentClass(string typeName, FormDefinition formPart,
        IReadOnlyDictionary<string, ComponentBaseClass> registry) : base([])
    {
        _typeName = typeName;
        _formPart = formPart;
        _registry = registry;
    }

    public override string Name =>
        _typeName.Contains('.') ? _typeName[(_typeName.LastIndexOf('.') + 1)..] : _typeName;

    public override string VBTypeName => _typeName;

    // A UserControl whose .ctl saves Enabled = False is disabled where it is placed, and so is what it holds (measured,
    // #1101).
    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        var control = VBLoader.SpawnComponentsForDesigner(_formPart, _registry);
        control.IsEnabled = _formPart.Components.First(c => c.BaseClass is FormComponentClass)
            .GetPropertyOrDefault(VBProperties.EnabledProperty);
        return control;
    }

    public static UserControlComponentClass ForModule(string typeName, FormDefinition formPart,
        IReadOnlyDictionary<string, ComponentBaseClass> registry) => new(typeName, formPart, registry);
}
