using Avalonia;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Runtime.Tests;

public class RoundedShapeTests
{
    // docs/vb6-fidelity-oracle/a-rounded-shapes-corners.md (#5220, #916).
    [Theory]
    [InlineData(10, 10, 2)]
    [InlineData(15, 15, 3)]
    [InlineData(20, 20, 5)]
    [InlineData(21, 21, 5)]
    [InlineData(30, 30, 7)]
    [InlineData(40, 40, 10)]
    [InlineData(45, 45, 11)]
    [InlineData(50, 50, 12)]
    [InlineData(60, 60, 15)]
    [InlineData(61, 61, 15)]
    [InlineData(75, 75, 18)]
    [InlineData(80, 80, 20)]
    [InlineData(90, 90, 22)]
    [InlineData(100, 100, 25)]
    [InlineData(120, 120, 30)]
    [InlineData(150, 150, 37)]
    [InlineData(200, 200, 50)]
    [InlineData(300, 300, 75)]
    [InlineData(100, 60, 15)]
    [InlineData(60, 100, 15)]
    [InlineData(301, 120, 30)]
    public void RoundedRectangle_UsesMeasuredDiameter(int width, int height, int diameter) =>
        VBShape.RoundedCornerRadius(new Size(width, height)).Should().Be(diameter / 2d);

    [Theory]
    [InlineData(100, 60, 15)]
    [InlineData(60, 100, 15)]
    [InlineData(150, 150, 37)]
    public void RoundedSquare_UsesMeasuredDiameter(int width, int height, int diameter) =>
        VBShape.RoundedCornerRadius(new Size(width, height)).Should().Be(diameter / 2d);
}
