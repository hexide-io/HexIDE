using System.IO;
using HexIDE.Recovery;

namespace HexIDE.Tests.Recovery;

/// <summary>
/// The recovery folder's layout (hexide-io/HexIDE#499 task 1.1): one folder per run, named by its process's start
/// time and id, whose run.json is written last and names the capture that is whole.
/// </summary>
public sealed class RecoveryRunTests : IDisposable
{
    private static readonly DateTime Started = new(2026, 10, 28, 13, 45, 12, 345, DateTimeKind.Utc);
    private readonly string root = Path.Join(Path.GetTempPath(), "hexide-recovery-run-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private static UnsavedProject Project(string moduleText, byte[]? companion = null) => new(
        "Test", @"C:\work\Test.vbp", "ABC123", new Vb6TextEncoding(1252),
        new UnsavedFile("Test.vbp", "Type=Exe\r\n"),
        [
            new("Form1", "Form", @"C:\work\Form1.frm", new Vb6TextEncoding(932),
                new UnsavedFile("Form1.frm", "Begin VB.Form Form1\r\n", companion is null ? null : "Form1.frx", companion)),
            new("Module1", "StandardModule", null, new Vb6TextEncoding(65001, Utf8Bom: true),
                new UnsavedFile("Module1.bas", moduleText)),
            new("Module2", "StandardModule", @"C:\work\Module2.bas", new Vb6TextEncoding(1252), null),
        ]);

    [Fact]
    public void A_run_s_folder_is_named_by_its_process_and_records_it()
    {
        var run = RecoveryRun.Begin(root, 4242, Started);

        Path.GetFileName(run.Folder).Should().Be("20261028T134512345Z-4242");
        var record = RecoveryRun.Read(run.Folder)!;
        record.Format.Should().Be(RecoveryRun.Format);
        record.ProcessId.Should().Be(4242);
        record.ProcessStart.Should().Be(Started, "kept to the tick, since an id alone is reused");
        record.Capture.Should().BeNull("nothing has been captured, which the next start can drop without asking");
        record.Projects.Should().BeEmpty();
    }

    [Fact]
    public void A_capture_is_written_whole_with_its_manifest()
    {
        var run = RecoveryRun.Begin(root, 4242, Started);

        run.Write([Project("Public Sub Ünsaved()\r\n", companion: [1, 2, 3])], Started.AddMinutes(1));

        var record = RecoveryRun.Read(run.Folder)!;
        record.Capture.Should().Be("s1");
        record.SavedAt.Should().Be(Started.AddMinutes(1));
        var project = record.Projects.Should().ContainSingle().Subject;
        project.Path.Should().Be(@"C:\work\Test.vbp");
        project.ProjectFileHash.Should().Be("ABC123");
        project.ProjectFile.Should().Be("s1/p1/Test.vbp");
        project.Documents.Select(d => (d.Name, d.File, d.Companion)).Should().Equal(
            ("Form1", "s1/p1/d1/Form1.frm", "s1/p1/d1/Form1.frx"),
            ("Module1", "s1/p1/d2/Module1.bas", null),
            ("Module2", null, null));
        project.Documents[0].CodePage.Should().Be(932);
        project.Documents[1].Utf8Bom.Should().BeTrue();

        File.ReadAllText(Path.Combine(run.Folder, "s1", "p1", "d2", "Module1.bas"))
            .Should().Be("Public Sub Ünsaved()\r\n", "a backup is UTF-8, so no character is refused");
        File.ReadAllBytes(Path.Combine(run.Folder, "s1", "p1", "d1", "Form1.frx")).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void A_newer_capture_replaces_the_older_one()
    {
        var run = RecoveryRun.Begin(root, 4242, Started);
        run.Write([Project("first")], Started);

        run.Write([Project("second")], Started);

        RecoveryRun.Read(run.Folder)!.Capture.Should().Be("s2");
        Directory.Exists(Path.Combine(run.Folder, "s1")).Should().BeFalse();
        File.ReadAllText(Path.Combine(run.Folder, "s2", "p1", "d2", "Module1.bas")).Should().Be("second");
    }

    [Fact]
    public void Once_nothing_is_unsaved_the_record_says_so_and_the_captures_go()
    {
        var run = RecoveryRun.Begin(root, 4242, Started);
        run.Write([Project("unsaved")], Started);

        run.Write([], Started);

        var record = RecoveryRun.Read(run.Folder)!;
        record.Capture.Should().BeNull();
        record.Projects.Should().BeEmpty();
        Directory.EnumerateDirectories(run.Folder).Should().BeEmpty();
    }

    [Fact]
    public void Deleting_a_run_removes_its_folder()
    {
        var run = RecoveryRun.Begin(root, 4242, Started);
        run.Write([Project("unsaved")], Started);

        run.Delete();

        Directory.Exists(run.Folder).Should().BeFalse();
    }

    [Theory]
    // Six levels: past the document's, project's and capture's own folders, the run's, and the root holding it.
    [InlineData("../../../../../../escaped.bas")]
    [InlineData(@"..\..\..\..\..\..\escaped.bas")]
    [InlineData("..")]
    public void A_file_name_that_climbs_out_is_written_inside_the_run(string fileName)
    {
        var run = RecoveryRun.Begin(root, 4242, Started);
        var project = new UnsavedProject("Test", null, null, new Vb6TextEncoding(1252), null,
            [new("Module1", "StandardModule", null, new Vb6TextEncoding(1252), new UnsavedFile(fileName, "text"))]);

        run.Write([project], Started);

        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Should().AllSatisfy(path => path.Should().StartWith(run.Folder + Path.DirectorySeparatorChar));
        var file = RecoveryRun.Read(run.Folder)!.Projects[0].Documents[0].File!;
        File.ReadAllText(Path.Combine(run.Folder, file)).Should().Be("text");
    }

    [Fact]
    public void Identical_captures_have_one_fingerprint_and_a_change_alters_it()
    {
        RecoveryRun.Fingerprint([Project("same")]).Should().Be(RecoveryRun.Fingerprint([Project("same")]));
        RecoveryRun.Fingerprint([Project("same")]).Should().NotBe(RecoveryRun.Fingerprint([Project("changed")]));
        RecoveryRun.Fingerprint([Project("same", [1])]).Should().NotBe(RecoveryRun.Fingerprint([Project("same", [2])]));
        RecoveryRun.Fingerprint([]).Should().NotBe(RecoveryRun.Fingerprint([Project("same")]));
    }
}
