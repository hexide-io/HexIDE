using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.Projects;

public class ProjectFileClassifierFormatTests
{
    [Theory]
    [InlineData(true, "C.xcls", ProjectFileKind.ClassModule)]
    [InlineData(true, "C.XCLS", ProjectFileKind.ClassModule)]
    [InlineData(true, "F.xfrm", ProjectFileKind.Form)]
    [InlineData(true, "F.XFRM", ProjectFileKind.Form)]
    [InlineData(true, "notes.md", ProjectFileKind.RelatedDocument)]
    [InlineData(true, "LICENSE", ProjectFileKind.RelatedDocument)]
    [InlineData(true, "C.cls", ProjectFileKind.RelatedDocument)]
    [InlineData(true, "F.frm", ProjectFileKind.RelatedDocument)]
    [InlineData(false, "F.frm", ProjectFileKind.Form)]
    [InlineData(false, "M.bas", ProjectFileKind.StandardModule)]
    [InlineData(false, "C.cls", ProjectFileKind.ClassModule)]
    [InlineData(false, "U.ctl", ProjectFileKind.UserControl)]
    [InlineData(false, "P.pag", ProjectFileKind.PropertyPage)]
    [InlineData(false, "D.dsr", ProjectFileKind.RelatedDocument)]
    [InlineData(false, "D.dob", ProjectFileKind.RelatedDocument)]
    [InlineData(false, "LICENSE", ProjectFileKind.RelatedDocument)]
    public void Classification_uses_the_formats_extensions(bool custom, string path, ProjectFileKind expected)
    {
        var format = custom
            ? (IProjectFileFormat)new ProjectFileFormatSeamTests.StandInFormat { FormExtension = "xfrm", ClassExtension = "xcls" }
            : new Vb6ProjectFileFormat();

        ProjectFileClassifier.Classify(path, format).Should().Be(expected);
    }
}
