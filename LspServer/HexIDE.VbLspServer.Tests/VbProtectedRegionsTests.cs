// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

using static HexIDE.VbLspServer.Tests.WholeFileFixtures;

namespace HexIDE.VbLspServer.Tests;

/// <summary>
/// Which lines of a whole VB6 file the server treats as the IDE's alone: the header, and members'
/// attribute lines (hexide-io/HexIDE#273 task 3.10).
/// </summary>
/// <remarks>
/// These pin the server's copy of the rule on fixtures. Agreement with the IDE's own copy
/// (<c>ReadOnlyRegions</c>) is proved over the corpus by <c>BundledServerRespectsTheIdesRegionsTests</c> in
/// <c>HexIDE.Tests</c>, which drives the built server.
/// </remarks>
public class VbProtectedRegionsTests
{
    private static int[] ProtectedLines(string text)
    {
        var regions = VbProtectedRegions.Of(text);
        return Enumerable.Range(0, Lines(text).Length).Where(regions.IsProtected).ToArray();
    }

    [Fact]
    public void AFormsHeaderRunsFromItsVersionLineThroughItsAttributeRun()
    {
        var regions = VbProtectedRegions.Of(Form);

        regions.HeaderLineCount.Should().Be(FormHeaderLines);
        ProtectedLines(Form).Should().Equal(Enumerable.Range(0, FormHeaderLines));
    }

    [Fact]
    public void AClassesHeaderAndItsMembersAttributeLinesAreProtectedAndNothingElse()
    {
        var regions = VbProtectedRegions.Of(Class);

        regions.HeaderLineCount.Should().Be(ClassHeaderLines);
        ProtectedLines(Class).Should().Equal(Enumerable.Range(0, ClassHeaderLines).Concat(ClassMemberAttributeLines));
    }

    [Fact]
    public void AStandardModulesHeaderIsItsNameLine()
    {
        const string module = "Attribute VB_Name = \"Module1\"\r\nOption Explicit\r\n";

        VbProtectedRegions.Of(module).HeaderLineCount.Should().Be(1);
    }

    [Fact]
    public void AFormWithNoAttributeLinesEndsItsHeaderAtTheDesignerBlock()
    {
        // Three of the demo forms were written this way.
        var lines = Lines(Form).ToList();
        lines.RemoveRange(12, 5);
        var form = string.Join("\r\n", lines);

        VbProtectedRegions.Of(form).HeaderLineCount.Should().Be(12);
    }

    [Fact]
    public void CodeAloneHasNoHeaderAndNothingProtected()
    {
        const string code = "Option Explicit\r\nSub Main()\r\n    Debug.Print 1\r\nEnd Sub\r\n";

        VbProtectedRegions.Of(code).HeaderLineCount.Should().Be(0);
        ProtectedLines(code).Should().BeEmpty();
    }

