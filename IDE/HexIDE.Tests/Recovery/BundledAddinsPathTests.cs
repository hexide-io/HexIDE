using System.IO;

namespace HexIDE.Tests.Recovery;

public class BundledAddinsPathTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mac_bundle_uses_Resources_with_or_without_a_trailing_separator(bool trailingSeparator)
    {
        var contents = Path.Combine(Path.GetTempPath(), "Probe.app", "Contents");
        var programs = Path.Combine(contents, "MacOS") + (trailingSeparator ? Path.DirectorySeparatorChar : "");

        BundledAddinsPath.For(programs, macOS: true).Should().Be(Path.Combine(contents, "Resources", "addins"));
    }

    [Theory]
    [InlineData(false, "Probe.app/Contents/MacOS")]
    [InlineData(true, "publish")]
    [InlineData(true, "Probe/Contents/MacOS")]
    [InlineData(true, "Probe.app/Other/MacOS")]
    public void Other_layouts_keep_addins_beside_the_program(bool macOS, string relative)
    {
        var programs = Path.Combine(Path.GetTempPath(), relative.Replace('/', Path.DirectorySeparatorChar));

        BundledAddinsPath.For(programs, macOS).Should().Be(Path.Combine(programs, "addins"));
    }
}
