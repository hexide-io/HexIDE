namespace HexIDE.Runtime.Components;

/// <summary>
/// Which component classes can hold other controls.
///
/// Form, PictureBox and Frame hold controls, as do the designer roots mapped onto FormComponentClass.
/// A project UserControl can carry placed children; its runtime ControlContainer setting determines whether
/// a program may move another control into it (#1631).
///
/// That matters because the .frm format lets anything be written nested and VB6 loads such a file without
/// complaint — a control nested under a ListBox is corrupt input, not an exotic container. The deserializer
/// handles that case itself: it declines to record a containment link, so the depth counter still sees the
/// nesting and the refusal gate still fires.
/// </summary>
public static class ContainerClasses
{
    public static bool IsContainer(IComponentClass componentClass) =>
        componentClass is FormComponentClass or FrameComponentClass or PictureBoxComponentClass or UserControlComponentClass;
}
