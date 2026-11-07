using System.Collections.Generic;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
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

    internal bool Setting(string name, bool fallback = false) =>
        AvaloniaInterop.UserControlMembers.Setting(
            _formPart.Components.First(c => c.BaseClass is FormComponentClass), name, fallback);


    // A UserControl whose .ctl saves Enabled = False is disabled where it is placed, and so is what it holds (measured,
    // #1101).
    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        var root = _formPart.Components.First(c => c.BaseClass is FormComponentClass);
        var keys = root.UnknownRawProperties.FirstOrDefault(p => p.Name.Equals("AccessKeys", System.StringComparison.OrdinalIgnoreCase));
        var line = keys.Name is null ? "" : keys.Lines[0];
        var first = line.IndexOf('"');
        var last = line.LastIndexOf('"');
        var accessKeys = first >= 0 && last > first ? line[(first + 1)..last] : "";
        var control = new VBUserControl(VBLoader.SpawnComponentsForDesigner(_formPart, _registry), accessKeys, _formPart);
        control.IsEnabled = root.GetPropertyOrDefault(VBProperties.EnabledProperty);
        AvaloniaInterop.UserControlMembers.Register(control, root);
        return control;
    }

    public override bool TryGetChildHost(Control control, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Canvas? host)
    {
        host = control is VBUserControl uc ? uc.ContainerHost : null;
        return host is not null;
    }

    public static UserControlComponentClass ForModule(string typeName, FormDefinition formPart,
        IReadOnlyDictionary<string, ComponentBaseClass> registry) => new(typeName, formPart, registry);
}
