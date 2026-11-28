using HexIDE.Runtime.Components;

namespace HexIDE.Tests.Components;

public class LineControlBoundsTests
{
    // Existing arithmetic, pinned for the move to Core (#5680); these are not new VB6 measurements.
    [Theory]
    [InlineData(0, 0, 1500, 750, 1, -2, -2, 105, 55)]
    [InlineData(0, 0, 1500, 750, 0, -2, -2, 105, 55)]
    [InlineData(0, 0, 1500, 750, 3, -4, -4, 109, 59)]
    [InlineData(1500, 750, 0, 0, 1, -2, -2, 105, 55)]
    public void BoundsFor_CoversSegmentAndPen(double x1, double y1, double x2, double y2, int borderWidth,
        double left, double top, double width, double height)
    {
        var bounds = LineControlBounds.BoundsFor(x1, y1, x2, y2, borderWidth);
        bounds.Should().Be((left, top, width, height));
    }
}
