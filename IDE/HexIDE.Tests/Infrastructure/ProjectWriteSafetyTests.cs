using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.Infrastructure;

public sealed class ProjectWriteSafetyTests
{
    [Theory]
    [InlineData(".frm", ".frx")]
    [InlineData(".ctl", ".ctx")]
    [InlineData(".pag", ".pgx")]
    public void Preserves_source_and_companion_bytes(string extension, string companionExtension)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-2504-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var project = TestHelpers.CreateProjectWithForm("Project1");
            project.AbsolutePath = Path.Combine(directory, "Project1.vbp");
            var form = project.Forms[0];
            form.AbsolutePath = Path.Combine(directory, "Form1" + extension);
            var companion = Path.ChangeExtension(form.AbsolutePath, companionExtension);
            File.WriteAllBytes(form.AbsolutePath, [0, 128, 255]);
            File.WriteAllBytes(companion, [255, 128, 0]);
            ProjectWriteSafety.Prepare(DocumentIdentity.For(form), out var note).Should().BeNull();
            foreach (var path in new[] { form.AbsolutePath, companion })
            {
                var backup = Directory.GetFiles(directory, Path.GetFileName(path) + ".hexide-recovery-*")
                    .Should().ContainSingle().Subject;
                File.ReadAllBytes(backup).Should().Equal(File.ReadAllBytes(path));
                note.Should().Contain(backup).And.Contain("To recover");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Keeps_only_the_newest_recovery_files_for_a_document()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-2504-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var project = TestHelpers.CreateProjectWithForm("Project1");
            project.AbsolutePath = Path.Combine(directory, "Project1.vbp");
            var form = project.Forms[0];
            form.AbsolutePath = Path.Combine(directory, "Form1.frm");
            for (var write = 0; write < 8; write++)
            {
                File.WriteAllText(form.AbsolutePath, "version " + write);
                ProjectWriteSafety.Prepare(DocumentIdentity.For(form), out _).Should().BeNull();
                foreach (var file in Directory.GetFiles(directory, "Form1.frm.hexide-recovery-*"))
                    if (File.ReadAllText(file) == "version " + write)
                        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(write - 100));
            }
            Directory.GetFiles(directory, "Form1.frm.hexide-recovery-*").Select(File.ReadAllText)
                .Should().BeEquivalentTo("version 3", "version 4", "version 5", "version 6", "version 7");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    public static bool CanCreateSymbolicLinks => !OperatingSystem.IsWindows();

    [Fact(Skip = "Creating symbolic links on Windows requires developer mode or elevation", SkipUnless = nameof(CanCreateSymbolicLinks))]
    public void A_link_above_the_project_folder_is_not_refused_but_one_inside_it_is()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-2504-" + Guid.NewGuid().ToString("N"));
        var real = Path.Combine(directory, "real");
        Directory.CreateDirectory(Path.Combine(real, "inner"));
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(directory, "via"), real);
            var projectFolder = Path.Combine(directory, "via");
            var project = TestHelpers.CreateProjectWithForm("Project1");
            project.AbsolutePath = Path.Combine(projectFolder, "Project1.vbp");
            var form = project.Forms[0];
            form.AbsolutePath = Path.Combine(projectFolder, "Form1.frm");
            ProjectWriteSafety.Prepare(DocumentIdentity.For(form), out _).Should().BeNull();

            Directory.CreateSymbolicLink(Path.Combine(real, "linked"), Path.Combine(real, "inner"));
            form.AbsolutePath = Path.Combine(projectFolder, "linked", "Form1.frm");
            ProjectWriteSafety.Prepare(DocumentIdentity.For(form), out _).Should().Contain("linked path");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory(Skip = "Creating symbolic links on Windows requires developer mode or elevation", SkipUnless = nameof(CanCreateSymbolicLinks))]
    [InlineData(false)]
    [InlineData(true)]
    public void Refuses_a_linked_companion_before_creating_any_recovery_file(bool dangling)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-2504-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var project = TestHelpers.CreateProjectWithForm("Project1");
            project.AbsolutePath = Path.Combine(directory, "Project1.vbp");
            var form = project.Forms[0];
            form.AbsolutePath = Path.Combine(directory, "Form1.frm");
            File.WriteAllText(form.AbsolutePath, "source");
            var victim = Path.Combine(directory, "victim");
            if (!dangling) File.WriteAllText(victim, "user bytes");
            File.CreateSymbolicLink(Path.ChangeExtension(form.AbsolutePath, ".frx"), victim);
            ProjectWriteSafety.Prepare(DocumentIdentity.For(form), out _).Should().Contain("linked path");
            Directory.GetFiles(directory, "*.hexide-recovery-*").Should().BeEmpty();
            if (!dangling) File.ReadAllText(victim).Should().Be("user bytes");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
