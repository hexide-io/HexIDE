using HexIDE.Addins;

namespace HexIDE.Tests.Addins;

/// <summary>The embedded anchors must match the fingerprints published in TRUST.md.</summary>
public class TrustAnchorsTests
{
    [Fact]
    public void Embedded_anchor_fingerprints_match_the_published_values()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TRUST.md")))
            root = root.Parent;
        root.Should().NotBeNull();
        var rows = File.ReadAllLines(Path.Combine(root!.FullName, "TRUST.md"));
        var publishedRoot = rows.Single(line => line.StartsWith("| **HexIDE root** |", StringComparison.Ordinal)).Split('`')[1];
        var publishedFirstParty = rows.Single(line => line.StartsWith("| **First-party publisher**", StringComparison.Ordinal)).Split('`')[3];

        TrustAnchors.RootFingerprint.Should().Be(publishedRoot);
        TrustAnchors.FirstPartyFingerprint.Should().Be(publishedFirstParty);
    }
}
