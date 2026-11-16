using HexIDE.Lsp;

namespace HexIDE.Tests.LspClient;

public sealed class ArchiveEntryUriTests
{
    [Fact]
    public void Round_trip_preserves_ordinal_segments_and_container()
    {
        var container = Path.Combine(Path.GetTempPath(), "A # project.twinproj");
        string[] segments = ["Sources", "a/b\\c", "Module # 1.twin"];
        var uri = ArchiveEntryUri.ForEntry(container, segments);
        uri.Should().Be("archive-entry:/Sources/a%2Fb%5Cc/Module%20%23%201.twin?container="
            + Uri.EscapeDataString(LspDocumentUri.ForFile(container)));
        ArchiveEntryUri.TryParse(uri, out var parsed, out var names).Should().BeTrue();
        parsed.Should().Be(LspDocumentUri.ForFile(container));
        names.Should().Equal(segments);
    }

    [Fact]
    public void Comparison_keeps_containers_and_segment_boundaries_distinct()
    {
        const string a = "archive-entry:/Sources/a%2Fb.twin?container=file%3A%2F%2F%2FC%3A%2FA.twinproj";
        LspDocumentUri.AreSame(a, a.Replace("A.twinproj", "B.twinproj")).Should().BeFalse();
        LspDocumentUri.AreSame(a, a.Replace("a%2Fb", "a/b")).Should().BeFalse();
        LspDocumentUri.AreSame(a, a.Replace("Sources", "sources")).Should().BeFalse();
        LspDocumentUri.AreSame(a, a.Replace("%2F", "%2f")).Should().BeTrue();
        var echo = a.Replace("C%3A", "c%3A");
        LspDocumentUri.AreSame(a, echo).Should().Be(OperatingSystem.IsWindows());
        if (OperatingSystem.IsWindows())
            LspDocumentUri.Comparer.GetHashCode(a).Should().Be(LspDocumentUri.Comparer.GetHashCode(echo));
    }

    [Theory]
    [InlineData("archive-entry:/Sources/M.twin?container=file%3A%2F%2F%2Ftmp%2FA.twinproj", ".twin")]
    [InlineData("archive-entry:/Sources/README?container=file%3A%2F%2F%2Ftmp%2FA.twinproj", null)]
    public void Routing_uses_the_entry_extension(string uri, string? extension) =>
        DocumentLanguage.ExtensionOf(uri).Should().Be(extension);

    [Fact]
    public void File_members_keep_file_uris() =>
        LspDocumentUri.ForFile(Path.Combine(Path.GetTempPath(), "M.bas")).Should().StartWith("file:");
}
