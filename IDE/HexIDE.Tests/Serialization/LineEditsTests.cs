using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Serialization;

/// <summary>
/// Reducing a server's rewrite of a buffer to the lines it changes, less those in a read-only region
/// (hexide-io/HexIDE#273 phase 3: the formatting row of the design record's writer policy).
/// </summary>
public class LineEditsTests
{
    private const string Designer =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        "   Begin VB.CommandButton Command1 \r\n" +
        "      Caption         =   \"OK\"\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    private static IReadOnlyList<TextRegion> RegionsOf(string text) =>
        ReadOnlyRegions.Of(text, text.StartsWith("VERSION", StringComparison.Ordinal)
            ? text.IndexOf("Attribute", StringComparison.Ordinal)
            : 0);

    // -- The measured case -------------------------------------------------------------------------------

    [Fact]
    public void AWholeDocumentRewriteThatFlattensTheHeaderLeavesTheHeaderAlone()
    {
        // The bundled server's answer, as captured on the wire: one edit replacing the whole document, every
        // designer line re-indented to column zero, and \n throughout. Applied as sent it cost a form its
        // attribute block and sixteen lines of code.
        const string code = "Option Explicit\r\n\r\nprivate sub Form_Load()\r\nx = 1\r\nend sub\r\n";
        var before = Designer + code;
        var after = (string.Join("\n", Designer.Split("\r\n").Select(l => l.TrimStart())) +
                     "Option Explicit\n\nPrivate Sub Form_Load()\n    x = 1\nEnd Sub\n");

        var reduction = LineEdits.Reduce(before, after, RegionsOf(before));
        var result = LineEdits.Apply(before, reduction.Changes);

        result.Should().Be(Designer + "Option Explicit\r\n\r\nPrivate Sub Form_Load()\r\n    x = 1\r\nEnd Sub\r\n",
            "the code is formatted, the header is untouched, and every line keeps the buffer's own terminator");
        reduction.DroppedLines.Should().BeGreaterThan(0, "the header's re-indent was dropped, not applied");
    }

    [Fact]
    public void ALineEndingDifferenceAloneIsNotAChange()
    {
        // A server answering in \n for a \r\n buffer has changed no line. Treating it as a change would
        // rewrite every line of the document, the header included.
        var before = Designer + "Option Explicit\r\n";

        var reduction = LineEdits.Reduce(before, before.Replace("\r\n", "\n"), RegionsOf(before));

        reduction.Changes.Should().BeEmpty();
        reduction.DroppedLines.Should().Be(0);
    }

    // -- Where a change meets a region -------------------------------------------------------------------

    [Fact]
    public void AChangeRunningFromTheHeaderIntoTheCodeKeepsItsCodeHalf()
    {
        // The last header line and the first code line change together, so the diff sees one run. Paired line
        // for line, it is taken apart: the code line is formatted and the header line is not.
        var before = Designer + "option explicit\r\n";
        var after = Designer.Replace("Attribute VB_Name", "attribute VB_Name") + "Option Explicit\r\n";

        var reduction = LineEdits.Reduce(before, after, RegionsOf(before));

        LineEdits.Apply(before, reduction.Changes).Should().Be(Designer + "Option Explicit\r\n");
        reduction.DroppedLines.Should().Be(1);
    }

    [Fact]
    public void ARunThatAddsLinesAndReachesARegionIsDroppedWhole()
    {
        // Lines added or removed cannot be paired with the lines they replace, so there is no way to tell which
        // of them belongs to the region. The header is kept and the run is not applied.
        var before = Designer + "Option Explicit\r\n";
        var after = Designer.Replace("End\r\nAttribute", "End\r\n' inserted\r\nAttribute") + "Option Explicit\r\n";

        var reduction = LineEdits.Reduce(before, after, RegionsOf(before));

        reduction.Changes.Should().BeEmpty();
        reduction.DroppedLines.Should().Be(1);
    }

    [Fact]
    public void NothingIsInsertedBetweenADeclarationAndTheAttributesDescribingIt()
    {
        const string header = "Attribute VB_Name = \"Tide\"\r\n";
        var before = header +
                     "Public Property Get Value() As Long\r\n" +
                     "Attribute Value.VB_UserMemId = 0\r\n" +
                     "Value = 1\r\n" +
                     "End Property\r\n";
        var after = before.Replace("As Long\r\nAttribute", "As Long\r\n' between\r\nAttribute")
                          .Replace("Value = 1", "    Value = 1");

        var reduction = LineEdits.Reduce(before, after, ReadOnlyRegions.Of(before, header.Length));

        LineEdits.Apply(before, reduction.Changes)
            .Should().Be(before.Replace("Value = 1", "    Value = 1"),
                "the body is formatted and the attribute run stays against its declaration");
    }

    [Fact]
    public void ARewriteThatDeletesTheLineARunDescribesIsDropped()
    {
        // The hunk ends where the run starts, so it does not touch the run; but it removes the line break
        // the run hangs from, and the run would then describe whatever line was above (#273 task 3.11).
        const string header = "Attribute VB_Name = \"Tide\"\r\n";
        var before = header +
                     "Option Explicit\r\n" +
                     "Public Property Get Value() As Long\r\n" +
                     "Attribute Value.VB_UserMemId = 0\r\n" +
                     "End Property\r\n";
        var after = before.Replace("Public Property Get Value() As Long\r\n", "");

        var reduction = LineEdits.Reduce(before, after, ReadOnlyRegions.Of(before, header.Length));

        LineEdits.Apply(before, reduction.Changes).Should().Be(before);
        reduction.DroppedLines.Should().Be(1);
    }

