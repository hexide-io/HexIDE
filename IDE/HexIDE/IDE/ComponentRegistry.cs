using System.Collections.Generic;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;

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

    public IReadOnlyList<ToolboxEntry> Toolbox { get; } =
    [
        new(PictureBoxComponentClass.Instance, "Geo.Picture"),
        new(LabelComponentClass.Instance, "Geo.Label"),
        new(TextBoxComponentClass.Instance, "Geo.Textbox"),
        new(FrameComponentClass.Instance, "Geo.Groupbox"),
        new(CommandButtonComponentClass.Instance, "Geo.Button"),
        new(CheckBoxComponentClass.Instance, "Geo.Checkbox"),
        new(OptionButtonComponentClass.Instance, "Geo.Radio"),
        new(ComboBoxComponentClass.Instance, "Geo.Combo"),
        new(ListBoxComponentClass.Instance, "Geo.Listbox"),
        new(HScrollBarComponentClass.Instance, "Geo.Hscroll"),
        new(VScrollBarComponentClass.Instance, "Geo.Vscroll"),
        new(TimerComponentClass.Instance, "Geo.Timer"),
        new(ShapeComponentClass.Instance, "Geo.Shape"),
    ];

    public IReadOnlyList<RuntimeLibrary> Libraries => ControlLibraries.All;

    // Built on first use, so these classes load only once a project references one of their libraries, as they always did.
    private IReadOnlyList<LibraryControl>? libraryControls;

    public IReadOnlyList<LibraryControl> LibraryControls => libraryControls ??=
    [
        .. from library in ControlLibraries.All
           from control in InBoxComponentClass.Implemented
           where control.Libraries.Contains(library)
           select new LibraryControl(control, library, control.ControlClassName, library.TypeName(control.ControlClassName), "Geo.Object"),
    ];

    public event Action<IComponentClass>? ComponentRegistered;

    public event Action<IComponentClass>? ComponentUnregistered;

    public IDisposable Register(IComponentClass component)
    {
        _components.Add(component);
        var handle = new HexIDE.Utils.ActionDisposable(() =>
        {
            _components.RemoveAt(_components.LastIndexOf(component));
            ComponentUnregistered?.Invoke(component);
        });
        HexIDE.Addins.AddinInitializationScope.Record(handle.Dispose);
        ComponentRegistered?.Invoke(component);
        return handle;
    }
}
