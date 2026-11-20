using Avalonia;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.Tests;

// #922 latest supervisor comment, measured in oracle PR #4984. Inputs/outputs below are twips except c/h.
public class LabelAutoSizePlacementTests
{
    [Theory]
    [InlineData(600, 1500, 3000, 195, 5, 13, 2, false, 2055, 1500, 90, 195)]
    [InlineData(600, 1500, 3000, 195, 10, 13, 2, false, 2025, 1500, 150, 195)]
    [InlineData(600, 1500, 3000, 195, 2, 13, 2, false, 2085, 1500, 30, 195)]
    [InlineData(600, 1500, 3000, 195, 3, 13, 2, false, 2070, 1500, 60, 195)]
    [InlineData(600, 1500, 3000, 195, 7, 13, 2, false, 2040, 1500, 120, 195)]
    [InlineData(600, 1500, 3015, 195, 5, 13, 2, false, 2055, 1500, 105, 195)]
    [InlineData(600, 1500, 3015, 195, 10, 13, 2, false, 2025, 1500, 165, 195)]
    [InlineData(600, 1500, 600, 195, 137, 13, 2, false, -135, 1500, 2070, 195)]
    [InlineData(600, 1500, 615, 195, 137, 13, 2, false, -135, 1500, 2085, 195)]
    [InlineData(600, 1500, 600, 195, 137, 13, 1, false, -855, 1500, 2055, 195)]
    [InlineData(600, 1500, 615, 195, 137, 13, 1, false, -840, 1500, 2055, 195)]
    [InlineData(610, 1510, 3000, 195, 10, 13, 0, false, 615, 1515, 150, 195)]
    [InlineData(600, 1500, 1000, 195, 67, 13, 0, true, 600, 1500, 1000, 195)]
    // Deliberate divergence: keep Center from creeping when no word exceeds its current width.
    [InlineData(600, 1500, 1000, 195, 67, 52, 2, true, 600, 1500, 1005, 780)]
    [InlineData(600, 1500, 1005, 780, 133, 13, 2, true, 90, 1500, 2025, 195)]
    [InlineData(90, 1500, 2025, 195, 135, 13, 2, true, 90, 1500, 2025, 195)]
    [InlineData(600, 1500, 1005, 780, 133, 13, 1, true, -390, 1500, 1995, 195)]
    public void Placement(double l, double t, double w, double h, double c, double height, int alignment, bool wrap,
        double left, double top, double width, double expectedHeight)
    {
        var result = LabelAutoSize.Place(new Rect(l / 15, t / 15, w / 15, h / 15), new Size(c, height),
            (VBTextAlignment)alignment, wrap);
        result.Should().Be(new Rect(left / 15, top / 15, width / 15, expectedHeight / 15));
    }
}
