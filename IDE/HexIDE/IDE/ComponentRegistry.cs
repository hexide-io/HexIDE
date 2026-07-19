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
    ];

    public IReadOnlyList<IComponentClass> Components => _components;
    public event Action<IComponentClass>? ComponentRegistered;

    public void Register(IComponentClass component)
    {
        _components.Add(component);
        ComponentRegistered?.Invoke(component);
    }
}
