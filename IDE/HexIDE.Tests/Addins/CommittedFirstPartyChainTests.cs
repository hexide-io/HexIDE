using System.IO;
using System.Text.Json;
using HexIDE.Addins;
using HexIDE.Tests.Infrastructure;

namespace HexIDE.Tests.Addins;

/// <summary>
/// The first-party chain committed in <c>HexIDE.AddinPacker/firstparty</c>, checked against the root and first-party keys
/// the IDE embeds, on every OS. Every release packs these records into its bundled add-ins, and their signatures cover
/// exact bytes: they were made over CRLF, so a checkout that turned them into LF shipped records the release's own root
/// refused, and AI Chat didn't load on macOS or Linux (#4529). The package tests generate their own chains, so these are
/// the only tests of the committed one.
/// </summary>
public class CommittedFirstPartyChainTests
{
    private static readonly string Folder = Path.Combine(RepoTree.Root(), "IDE", "HexIDE.AddinPacker", "firstparty");

    private static byte[] Bytes(string name) => File.ReadAllBytes(Path.Combine(Folder, name));

    private static string Signature(string name) => File.ReadAllText(Path.Combine(Folder, name)).Trim();

    [Fact]
    public void The_embedded_root_signs_the_committed_intermediate_record()
    {
        var root = TrustAnchors.RootPublicKey;
        root.Should().NotBeNull("HexIDE embeds its root key");
        using var key = PackageCrypto.ImportPublicKey(root!);
        var record = Bytes("intermediate.json");

        PackageCrypto.Verify(key, record, Signature("intermediate.sig")).Should().BeTrue(
            "the committed intermediate record has to be the bytes its signature covers on every checkout, and here it has "
            + "{0} CR and {1} LF bytes", Count(record, '\r'), Count(record, '\n'));
    }

    [Fact]
    public void The_committed_intermediate_signs_the_committed_first_party_publisher_record()
    {
        var intermediate = JsonSerializer.Deserialize(Bytes("intermediate.json"), AddinJsonContext.Default.IntermediateRecord);
        intermediate.Should().NotBeNull();
        using var key = PackageCrypto.ImportPublicKey(intermediate!.PublicKey);
        var record = Bytes("publisher.json");

        PackageCrypto.Verify(key, record, Signature("publisher.sig")).Should().BeTrue(
            "the committed publisher record has to be the bytes its signature covers on every checkout, and here it has "
            + "{0} CR and {1} LF bytes", Count(record, '\r'), Count(record, '\n'));

        var publisher = JsonSerializer.Deserialize(record, AddinJsonContext.Default.PublisherRecord);
        publisher.Should().NotBeNull();
        publisher!.IntermediateId.Should().Be(intermediate.IntermediateId);
        publisher.PublicKey.Should().Be(TrustAnchors.FirstPartyPublicKey,
            "the IDE labels an add-in first-party only when its publisher key is the embedded first-party key");
    }

    private static int Count(byte[] bytes, char c) => bytes.Count(b => b == (byte)c);
}
