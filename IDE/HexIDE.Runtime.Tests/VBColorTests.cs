using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 2.6 — the numeric OLE_COLOR -> VBColor conversion used at the property boundary now that &amp;H
/// literals evaluate as numbers rather than colours.
/// </summary>
public class VBColorTests
{
    [Fact]
    public void FromOle_RawRgb_LowByteIsRed_ThirdByteIsBlue()
    {
        var red = VBColor.FromOle(0xFF);          // 0x000000FF
        red.Type.Should().Be(VBColor.ColorType.Raw);
        (red.R, red.G, red.B).Should().Be(((byte)255, (byte)0, (byte)0));

        var blue = VBColor.FromOle(0xFF0000);     // 0x00FF0000 (BGR order)
        (blue.R, blue.G, blue.B).Should().Be(((byte)0, (byte)0, (byte)255));
    }

    [Fact]
    public void FromOle_HighByte80_IsSystemColor()
    {
        var c = VBColor.FromOle(0x80000005);      // system colour, index 5
        c.Type.Should().Be(VBColor.ColorType.SystemColor);
    }

    // Regression (bug-hunt MED): a short "&H…" value made the fixed range slices throw ArgumentOutOfRangeException,
    // aborting form load. TryParse must return false for malformed input, never throw.
    [Theory]
    [InlineData("&H0&")]
    [InlineData("&HFF")]
    [InlineData("&H")]
    [InlineData("&h8000")]
    public void TryParse_ShortValue_ReturnsFalseWithoutThrowing(string str)
    {
        var act = () => VBColor.TryParse(str, out _);
        act.Should().NotThrow();
        VBColor.TryParse(str, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_FullValue_StillParses()
    {
        VBColor.TryParse("&H00FF8040&", out var c).Should().BeTrue();
        (c.R, c.G, c.B).Should().Be(((byte)0x40, (byte)0x80, (byte)0xFF));
    }
}
