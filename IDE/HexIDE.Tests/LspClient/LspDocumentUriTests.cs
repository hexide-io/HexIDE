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
        LspDocumentUri.AreSame("UNTITLED:Project1/Module1.bas", "untitled:Project1/Module1.bas")
            .Should().BeTrue();
    }

    [Fact]
    public void TheUntitledSchemeIgnoresCaseBecauseVb6NamesDo()
    {
        // Both segments of untitled:<Project>/<Name>.<ext> are VB6 names, and VB6 compares names without
        // regard to case. Inherited from the vb6:// scheme this replaced, for the same reason.
        LspDocumentUri.AreSame("untitled:Project1/Module1.bas", "untitled:PROJECT1/MODULE1.bas")
            .Should().BeTrue();
    }

    [Fact]
    public void AnUntitledUriSurvivesNormalisationAtAll()
    {
        // untitled: has no authority, so it is not the hierarchical shape the file: cases exercise. Asserted
        // on its own because a scheme the URI parser handles differently would make every comparison above
        // pass for the wrong reason, or throw, and neither would be obvious from the cases that use it.
        LspDocumentUri.AreSame("untitled:Project1/Module1.bas", "untitled:Project1/Module1.bas")
            .Should().BeTrue();
        LspDocumentUri.AreSame("untitled:Project1/Module1.bas", "untitled:Project1/Module2.bas")
            .Should().BeFalse();
        LspDocumentUri.AreSame("untitled:Project1/Module1.bas", "untitled:Project2/Module1.bas")
            .Should().BeFalse();
        LspDocumentUri.AreSame("untitled:Project1/Module1.bas", "untitled:Project1/Module1.cls")
            .Should().BeFalse("the extension is what routes the document, so it distinguishes two of them");
    }

    [Fact]
    public void AnEncodedUntitledNameMatchesItsRawSpelling()
    {
        // HexIDE always sends the encoded form (#486). A push server echoes back what it was sent, so this
        // only matters for one that normalises — but that is exactly the case this class exists for.
        LspDocumentUri.AreSame("untitled:Pr%C3%B8jekt/Mod.bas", "untitled:Prøjekt/Mod.bas")
            .Should().BeTrue();
    }

    [Fact]
    public void DifferentDocumentsStillCompareUnequal()
    {
        // The counter-example that matters. A fix implemented as OrdinalIgnoreCase would pass every
        // test above and fail this one only on a case-sensitive filesystem — silently attributing
        // one file's diagnostics to another, which is worse than the bug being fixed.
        LspDocumentUri.AreSame("untitled:P/Module1.bas", "untitled:P/Module2.bas").Should().BeFalse();
        LspDocumentUri.AreSame("file:///c:/a/Mod.bas", "file:///c:/b/Mod.bas").Should().BeFalse();
        LspDocumentUri.AreSame("untitled:P/Module1.bas", "file:///c:/P/Module1.bas").Should().BeFalse();
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
        LspDocumentUri.AreSame(null, "untitled:P/M.bas").Should().BeFalse();
        LspDocumentUri.AreSame("untitled:P/M.bas", null).Should().BeFalse();
    }

    [Fact]
    public void TheComparerHashesConsistentlyWithItsEquality()
    {
        // If these disagree, two URIs that AreSame land in different dictionary buckets and the
        // comparer silently stops working for precisely the inputs it exists to handle.
        var dict = new Dictionary<string, int>(LspDocumentUri.Comparer)
        {
            ["untitled:Project1/Module1.bas"] = 1,
        };

        dict.ContainsKey("untitled:PROJECT1/MODULE1.bas").Should().BeTrue();
        dict["untitled:PROJECT1/MODULE1.bas"] = 2;
        dict.Should().HaveCount(1, "one document must not occupy two entries");
    }
}
