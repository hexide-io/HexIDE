using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A project can carry files it does not compile — a README, a spec, notes beside the source. VB6 writes
/// these as <c>RelatedDoc=</c>, and writes them as ordinary <c>Module=</c> lines whenever its "Add As
/// Related Document" tickbox is left unticked, which is its default.
///
/// <para>
/// The round-trip corpus cannot guard any of this: the VB98 template tree contains no <c>RelatedDoc=</c>
/// line at all, so a regression here would pass the corpus gate silently. These fixtures are the only
/// cover.
/// </para>
/// </summary>
public class RelatedDocumentSerializationTests
{
    private class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string error) { }
    }

    private static SerializedProject Read(string vbp) =>
        new ProjectDeserializer().Deserialize(vbp, NullSink.Instance);

    [Fact]
    public void AProjectCarryingRelatedDocsRoundTripsByteForByte()
    {
        // THE trap in this change. `RelatedDoc=` used to fall through to the unknown-line channel, which
        // round-tripped it perfectly by accident. Promoting it to a known key means it must now be counted
        // on read and re-emitted through the same counter on write — get either half wrong and the line
        // either double-emits, or every unknown line after it shifts by one position.
        var original =
            "Type=Exe\r\n"
          + "Module=Module1; Module1.bas\r\n"
          + "RelatedDoc=notes.md\r\n"
          + "RelatedDoc=docs\\README.md\r\n"
          + "Startup=\"Sub Main\"\r\n"
          + "HelpFile=\"\"\r\n"
          + "Name=\"TestProject\"\r\n";

        var read = Read(original);
        var project = new ProjectDefinition(read.ProjectType, read.Name!) { AbsolutePath = @"C:\proj\Test.vbp" };
        foreach (var (name, path, _) in read.RelativeModulePaths)
            project.AddModule(new ModuleDefinition(project, name, ModuleKind.StandardModule)
            {
                AbsolutePath = Path.Combine(@"C:\proj", path),
            });
        foreach (var (name, path, line) in read.RelativeRelatedDocPaths)
            project.AddRelatedDocument(
                new RelatedDocumentDefinition(project, name, Path.Combine(@"C:\proj", path), line));
        project.StartsAtSubMain = true;
        foreach (var unknown in read.UnknownPreSectionLines) project.UnknownPreSectionLines.Add(unknown);

        var written = new ProjectSerializer().Serialize(project, project.AbsolutePath!);

        written.Should().Be(original,
            "an unknown line after a RelatedDoc= entry must not shift, and the entry must not double-emit");
    }

    [Fact]
    public void ARelatedDocIsReadAsOneAndNotAsAModule()
    {
        var read = Read("Type=Exe\r\nRelatedDoc=docs\\README.md\r\nName=\"P\"\r\n");

        read.RelativeModulePaths.Should().BeEmpty("a related document is not compiled");
        var (name, path, originalLine) = read.RelativeRelatedDocPaths.Should().ContainSingle().Subject;
        name.Should().Be("README.md", "a RelatedDoc= line carries no name field, so the filename is the name");
        path.Should().Be(@"docs\README.md");
        originalLine.Should().BeNull("it arrived on a RelatedDoc= line, so there is nothing to preserve");
    }

    [Theory]
    [InlineData("Module=Notes; notes.md")]
    [InlineData("Module=Spec; docs\\spec.txt")]
    [InlineData("Class=Readme; README.MD")]
    public void ANonCodeFileOnACodeLineIsReclassified(string itemLine)
    {
        // VB6's tickbox is not sticky and defaults off, so this is the COMMON shape for a file added
        // through its Add File dialog — not an edge case. Left as a module it is treated as VB6 source, and
        // Save Project prepends an Attribute VB_Name header to the user's prose (#245).
        var read = Read($"Type=Exe\r\n{itemLine}\r\nName=\"P\"\r\n");

        read.RelativeModulePaths.Should().BeEmpty("a .md is not something the interpreter should ever see");
        read.RelativeRelatedDocPaths.Should().ContainSingle()
            .Which.OriginalItemLine.Should().Be(itemLine,
                "reclassifying is an inference about intent; rewriting the project file on an inference is not");
    }

    [Theory]
    [InlineData("Module=Module1; Module1.bas")]
    [InlineData("Class=Widget; Widget.cls")]
    [InlineData("Module=NoExtension; somefile")]
    public void ARealCodeFileIsStillAModule(string itemLine)
    {
        // The control. Without it, a reclassification rule that swallowed everything would pass every test
        // above. The no-extension case is deliberate: reclassifying something we cannot classify would be a
        // guess on top of a guess, so it stays a module.
        var read = Read($"Type=Exe\r\n{itemLine}\r\nName=\"P\"\r\n");

        read.RelativeModulePaths.Should().ContainSingle();
        read.RelativeRelatedDocPaths.Should().BeEmpty();
    }

    [Fact]
    public void AReclassifiedLineIsWrittenBackExactlyAsItArrived()
    {
        // The fix for #245, stated as an invariant: opening and saving a project must not edit a line
        // HexIDE merely reinterpreted.
        const string itemLine = "Module=Notes; notes.md";
        var read = Read($"Type=Exe\r\n{itemLine}\r\nName=\"P\"\r\n");

        var project = new ProjectDefinition(read.ProjectType, read.Name!) { AbsolutePath = @"C:\proj\P.vbp" };
        foreach (var (name, path, line) in read.RelativeRelatedDocPaths)
            project.AddRelatedDocument(
                new RelatedDocumentDefinition(project, name, Path.Combine(@"C:\proj", path), line));

        var written = new ProjectSerializer().Serialize(project, project.AbsolutePath!);

        written.Should().Contain(itemLine)
            .And.NotContain("RelatedDoc=", "the line is preserved, not rewritten into the modern key");
    }

    [Fact]
    public void ADocumentAddedByTheDeveloperIsWrittenAsRelatedDoc()
    {
        // The other half: where membership genuinely changes, the modern key is what gets written.
        var project = new ProjectDefinition(VBProjectType.EXE, "P") { AbsolutePath = @"C:\proj\P.vbp" };
        project.AddRelatedDocument(
            new RelatedDocumentDefinition(project, "README.md", @"C:\proj\docs\README.md"));

        var written = new ProjectSerializer().Serialize(project, project.AbsolutePath!);

        written.Should().Contain(@"RelatedDoc=docs\README.md");
    }

    [Fact]
    public void ARelatedDocumentNeverEntersTheModuleCollection()
    {
        // The structural guarantee this whole shape exists for. Everything that could damage a non-code
        // file — the interpreter, the extension-based rename in Save As, the Attribute header writer —
        // iterates Modules. Being absent from that collection is what makes the damage unreachable, rather
        // than a guard somebody has to remember to add at each site.
        var read = Read("Type=Exe\r\nModule=Notes; notes.md\r\nRelatedDoc=other.md\r\nName=\"P\"\r\n");

        var project = new ProjectDefinition(read.ProjectType, read.Name!) { AbsolutePath = @"C:\proj\P.vbp" };
        foreach (var (name, path, line) in read.RelativeRelatedDocPaths)
            project.AddRelatedDocument(new RelatedDocumentDefinition(project, name, path, line));

        project.RelatedDocuments.Should().HaveCount(2);
        project.Modules.Should().BeEmpty();
    }
}
