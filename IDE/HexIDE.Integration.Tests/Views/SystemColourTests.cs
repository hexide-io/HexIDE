using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Every one of VB6's 25 system colours has a value, and the seven that had none take Windows 10's (#908).
/// </summary>
/// <remarks>
/// A system colour with no resource behind it fell back to <see cref="Brushes.DeepPink"/>, so a control given
/// <c>vbInfoBackground</c> painted hot pink in the designer and at run time. Nothing caught it because the test
/// application held none of the colours either: every one was pink here, and a test comparing a painted pixel with
/// <c>vbButtonFace</c> compared pink with pink.
/// </remarks>
public class SystemColourTests
{
    public static TheoryData<VbSystemColor> Every => [.. Enum.GetValues<VbSystemColor>()];

    [AvaloniaTheory]
    [MemberData(nameof(Every))]
    public void Every_system_colour_has_a_value(VbSystemColor index) =>
        VBColor.FromSystemColor(index).ToBrush().Should().NotBeSameAs(Brushes.DeepPink,
            $"{index} must resolve to a colour; the pink is the fallback for a key nothing defines");

    // Measured on the oracle guest (Windows 10), the oracle's "Seven system colours": vb6.exe paints a Label in each
    // exactly as GetSysColor answers for its index, read back with Point.
    [AvaloniaTheory]
    [InlineData(VbSystemColor.Scrollbar, "#C8C8C8")]
    [InlineData(VbSystemColor.Activecaption, "#99B4D1")]
    [InlineData(VbSystemColor.Activeborder, "#B4B4B4")]
    [InlineData(VbSystemColor.Inactiveborder, "#F4F7FC")]
    [InlineData(VbSystemColor.Infotext, "#000000")]
    [InlineData(VbSystemColor.Infobk, "#FFFFE1")]
    [InlineData(VbSystemColor.Windowframe, "#646464")]
    public void A_colour_that_had_no_value_takes_Windows_10s(VbSystemColor index, string expected) =>
        ((ISolidColorBrush)VBColor.FromSystemColor(index).ToBrush()).Color.Should().Be(Color.Parse(expected));
}
