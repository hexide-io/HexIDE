using System.Linq;
using HexIDE.IDE;

namespace HexIDE.Tests.IDE;

public class LayoutManifestJsonTests
{
    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new LayoutManifest(LayoutManifest.CurrentVersion, 0.1, 0.3, new ToolLayoutState[]
        {
            new("toolbox",       true,  DockRegion.Left,     0, 0.0625),
            new("project",       true,  DockRegion.Right,    0, 0.25),
            new("immediate",     true,  DockRegion.Bottom,   0, 0.5),
            new("objectBrowser", false, DockRegion.Document, 0, null),
        });

        var back = LayoutManifestJson.Deserialize(LayoutManifestJson.Serialize(original));

        back.Should().NotBeNull();
        back!.Version.Should().Be(LayoutManifest.CurrentVersion);
        back.LeftProportion.Should().Be(0.1);
        back.RightProportion.Should().Be(0.3);
        back.Tools.Should().Equal(original.Tools);
    }

    [Fact]
    public void Deserialize_LegacyV1File_MigratesOntoDefaultPreservingProportions()
    {
        var v1 = "{ \"leftProportion\": 0.12, \"rightProportion\": 0.34 }";

        var m = LayoutManifestJson.Deserialize(v1);

        m.Should().NotBeNull();
        m!.Version.Should().Be(LayoutManifest.CurrentVersion);
        m.LeftProportion.Should().Be(0.12);
        m.RightProportion.Should().Be(0.34);
        m.Tools.Should().Equal(LayoutManifest.Default.Tools);
    }

    [Theory]
    [InlineData("{ not valid json")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Deserialize_EmptyOrCorrupt_ReturnsNull(string? json)
    {
        LayoutManifestJson.Deserialize(json).Should().BeNull();
    }

    [Fact]
    public void Default_HasElevenToolsWithExpectedHomes()
    {
        var d = LayoutManifest.Default;

        d.Version.Should().Be(LayoutManifest.CurrentVersion);
        d.Tools.Should().HaveCount(11);

        d.Tools.Single(t => t.Key == "toolbox").Region.Should().Be(DockRegion.Left);
        d.Tools.Single(t => t.Key == "properties").Region.Should().Be(DockRegion.Right);
        d.Tools.Single(t => t.Key == "properties").Order.Should().Be(1);
        d.Tools.Single(t => t.Key == "immediate").Region.Should().Be(DockRegion.Bottom);
        d.Tools.Single(t => t.Key == "callStack").Region.Should().Be(DockRegion.Bottom);
        d.Tools.Single(t => t.Key == "callStack").Open.Should().BeFalse();
        d.Tools.Single(t => t.Key == "objectBrowser").Region.Should().Be(DockRegion.Document);

        // Default open-state: the four left/right built-ins are open; debug + document tools start closed.
        d.Tools.Where(t => t.Open).Select(t => t.Key)
            .Should().BeEquivalentTo("toolbox", "project", "properties", "formLayout");
    }

    [Fact]
    public void Serialize_UsesCamelCaseKeysAndStringEnums()
    {
        var json = LayoutManifestJson.Serialize(LayoutManifest.Default);

        json.Should().Contain("\"version\": 2");
        json.Should().Contain("\"region\": \"left\"");
        json.Should().Contain("\"region\": \"document\"");
    }
}
