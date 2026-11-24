using System;
using HexIDE.Converters;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Tests.Converters;

public class VBColorBrushesTests
{
    [Fact]
    public void System_color_keys_match_the_provider()
    {
        foreach (var color in Enum.GetValues<VbSystemColor>())
            VBColorBrushes.GetKey(color).Should().Be(VBColorExtensions.GetKey(color), "both map {0}", color);
    }
}
