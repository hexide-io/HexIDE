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

    protected override Control InstantiateInternal(ComponentInstance instance) =>
        VBLoader.SpawnComponentsForDesigner(_formPart, _registry);

    public static UserControlComponentClass ForModule(string typeName, FormDefinition formPart,
        IReadOnlyDictionary<string, ComponentBaseClass> registry) => new(typeName, formPart, registry);
}
