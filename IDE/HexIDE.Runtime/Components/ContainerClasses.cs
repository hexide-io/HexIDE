namespace HexIDE.Runtime.Components;

/// <summary>
/// Which component classes can hold other controls.
///
/// The list is closed: Form, PictureBox, Frame and the in-box SSTab (#1090), plus the two other designer roots (UserControl, PropertyPage), which the
/// deserializer already maps onto <see cref="FormComponentClass"/>. Nothing else is one: not a hosted
/// UserControl placed on a form, and not a class an add-in registered, because HexIDE has no way to host
/// arbitrary children inside a control it did not build.
///
/// That matters because the .frm format lets anything be written nested and VB6 loads such a file without
/// complaint — a control nested under a ListBox is corrupt input, not an exotic container. The deserializer
/// handles that case itself: it declines to record a containment link, so the depth counter still sees the
/// nesting and the refusal gate still fires.
/// </summary>
public static class ContainerClasses
{
    public static bool IsContainer(IComponentClass componentClass) =>
        componentClass is FormComponentClass or FrameComponentClass or PictureBoxComponentClass or InBox.SSTabComponentClass;
}