    [Fact]
    public void BlankLinesInFrontOfTheHeaderAndInsideTheAttributeRunCountButNotAfterIt()
    {
        const string module =
            "\r\n" +                                  // 0
            "Attribute VB_Name = \"Module1\"\r\n" +   // 1
            "\r\n" +                                  // 2
            "Attribute VB_Description = \"x\"\r\n" +  // 3
            "\r\n" +                                  // 4
            "Option Explicit\r\n";                    // 5

        ProtectedLines(module).Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void AVariableCalledAttributeIsCode()
    {
        const string code = "Sub S()\r\nAttribute = 5\r\nAttribute(1) = 2\r\nEnd Sub\r\n";

        ProtectedLines(code).Should().BeEmpty();
    }

    [Fact]
    public void AnIndentedMemberAttributeLineIsStillOne()
    {
        const string code = "Public Property Get Total() As Long\r\n    Attribute Total.VB_UserMemId = 0\r\nEnd Property\r\n";

        ProtectedLines(code).Should().Equal(1);
    }

    [Fact]
    public void OnlyARunVb6GivesToTheLineAboveIsProtected()
    {
        // Task 4.1: a run of procedure attributes under a procedure's declaration, whatever they name, and a run
        // of VB_Var lines under a module-level variable or constant naming what it declares, or what a rename
        // left. An orphan is ordinary text.
        ProtectedLines(ClassWithOrphans).Should().Equal(
            Enumerable.Range(0, ClassWithOrphansHeaderLines).Concat(ClassWithOrphansDescribingLines));
    }

    [Fact]
    public void EachRuleOfTheDeclarationReaderDecidesItsOwnRun()
    {
        // The shapes the reviewer of the first 4.1 found unpinned on this side: each run in the fixture is
        // protected or not by one rule of the reader, or of what a line is, so breaking that rule moves exactly
        // that run. BundledServerRespectsTheIdesRegionsTests holds the IDE's copy to the same text.
        ProtectedLines(FormWithReaderShapes).Should().Equal(
            Enumerable.Range(0, FormWithReaderShapesHeaderLines).Concat(FormWithReaderShapesDescribingLines));
    }

    [Theory]
    [InlineData("Public Sub Go()\r\n\r\nAttribute Go.VB_Description = \"x\"\r\nEnd Sub\r\n")]
    [InlineData("Public Sub Go()\r\n' describes Go\r\nAttribute Go.VB_Description = \"x\"\r\nEnd Sub\r\n")]
    [InlineData("Public Sub Go()\r\n    Debug.Print 1\r\nAttribute Go.VB_Description = \"x\"\r\nEnd Sub\r\n")]
    [InlineData("Public Sub Go()\r\nAttribute VB_Description = \"x\"\r\nEnd Sub\r\n")]
    [InlineData("Public Sub Go()\r\nAttribute Go.VB_Description = \"x\"\r\nAttribute A.VB_VarDescription = \"y\"\r\nEnd Sub\r\n")]
    [InlineData("Public Total As Long\r\nAttribute Total.VB_Description = \"x\"\r\n")]
    [InlineData("Public Const Rate = 5\r\nAttribute Rate.VB_Description = \"x\"\r\n")]
    [InlineData("Public Sub Go()\r\nAttribute Go.VB_VarHelpID = -1\r\nEnd Sub\r\n")]
    [InlineData("Public Event Changed()\r\nAttribute Changed.VB_VarDescription = \"x\"\r\n")]
    [InlineData("Public Event Changed()\r\nAttribute Count.VB_VarDescription = \"x\"\r\n")]
    [InlineData("Public A As Long: Public Sub Go()\r\nAttribute A.VB_VarDescription = \"x\"\r\nEnd Sub\r\n")]
    [InlineData("Public A As Long, C As Long\r\nAttribute B.VB_VarDescription = \"x\"\r\n")]
    [InlineData("Public Tally As Long\r\nAttribute Count.VB_VarDescription = \"x\"\r\nPublic Count As Long\r\n")]
    [InlineData("Public Tally As Long\r\nAttribute Count.VB_VarDescription = \"x\"\r\nAttribute Total.VB_VarHelpID = 3\r\n")]
    [InlineData("Public Tally As Long\r\nAttribute Count.VB_VarDescription = \"x\"\r\nAttribute Tally.VB_VarHelpID = 3\r\n")]
    [InlineData("Public Sub Go()\r\nAttribute Go.VB_Description = \"x\"\r\nAttribute(1) = 2\r\nEnd Sub\r\n")]
    [InlineData("Public Sub Go()\r\n    Dim t As Long\r\nAttribute t.VB_VarDescription = \"x\"\r\nEnd Sub\r\n")]
    [InlineData("Public Sub Go()\r\nEnd Sub\r\nPublic Late As Long\r\nAttribute Late.VB_VarDescription = \"x\"\r\n")]
    [InlineData("Public Sub Foo(ByVal a As Long, _\r\nAttribute Foo.VB_Description = \"p07\"\r\nByVal b As Long)\r\nEnd Sub\r\n")]
    [InlineData("Public Sub Foo()\r\n' a comment _\r\nAttribute Foo.VB_Description = \"p07c\"\r\nEnd Sub\r\n")]
    public void ARunVb6DoesNotGiveToTheLineAboveIsNotProtected(string code)
    {
        ProtectedLines(code).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Sub Main()\r\nAttribute main.VB_Description = \"x\"\r\nEnd Sub\r\n")] // compared without case
    [InlineData("Public Sub Halt()\r\nAttribute Go.VB_Description = \"x\"\r\nEnd Sub\r\n")] // whatever it names
    [InlineData("Public Sub Halt()\r\nAttribute VB_Varnish.VB_Description = \"x\"\r\nEnd Sub\r\n")] // VB_Var only after the dot
    [InlineData("Private Static Function Twice(ByVal n As Long) As Long\r\nAttribute Twice.VB_UserMemId = 0\r\nEnd Function\r\n")]
    [InlineData("Friend Property Set Target(ByVal o As Object)\r\nAttribute Target.VB_Description = \"x\"\r\nEnd Property\r\n")]
    [InlineData("Public Event Changed(ByVal Value As Long)\r\nAttribute Changed.VB_Description = \"x\"\r\n")]
    [InlineData("Private Declare Function GetTickCount Lib \"kernel32\" () As Long\r\nAttribute GetTickCount.VB_Description = \"x\"\r\n")]
    [InlineData("Dim a As Long, b(1 To 3, 4) As String, c\r\nAttribute b.VB_VarDescription = \"x\"\r\nAttribute c.VB_VarDescription = \"y\"\r\n")]
    [InlineData("Global Count% ' how many\r\nAttribute Count.VB_VarDescription = \"x\"\r\n")]
    [InlineData("Private WithEvents mGame As Game\r\nAttribute mGame.VB_VarHelpID = -1\r\n")]
    [InlineData("Public A As Long: Public B As Long\r\nAttribute B.VB_VarDescription = \"x\"\r\n")]
    [InlineData("Public Const C1 = 1, C2 = 2\r\nAttribute C2.VB_VarDescription = \"x\"\r\n")]
    [InlineData("Public Spare As Long\r\nAttribute spare.vb_vardescription = \"x\"\r\n")] // VB_Var without case
    [InlineData("Public Tally As Long\r\nAttribute Count.VB_VarDescription = \"x\"\r\n")] // a rename left it
    [InlineData("Public Function Total(ByVal a As Long, _  \r\n    ByVal b As Long) As Currency\r\nAttribute Total.VB_Description = \"x\"\r\nEnd Function\r\n")]
    public void ARunVb6GivesToTheLineAboveIsProtected(string code)
    {
        var attribute = Lines(code).Select((line, i) => (line, i)).First(l => l.line.StartsWith("Attribute", StringComparison.Ordinal)).i;
        var last = Lines(code).Select((line, i) => (line, i)).Last(l => l.line.StartsWith("Attribute", StringComparison.Ordinal)).i;

        ProtectedLines(code).Should().Equal(Enumerable.Range(attribute, last - attribute + 1));
    }