    [Fact]
    public void AHunkThatRewritesADescribedDeclarationAndKeepsItsLineBreakIsKept()
    {
        // One hunk: a line added above the declaration and the declaration itself rewritten. The hunk ends
        // where the run starts, and its replacement ends with a line break, so the run still hangs from the
        // declaration. Refusing it would lose formatting for nothing.
        const string header = "Attribute VB_Name = \"Tide\"\r\n";
        var before = header +
                     "Option Explicit\r\n" +
                     "public property get Value() as long\r\n" +
                     "Attribute Value.VB_UserMemId = 0\r\n" +
                     "End Property\r\n";
        var after = before.Replace("public property get Value() as long\r\n",
            "' Value\r\nPublic Property Get Value() As Long\r\n");

        var reduction = LineEdits.Reduce(before, after, ReadOnlyRegions.Of(before, header.Length));

        LineEdits.Apply(before, reduction.Changes).Should().Be(after);
        reduction.DroppedLines.Should().Be(0);
    }

    [Fact]
    public void ALineInsertedAfterAnAttributeRunIsKept()
    {
        const string header = "Attribute VB_Name = \"Tide\"\r\n";
        var before = header + "Option Explicit\r\n";
        var after = header + "Option Explicit\r\n' added\r\n";

        LineEdits.Apply(before, LineEdits.Reduce(before, after, ReadOnlyRegions.Of(before, header.Length)).Changes)
            .Should().Be(after);
    }

    // -- The diff itself ---------------------------------------------------------------------------------

    [Fact]
    public void WithNoRegionTheChangesReproduceTheRewriteExactly()
    {
        // The reduction is only as good as the alignment under it, and a hand-picked case cannot exercise a
        // diff. With nothing to protect, applying what it returns must give the server's text back byte for
        // byte, across insertions, deletions, replacements and a missing final newline at either end. A small
        // vocabulary makes repeated lines common, which is where an alignment goes wrong.
        var words = new[] { "Dim a", "a = 1", "", "End Sub", "Sub Main()", "' note", "x = x + 1", "If x Then" };
        for (var seed = 0; seed < 300; seed++)
        {
            var random = new Random(seed);
            string Make(int lines) =>
                string.Join("\n", Enumerable.Range(0, lines).Select(_ => words[random.Next(words.Length)]))
                + (random.Next(2) == 0 ? "\n" : "");
            var before = Make(random.Next(0, 40));
            var after = random.Next(3) == 0 ? before : Make(random.Next(0, 40));

            LineEdits.Apply(before, LineEdits.Reduce(before, after, []).Changes)
                .Should().Be(after, $"seed {seed}");
        }
    }

    [Fact]
    public void AndInACrlfBufferTheLinesItWritesAreCrlf()
    {
        // The server answered in \n; what lands in a \r\n buffer must not mix the two.
        const string before = "a\r\nb\r\nc\r\n";

        LineEdits.Apply(before, LineEdits.Reduce(before, "a\nB\nb2\nc\n", []).Changes)
            .Should().Be("a\r\nB\r\nb2\r\nc\r\n");
    }

    [Fact]
    public void PastTheDistanceCapWhatIsLeftIsOneChangeAndIsStillExact()
    {
        var before = string.Concat(Enumerable.Range(0, 1500).Select(i => $"old {i}\n"));
        var after = string.Concat(Enumerable.Range(0, 1500).Select(i => $"new {i}\n"));

        var reduction = LineEdits.Reduce(before, after, []);

        reduction.Changes.Should().ContainSingle();
        LineEdits.Apply(before, reduction.Changes).Should().Be(after);
    }

    [Fact]
    public void AndIfThatOneChangePairsLineForLineOnlyTheHeaderIsLeftOut()
    {
        // The same rewrite without the extra line: every old line has a new one opposite it, so the run is
        // taken line by line and the code is formatted even past the cap.
        var body = string.Concat(Enumerable.Range(0, 1500).Select(i => $"old {i}\r\n"));
        var before = Designer + body;
        var after = Designer.Replace("   ", "") + body.Replace("old", "new");

        LineEdits.Apply(before, LineEdits.Reduce(before, after, RegionsOf(before)).Changes)
            .Should().Be(Designer + body.Replace("old", "new"));
    }

    [Fact]
    public void AndIfThatOneChangeReachesTheHeaderNothingIsApplied()
    {
        // The fallback errs towards the header: a rewrite too large to align, which also adds a line so that it
        // cannot be paired line for line, is left undone rather than risked.
        var body = string.Concat(Enumerable.Range(0, 1500).Select(i => $"old {i}\r\n"));
        var before = Designer + body;
        var after = Designer.Replace("   ", "") + body.Replace("old", "new") + "extra\r\n";

        var reduction = LineEdits.Reduce(before, after, RegionsOf(before));

        reduction.Changes.Should().BeEmpty();
        reduction.DroppedLines.Should().BeGreaterThan(1500);
    }
}
