using HexIDE.Controls;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Integration.Tests.Controls;

/// <summary>
/// MousePointer has VB6's 17 values, named as VB6 names them and drawn as VB6 draws them (#120). It had 13: 4, 5, 13 and
/// 14 loaded as bare numbers, saved without the comment VB6 writes beside them, showed a blank in the Properties window,
/// and drew the default pointer.
/// </summary>
/// <remarks>
/// Every expectation is the oracle's "MousePointer's names and pointers": the comments are what the VB6 IDE wrote on
/// saving a form that gave a Label each value bare, and the pointers are what GetCursorInfo reported over each Label of
/// the same form built and run.
/// </remarks>
public class MousePointerValuesTests
{
    [AvaloniaFact]
    public void The_Properties_window_offers_all_seventeen_values_by_VB6s_names()
    {
        new PropertyEnumBox { PropertyType = typeof(VBCursorType) }.Options!.Select(o => o.Text).Should().Equal(
            "0 - Default", "1 - Arrow", "2 - Cross", "3 - I-Beam", "4 - Icon", "5 - Size", "6 - Size NE SW", "7 - Size N S",
            "8 - Size NW SE", "9 - Size W E", "10 - Up Arrow", "11 - Hourglass", "12 - No Drop", "13 - Arrow and Hourglass",
            "14 - Arrow and Question", "15 - Size All", "99 - Custom");
    }
}