    [Fact]
    public void AnAttributeLineContinuedWithAnUnderscoreIsProtectedWhole()
    {
        // Oracle probes p11a and p11b: VB6 reads each as one attribute line, the name on the second physical line
        // in the second.
        const string code =
            "Public Sub Foo()\r\n" +                    // 0
            "Attribute Foo.VB_Description = _\r\n" +    // 1
            "\"p11a\"\r\n" +                             // 2
            "Attribute _\r\n" +                         // 3
            "Foo.VB_HelpID = 3\r\n" +                   // 4
            "End Sub\r\n";                              // 5

        ProtectedLines(code).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void ARangeEndingAtTheStartOfALineDoesNotCoverThatLine()
    {
        var regions = VbProtectedRegions.Of(Form);

        regions.Touches(Range(FormHeaderLines, 0, FormHeaderLines + 1, 0)).Should().BeFalse();
        regions.Touches(Range(FormHeaderLines - 1, 0, FormHeaderLines, 0)).Should().BeTrue();
        regions.Touches(Range(11, 2, FormHeaderLines + 2, 3)).Should().BeTrue();
    }

    [Fact]
    public void TheNamesADesignerBlockDeclaresAreTheFormsAndItsControls()
    {
        var regions = VbProtectedRegions.Of(Form);

        regions.Declares("frmOrders").Should().BeTrue();
        regions.Declares("CMDOK").Should().BeTrue("VB6 compares names ignoring case");
        regions.Declares("Caption").Should().BeFalse("a property is not a declaration");
        regions.Declares("Font").Should().BeFalse("BeginProperty opens a property, not a control");
        regions.Declares("CommandButton").Should().BeFalse();
        VbProtectedRegions.Of(Class).Declares("Order").Should().BeFalse("a class's BEGIN block declares nothing");
    }

    [Fact]
    public void BlankingEmptiesEveryProtectedLineAndLeavesEveryOtherCharacterWhereItWas()
    {
        var regions = VbProtectedRegions.Of(Class);

        var blanked = regions.Blank(Class);

        var before = Lines(Class);
        var after = Lines(blanked);
        after.Should().HaveSameCount(before);
        for (var i = 0; i < before.Length; i++)
            after[i].Should().Be(regions.IsProtected(i) ? "" : before[i], $"line {i}");
        blanked.Count(c => c == '\r').Should().Be(Class.Count(c => c == '\r'), "every line keeps its CRLF");
    }

    [Theory]
    [InlineData("Attribute Total.VB_Description = \"x\"", 10, true)]
    [InlineData("attribute total.VB_Description = \"x\"", 10, true)]
    [InlineData("    Attribute Total.VB_UserMemId = 0", 14, true)]
    [InlineData("Attribute Total = 1", 10, false)]
    [InlineData("x = Total.VB_Description", 4, false)]
    public void OnlyTheMemberNameInAnAttributeLineIsItsOwnQualifier(string line, int start, bool expected)
    {
        VbProtectedRegions.IsOwnQualifier(line, Range(0, start, 0, start + "Total".Length), "Total")
            .Should().Be(expected);
    }

    [Fact]
    public void EveryCorpusFileEndsItsHeaderOnItsLastAttributeLineOrItsBlocksEnd()
    {
        // The server's copy of the rule against every whole file the repository carries. Where it would go
        // wrong is at the edges -- a header ending a line early leaves its last attribute line to be
        // formatted, a line late takes the first line of code away from the developer.
        var files = CorpusFiles().ToList();
        files.Should().NotBeEmpty("the demo projects and corpus/designer ship with the repository");

        foreach (var path in files)
        {
            var text = File.ReadAllText(path);
            var lines = Lines(text);
            var header = VbProtectedRegions.Of(text).HeaderLineCount;

            header.Should().BeGreaterThan(0, path + " is a whole VB6 file");
            var last = lines[header - 1].Trim();
            (last.StartsWith("Attribute ", StringComparison.OrdinalIgnoreCase)
             || last.Equals("End", StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue($"{path}: the header's last line is '{last}'");
            lines.Skip(header).FirstOrDefault(l => l.Trim().Length > 0)?.Trim()
                .StartsWith("Attribute ", StringComparison.OrdinalIgnoreCase)
                .Should().NotBe(true, $"{path}: an attribute line was left out of the header");
        }
    }

    private static LspRange Range(int startLine, int startCharacter, int endLine, int endCharacter) =>
        new(new LspPosition(startLine, startCharacter), new LspPosition(endLine, endCharacter));

    private static IEnumerable<string> CorpusFiles()
    {
        string[] extensions = [".frm", ".cls", ".bas", ".ctl", ".pag"];
        foreach (var root in new[] { "demo", Path.Combine("corpus", "designer") })
        {
            if (FindUpwards(root) is not { } dir)
                continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                if (extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    yield return file;
            }
        }
    }

    private static string? FindUpwards(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
