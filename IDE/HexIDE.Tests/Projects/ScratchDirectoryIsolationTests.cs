using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.Projects;

/// <summary>
/// Two unsaved projects must not share a directory.
///
/// <para>
/// They did. The scratch directory was <c>%TEMP%/hexide_{Name}</c> and every new Standard EXE is named
/// <c>Project1</c>, so every first project of every session landed in one folder. Observed on a
/// development machine rather than reasoned about: eleven files spanning four days of unrelated sessions,
/// in a single directory (hexide-io/HexIDE#260).
/// </para>
///
/// <para>
/// Three consequences, none of which announced itself — adding a <c>Module1</c> destroyed a previous
/// session's, Save As copied whatever else was lying there, and two IDE instances wrote to the same place
/// concurrently. All three follow from the one property asserted here, which is why this is the test
/// that matters rather than one per symptom.
/// </para>
/// </summary>
public class ScratchDirectoryIsolationTests
{
    private static ProjectDefinition Unsaved(string name) =>
        new(VBProjectType.EXE, name);

    [Fact]
    public void TwoUnsavedProjectsWithTheSameNameGetDifferentDirectories()
    {
        // The bug in one assertion. Both are called Project1, because that is what HexIDE names them.
        var first = ProjectService.ProjectFilesDirectory(Unsaved("Project1"));
        var second = ProjectService.ProjectFilesDirectory(Unsaved("Project1"));

        second.Should().NotBe(first,
            "every new Standard EXE is named Project1, so keying the scratch directory on the name put "
          + "every unsaved project in one folder, where adding a Module1 destroyed the last session's");
    }

    [Fact]
    public void TheSameProjectAlwaysGetsTheSameDirectory()
    {
        // The other half, and the one a naive fix breaks: a fresh directory per CALL would scatter one
        // project's files across many, which is a different way to lose them.
        var project = Unsaved("Project1");

        var first = ProjectService.ProjectFilesDirectory(project);
        var second = ProjectService.ProjectFilesDirectory(project);

        second.Should().Be(first, "callers ask repeatedly and every answer must name the same place");
    }

    [Fact]
    public void TheDirectoryIsUnderTheTempDirectory()
    {
        var dir = ProjectService.ProjectFilesDirectory(Unsaved("Project1"));

        Path.GetFullPath(dir).Should().StartWith(Path.GetFullPath(Path.GetTempPath()));
    }

    [Fact]
    public void TheProjectNameStaysVisibleInTheDirectory()
    {
        // So a user looking in TEMP can still tell which directory belongs to what. Uniqueness must not
        // cost legibility.
        var dir = ProjectService.ProjectFilesDirectory(Unsaved("Payroll"));

        Path.GetFileName(dir).Should().StartWith("hexide_Payroll_");
    }

    [Fact]
    public void ASavedProjectStillUsesItsOwnDirectory()
    {
        // Unchanged behaviour, pinned: the scratch path applies only before a project has a home.
        var saved = new ProjectDefinition(VBProjectType.EXE, "Payroll")
        {
            AbsolutePath = Path.Combine(Path.GetTempPath(), "somewhere", "Payroll.vbp"),
        };

        ProjectService.ProjectFilesDirectory(saved)
            .Should().Be(Path.Combine(Path.GetTempPath(), "somewhere"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void ANameThatLooksLikeAPathCannotMoveTheDirectory(string hostile)
    {
        // A project name is user text and arrives here unfiltered. A separator in it would otherwise put
        // the scratch directory somewhere other than TEMP.
        var dir = ProjectService.ProjectFilesDirectory(Unsaved(hostile));

        Path.GetFullPath(dir).Should().StartWith(Path.GetFullPath(Path.GetTempPath()));
        Path.GetDirectoryName(Path.GetFullPath(dir))!.TrimEnd(Path.DirectorySeparatorChar)
            .Should().Be(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),
                "a hostile name must not add a directory level");
    }

    [Fact]
    public void AnEmptyNameStillYieldsAUsableDirectory()
    {
        var dir = ProjectService.ProjectFilesDirectory(Unsaved("   "));

        Path.GetFileName(dir).Should().StartWith("hexide_project_");
    }
}
