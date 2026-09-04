using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Add File decides what a picked file becomes purely from its extension. Two of those decisions
/// deliberately disagree with <see cref="SerializedProject.IsVb6CodeFile"/>, which answers a similar-looking
/// question about a different situation — those two are the reason this file exists, since a later reader
/// comparing the tables would otherwise reasonably "fix" one to match the other.
/// </summary>
public class ProjectFileClassifierTests
{
    [Theory]
    [InlineData("Form1.frm", ProjectFileKind.Form)]
    [InlineData("Module1.bas", ProjectFileKind.StandardModule)]
    [InlineData("Widget.cls", ProjectFileKind.ClassModule)]
    [InlineData("Gauge.ctl", ProjectFileKind.UserControl)]
    [InlineData("Props.pag", ProjectFileKind.PropertyPage)]
    public void AVb6SourceFileIsClassifiedByItsExtension(string path, ProjectFileKind expected) =>
        ProjectFileClassifier.Classify(path).Should().Be(expected);

    [Theory]
    [InlineData("README.MD", ProjectFileKind.RelatedDocument)]
    [InlineData("Form1.FRM", ProjectFileKind.Form)]
    [InlineData("module1.BAS", ProjectFileKind.StandardModule)]
    public void ClassificationIsCaseInsensitive(string path, ProjectFileKind expected) =>
        ProjectFileClassifier.Classify(path).Should().Be(expected,
            "Windows hands back whatever case the file was created with");

    [Theory]
    [InlineData("README.md")]
    [InlineData("notes.txt")]
    [InlineData("build.ps1")]
    [InlineData("app.manifest")]
    public void AnythingThatIsNotVb6SourceJoinsAsARelatedDocument(string path) =>
        ProjectFileClassifier.Classify(path).Should().Be(ProjectFileKind.RelatedDocument);

    [Theory]
    [InlineData("Doc.dob")]
    [InlineData("Designer.dsr")]
    public void AnUnmodelledVb6FileJoinsAsARelatedDocumentDespiteBeingSource(string path)
    {
        // The first deliberate divergence. These ARE VB6 source and IsVb6CodeFile rightly says so — but
        // HexIDE models neither, so classifying one as a module would promise a designer that does not
        // exist and hand the save path a file nothing ever parsed. As a related document it opens as text
        // and is written back verbatim, which is the honest outcome rather than the flattering one.
        SerializedProject.IsVb6CodeFile(path).Should().BeTrue("the divergence is the point of this test");
        ProjectFileClassifier.Classify(path).Should().Be(ProjectFileKind.RelatedDocument);
    }

    [Theory]
    [InlineData("LICENSE")]
    [InlineData("Makefile")]
    public void AFileWithNoExtensionJoinsAsARelatedDocument(string path)
    {
        // The second deliberate divergence, and it runs the other way round. Reading a
        // "Module=Foo; somefile" line means the PROJECT already claims the file is source, and the
        // conservative move is to believe an existing claim. Add File has no claim to preserve: the
        // developer picked a file out of a dialog, and an extensionless one is far likelier to be a LICENSE
        // than VB6 source.
        SerializedProject.IsVb6CodeFile(path).Should().BeTrue("an existing claim is believed on read");
        ProjectFileClassifier.Classify(path).Should().Be(ProjectFileKind.RelatedDocument);
    }

    [Theory]
    [InlineData(ProjectFileKind.StandardModule, ModuleKind.StandardModule)]
    [InlineData(ProjectFileKind.ClassModule, ModuleKind.ClassModule)]
    [InlineData(ProjectFileKind.UserControl, ModuleKind.UserControl)]
    [InlineData(ProjectFileKind.PropertyPage, ModuleKind.PropertyPage)]
    public void AModuleKindMapsToItsModuleKind(ProjectFileKind kind, ModuleKind expected) =>
        kind.AsModuleKind().Should().Be(expected);

    [Theory]
    [InlineData(ProjectFileKind.Form)]
    [InlineData(ProjectFileKind.RelatedDocument)]
    public void AKindThatIsNotAModuleMapsToNothing(ProjectFileKind kind) =>
        kind.AsModuleKind().Should().BeNull(
            "a form and a related document each have their own door; collapsing them into the module one "
          + "is exactly the mistake #245 was");
}
