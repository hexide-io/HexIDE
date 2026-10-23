using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Which controls have ToolTipText, as the Properties window lists it and a .frm keeps it (#1403). vb6.exe gave it to
/// these ten and to the DriveListBox, DirListBox, FileListBox and Data control, which HexIDE does not run, and to no
/// scroll bar, Shape, Line, Timer, menu or form: reached late-bound, each of those answered 438, and a ToolTipText line on
/// an HScrollBar stopped vb6.exe loading the form (the oracle's "ToolTipText: which controls have it").
/// </summary>
public class ToolTipTextDeclarationTests
{
    public static TheoryData<ComponentBaseClass, bool> Classes => new()
    {
        { CommandButtonComponentClass.Instance, true },
        { TextBoxComponentClass.Instance, true },
        { ComboBoxComponentClass.Instance, true },
        { ListBoxComponentClass.Instance, true },
        { CheckBoxComponentClass.Instance, true },
        { OptionButtonComponentClass.Instance, true },
        { FrameComponentClass.Instance, true },
        { PictureBoxComponentClass.Instance, true },
        { LabelComponentClass.Instance, true },
        { ImageComponentClass.Instance, true },
        { HScrollBarComponentClass.Instance, false },
        { VScrollBarComponentClass.Instance, false },
        { ShapeComponentClass.Instance, false },
        { LineComponentClass.Instance, false },
        { TimerComponentClass.Instance, false },
        { FormComponentClass.Instance, false },
    };

    [Theory]
    [MemberData(nameof(Classes))]
    public void ToolTipText_is_declared_on_the_controls_VB6_gives_it(ComponentBaseClass component, bool has)
    {
        component.Properties.Contains(VBProperties.ToolTipTextProperty).Should().Be(has, component.VBTypeName);
    }
}
