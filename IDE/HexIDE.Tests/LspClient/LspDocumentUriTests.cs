using HexIDE.Lsp;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// The measured case (#236) plus the counter-example that stops the fix regressing into
/// <c>OrdinalIgnoreCase</c>, which would trade a silent dropped diagnostic for a silent
/// mis-attributed one.
/// </summary>
public class LspDocumentUriTests
{
    [Fact]
    public void TheMeasuredCase_AServerLowercasingTheWindowsDriveLetter_StillMatches()
    {
        // Measured against a real third-party LSP server: client sent `C:`, server answered `c:`.
        // Before the fix this returned false and every diagnostic from that server was discarded.
        LspDocumentUri.AreSame("file:///C:/Users/dev/Clean.bas", "file:///c:/Users/dev/Clean.bas")
            .Should().Be(OperatingSystem.IsWindows(),
                "Windows filesystems are case-insensitive, so the two name one file — but on a "
              + "case-sensitive platform this URI shape is not meaningful and must not be assumed equal");
    }

    [Fact]
    public void PercentEncodingDoesNotDefeatTheMatch()
    {
        LspDocumentUri.AreSame("file:///c:/my%20project/Mod.bas", "file:///c:/my project/Mod.bas")
            .Should().BeTrue("a space and its escape name the same path");
    }

    [Fact]
    public void SchemeAndHostAreCaseInsensitivePerRfc3986()
    {
        LspDocumentUri.AreSame("VB6://module/Module1", "vb6://module/Module1").Should().BeTrue();
    }

    [Fact]
    public void TheVb6SchemeIgnoresCaseBecauseVb6IdentifiersDo()
    {
        LspDocumentUri.AreSame("vb6://module/Module1", "vb6://module/MODULE1").Should().BeTrue();
    }

    [Fact]
    public void DifferentDocumentsStillCompareUnequal()
    {
        // The counter-example that matters. A fix implemented as OrdinalIgnoreCase would pass every
        // test above and fail this one only on a case-sensitive filesystem — silently attributing
        // one file's diagnostics to another, which is worse than the bug being fixed.
        LspDocumentUri.AreSame("vb6://module/Module1", "vb6://module/Module2").Should().BeFalse();
        LspDocumentUri.AreSame("file:///c:/a/Mod.bas", "file:///c:/b/Mod.bas").Should().BeFalse();
        LspDocumentUri.AreSame("vb6://module/Module1", "vb6://form/Module1").Should().BeFalse();
    }

    [Fact]
    public void AnUnknownSchemeKeepsItsPathCaseSensitive()
    {
        // RFC 3986 says a path is case-sensitive unless its scheme says otherwise, and we do not
        // know this scheme. Guessing generously here is how a diagnostic lands on the wrong file.
        LspDocumentUri.AreSame("custom://x/Doc.md", "custom://x/doc.md").Should().BeFalse();
    }

    [Fact]
    public void AnUnparseableUriDegradesToAnExactMatchRatherThanThrowing()
    {
        LspDocumentUri.AreSame("not a uri at all", "not a uri at all").Should().BeTrue();
        LspDocumentUri.AreSame("not a uri at all", "also not a uri").Should().BeFalse();
    }

    [Fact]
    public void NullsAreHandledWithoutThrowing()
    {
        LspDocumentUri.AreSame(null, null).Should().BeTrue();
        LspDocumentUri.AreSame(null, "vb6://module/M").Should().BeFalse();
        LspDocumentUri.AreSame("vb6://module/M", null).Should().BeFalse();
    }

    [Fact]
    public void TheComparerHashesConsistentlyWithItsEquality()
    {
        // If these disagree, two URIs that AreSame land in different dictionary buckets and the
        // comparer silently stops working for precisely the inputs it exists to handle.
        var dict = new Dictionary<string, int>(LspDocumentUri.Comparer)
        {
            ["vb6://module/Module1"] = 1,
        };

        dict.ContainsKey("vb6://module/MODULE1").Should().BeTrue();
        dict["vb6://module/MODULE1"] = 2;
        dict.Should().HaveCount(1, "one document must not occupy two entries");
    }
}
