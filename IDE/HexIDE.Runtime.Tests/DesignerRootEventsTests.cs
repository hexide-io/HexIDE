using System.Linq;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A UserControl's and a PropertyPage's designer roots have their own events, which the code window's Procedure box lists
/// and writes stubs for, and their own procedure for a double click on the designer's background (#1632). Each expected
/// line is one VB6 wrote: in its IDE on the oracle guest's console, every event in the Procedure box was chosen in turn
/// and the file saved, on SP6, VB6.EXE 6.00.9782. The release build, 6.00.8176, listed the same events and wrote the same
/// double-click procedures (the oracle's "A UserControl's and a PropertyPage's own events").
/// </summary>
public class DesignerRootEventsTests
{
    // Each event as VB6's stub declares it, with the root's name before it, as the Procedure box writes it.
    private static string[] Stubs(FormComponentClass root) =>
        [.. root.Events.Select(e => $"Private Sub {root.Name}_{e.Name}("
                                    + string.Join(", ", e.Arguments.Select(a => $"{a.DefaultName} As {a.Type}")) + ")")];

    [Fact]
    public void A_UserControl_has_VB6s_35_events_with_VB6s_arguments() =>
        Stubs(UserControlRootClass.Instance).Should().Equal(
        "Private Sub UserControl_AccessKeyPress(KeyAscii As Integer)",
        "Private Sub UserControl_AmbientChanged(PropertyName As String)",
        "Private Sub UserControl_AsyncReadComplete(AsyncProp As AsyncProperty)",
        "Private Sub UserControl_AsyncReadProgress(AsyncProp As AsyncProperty)",
        "Private Sub UserControl_Click()",
        "Private Sub UserControl_DblClick()",
        "Private Sub UserControl_DragDrop(Source As Control, X As Single, Y As Single)",
        "Private Sub UserControl_DragOver(Source As Control, X As Single, Y As Single, State As Integer)",
        "Private Sub UserControl_EnterFocus()",
        "Private Sub UserControl_ExitFocus()",
        "Private Sub UserControl_GetDataMember(DataMember As String, Data As Object)",
        "Private Sub UserControl_GotFocus()",
        "Private Sub UserControl_Hide()",
        "Private Sub UserControl_HitTest(X As Single, Y As Single, HitResult As Integer)",
        "Private Sub UserControl_Initialize()",
        "Private Sub UserControl_InitProperties()",
        "Private Sub UserControl_KeyDown(KeyCode As Integer, Shift As Integer)",
        "Private Sub UserControl_KeyPress(KeyAscii As Integer)",
        "Private Sub UserControl_KeyUp(KeyCode As Integer, Shift As Integer)",
        "Private Sub UserControl_LostFocus()",
        "Private Sub UserControl_MouseDown(Button As Integer, Shift As Integer, X As Single, Y As Single)",
        "Private Sub UserControl_MouseMove(Button As Integer, Shift As Integer, X As Single, Y As Single)",
        "Private Sub UserControl_MouseUp(Button As Integer, Shift As Integer, X As Single, Y As Single)",
        "Private Sub UserControl_OLECompleteDrag(Effect As Long)",
        "Private Sub UserControl_OLEDragDrop(Data As DataObject, Effect As Long, Button As Integer, Shift As Integer, X As Single, Y As Single)",
        "Private Sub UserControl_OLEDragOver(Data As DataObject, Effect As Long, Button As Integer, Shift As Integer, X As Single, Y As Single, State As Integer)",
        "Private Sub UserControl_OLEGiveFeedback(Effect As Long, DefaultCursors As Boolean)",
        "Private Sub UserControl_OLESetData(Data As DataObject, DataFormat As Integer)",
        "Private Sub UserControl_OLEStartDrag(Data As DataObject, AllowedEffects As Long)",
        "Private Sub UserControl_Paint()",
        "Private Sub UserControl_ReadProperties(PropBag As PropertyBag)",
        "Private Sub UserControl_Resize()",
        "Private Sub UserControl_Show()",
        "Private Sub UserControl_Terminate()",
        "Private Sub UserControl_WriteProperties(PropBag As PropertyBag)"
        );

    [Fact]
    public void A_PropertyPage_has_VB6s_24_events_with_VB6s_arguments() =>
        Stubs(PropertyPageRootClass.Instance).Should().Equal(
        "Private Sub PropertyPage_ApplyChanges()",
        "Private Sub PropertyPage_Click()",
        "Private Sub PropertyPage_DblClick()",
        "Private Sub PropertyPage_DragDrop(Source As Control, X As Single, Y As Single)",
        "Private Sub PropertyPage_DragOver(Source As Control, X As Single, Y As Single, State As Integer)",
        "Private Sub PropertyPage_EditProperty(PropertyName As String)",
        "Private Sub PropertyPage_GotFocus()",
        "Private Sub PropertyPage_Initialize()",
        "Private Sub PropertyPage_KeyDown(KeyCode As Integer, Shift As Integer)",
        "Private Sub PropertyPage_KeyPress(KeyAscii As Integer)",
        "Private Sub PropertyPage_KeyUp(KeyCode As Integer, Shift As Integer)",
        "Private Sub PropertyPage_LostFocus()",
        "Private Sub PropertyPage_MouseDown(Button As Integer, Shift As Integer, X As Single, Y As Single)",
        "Private Sub PropertyPage_MouseMove(Button As Integer, Shift As Integer, X As Single, Y As Single)",
        "Private Sub PropertyPage_MouseUp(Button As Integer, Shift As Integer, X As Single, Y As Single)",
        "Private Sub PropertyPage_OLECompleteDrag(Effect As Long)",
        "Private Sub PropertyPage_OLEDragDrop(Data As DataObject, Effect As Long, Button As Integer, Shift As Integer, X As Single, Y As Single)",
        "Private Sub PropertyPage_OLEDragOver(Data As DataObject, Effect As Long, Button As Integer, Shift As Integer, X As Single, Y As Single, State As Integer)",
        "Private Sub PropertyPage_OLEGiveFeedback(Effect As Long, DefaultCursors As Boolean)",
        "Private Sub PropertyPage_OLESetData(Data As DataObject, DataFormat As Integer)",
        "Private Sub PropertyPage_OLEStartDrag(Data As DataObject, AllowedEffects As Long)",
        "Private Sub PropertyPage_Paint()",
        "Private Sub PropertyPage_SelectionChanged()",
        "Private Sub PropertyPage_Terminate()"
        );

    [Fact]
    public void A_double_click_on_the_background_writes_the_roots_own_event()
    {
        UserControlRootClass.Instance.DesignerDoubleClickEvent.Should().Be("Initialize");
        PropertyPageRootClass.Instance.DesignerDoubleClickEvent.Should().Be("SelectionChanged");
        FormComponentClass.Instance.DesignerDoubleClickEvent.Should().Be("Load", "a form's is Load, as #1532 measured");
    }
}
