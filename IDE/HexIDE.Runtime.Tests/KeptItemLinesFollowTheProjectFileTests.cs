using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A member the project keeps only as its line (a form whose file is missing, one that failed to load, a UserDocument)
/// is named from wherever the project file is written, not from where it was read (#737). The line was written as it
/// was read, so a Save Project As to another folder left it naming a file beside the new project file, where there is
/// none, and the next open lost the member.
/// </summary>
/// <remarks>
/// Every expectation is the text a project file holds, so it is written out with backslashes on every host. The
/// folders are host paths, and are built with the host's API.
/// </remarks>
public class KeptItemLinesFollowTheProjectFileTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "hexide-keptlines-fixture");
    private static readonly string Old = Path.Combine(Root, "Old");

    private static ProjectDefinition Project(params string[] kept)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Test")
        {
            AbsolutePath = Path.Combine(Old, "Test.vbp"),
            ItemLinesDirectory = Old,
        };
        project.PreservedItemLines.AddRange(kept);
        return project;
    }

    private static string[] Lines(ProjectDefinition project, string projectPath) =>
        new ProjectSerializer().Serialize(project, projectPath).Split("\r\n");

    [Theory]
    [InlineData(@"Form=Broken.frm", @"Form=..\..\Old\Broken.frm")]
    [InlineData(@"Module=Mod1; Mod1.bas", @"Module=Mod1; ..\..\Old\Mod1.bas")]
    [InlineData(@"Class=Cls1; sub\Cls1.cls", @"Class=Cls1; ..\..\Old\sub\Cls1.cls")]
    [InlineData(@"UserControl=..\shared\Gauge.ctl", @"UserControl=..\..\shared\Gauge.ctl")]
    [InlineData(@"PropertyPage=Page.pag", @"PropertyPage=..\..\Old\Page.pag")]
    [InlineData(@"UserDocument=Doc.dob", @"UserDocument=..\..\Old\Doc.dob")]
    public void A_kept_line_names_its_file_from_the_folder_the_project_is_saved_to(string kept, string expected)
    {
        Lines(Project(kept), Path.Combine(Root, "New", "Deeper", "Test.vbp")).Should().Contain(expected);
    }

    [Fact]
    public void A_kept_line_follows_the_project_file_up_or_down()
    {
        var project = Project(@"Form=Broken.frm");

        Lines(project, Path.Combine(Old, "Sub", "Test.vbp")).Should().Contain(@"Form=..\Broken.frm");
        Lines(project, Path.Combine(Root, "Test.vbp")).Should().Contain(@"Form=Old\Broken.frm");
    }

    [Fact]
    public void A_kept_line_saved_in_its_own_folder_is_written_as_it_was_read()
    {
        // Resolving and re-relativising would tidy these, and a project saved where it is must not change.
        var project = Project(@"Form=.\Odd.frm", @"Module=Mod1;Mod1.bas", @"form=lower.frm", @"Form=sub\..\Up.frm");

        Lines(project, Path.Combine(Old, "Test.vbp"))
            .Should().Contain([@"Form=.\Odd.frm", @"Module=Mod1;Mod1.bas", @"form=lower.frm", @"Form=sub\..\Up.frm"]);
    }

    [Fact]
    public void A_kept_line_whose_path_names_a_drive_or_a_root_is_written_as_it_was_read()
    {
        var project = Project(@"Form=C:\Elsewhere\Abs.frm", @"Form=\Rooted\R.frm", @"Form=\\server\share\U.frm");

        Lines(project, Path.Combine(Root, "New", "Test.vbp"))
            .Should().Contain([@"Form=C:\Elsewhere\Abs.frm", @"Form=\Rooted\R.frm", @"Form=\\server\share\U.frm"]);
    }

    [Fact]
    public void A_related_documents_original_line_follows_the_project_file_too()
    {
        // VB6 writes a non-code file as an ordinary Module= line, and HexIDE keeps that line as it was (#273).
        // The folder is "notes", not "docs": ci-skippable.sh reads a Path.Combine whose first literal names a folder at
        // the repository's root as a file the build reads, and "docs" is one.
        var project = Project();
        project.AddRelatedDocument(new RelatedDocumentDefinition(project, "Readme",
            Path.Combine(Old, "notes", "README.md"), @"Module=Readme; notes\README.md"));

        Lines(project, Path.Combine(Root, "New", "Test.vbp")).Should().Contain(@"Module=Readme; ..\Old\notes\README.md");
    }

    [Fact]
    public async Task A_project_saved_to_another_folder_and_saved_again_still_names_the_kept_file()
    {
        // The second save is the one that lost the member: by then the project's own path is the new one, and a line
        // read relative to that names a file beside the new project file.
        var dir = Path.Combine(Path.GetTempPath(), "hexide-keptlines-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var projectPath = Path.Combine(dir, "Test.vbp");
            await File.WriteAllTextAsync(projectPath, "Type=Exe\r\nForm=Missing.frm\r\nStartup=\"(None)\"\r\nName=\"Test\"\r\n",
                TestContext.Current.CancellationToken);
            var project = (await new ProjectLoader().LoadAsync(projectPath, new NullSink())).Project;
            project.ItemLinesDirectory.Should().Be(dir);

            var moved = Path.Combine(dir, "Moved", "Test.vbp");
            Lines(project, moved).Should().Contain(@"Form=..\Missing.frm");
            project.AbsolutePath = moved;
            Lines(project, moved).Should().Contain(@"Form=..\Missing.frm");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string error) { }
    }
}
