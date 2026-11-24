using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Tests;

public class VBFontSizingTests
{
    // Design-time measurements at 96 dpi: docs/vb6-fidelity-oracle/the-properties-windows-font-dialog.md (#4153).
    [Theory]
    [InlineData(2048, 2048.25)]
    [InlineData(10, 9.75)]
    [InlineData(1, 1.5)]
    public void SnapDialogSize_matches_the_measured_dialog_rounding(double size, double expected)
    {
        VBFontSizing.SnapDialogSize(size).Should().Be(expected);
    }

    // Runtime measurements at 96 dpi: docs/vb6-fidelity-oracle/a-fonts-fontsize-fontbold-and-the-rest.md (#1343).
    [Theory]
    [InlineData(9.375, 13)]
    [InlineData(11.625, 16)]
    [InlineData(10, 13)]
    public void RoundToPixels_matches_the_measured_pixel_heights(double size, int expected)
    {
        VBFontSizing.RoundToPixels(size).Should().Be(expected);
    }
}
