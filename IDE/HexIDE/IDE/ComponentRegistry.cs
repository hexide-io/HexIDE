using System.Collections.Generic;
using HexIDE.Runtime.Components;

namespace HexIDE.IDE;

public class ComponentRegistry : IComponentRegistry
{
    private readonly List<IComponentClass> _components =
    [
        FormComponentClass.Instance,
        PictureBoxComponentClass.Instance,
        LabelComponentClass.Instance,
        TextBoxComponentClass.Instance,
        FrameComponentClass.Instance,
        CommandButtonComponentClass.Instance,
        CheckBoxComponentClass.Instance,
        OptionButtonComponentClass.Instance,
        ComboBoxComponentClass.Instance,
        ListBoxComponentClass.Instance,
        HScrollBarComponentClass.Instance,
        VScrollBarComponentClass.Instance,
        TimerComponentClass.Instance,
        ShapeComponentClass.Instance,
        ImageComponentClass.Instance,
    ];

    public IReadOnlyList<IComponentClass> Components => _components;
    public event Action<IComponentClass>? ComponentRegistered;

    public event Action<IComponentClass>? ComponentUnregistered;

    public void Register(IComponentClass component)
    {
        _components.Add(component);
        HexIDE.Addins.AddinInitializationScope.Record(() =>
        {
            _components.RemoveAt(_components.LastIndexOf(component));
            ComponentUnregistered?.Invoke(component);
        });
        ComponentRegistered?.Invoke(component);
    }
}
