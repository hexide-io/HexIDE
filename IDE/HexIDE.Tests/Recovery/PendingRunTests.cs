using System.IO;
using HexIDE.Recovery;

namespace HexIDE.Tests.Recovery;

/// <summary>
/// Which runs the next start offers (hexide-io/HexIDE#499 task 2.1): one that is over and captured something, claimed
/// so that no other IDE offers it too. Every other folder is left alone or, when there is nothing in it to offer, deleted
/// without asking.
/// </summary>
public sealed class PendingRunTests : IDisposable
{
    private static readonly DateTime Started = new(2026, 10, 28, 13, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = Started.AddHours(1);
    private readonly string root = Path.Join(Path.GetTempPath(), "hexide-pending-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> taken = [];

    public PendingRunTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        foreach (var run in taken) run.Dispose();
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    private static readonly Func<int, DateTime, bool> Over = (_, _) => false;

    private static UnsavedProject Project(string name) => new(
        name, null, null, new Vb6TextEncoding(1252), new UnsavedFile(name + ".vbp", "Type=Exe\r\n"),
        [new("Module1", "StandardModule", null, new Vb6TextEncoding(1252), new UnsavedFile("Module1.bas", "Sub A()\r\nEnd Sub\r\n"))]);

    private string Crashed(int processId, params UnsavedProject[] projects)
    {
        var run = RecoveryRun.Begin(root, processId, Started);
        if (projects.Length > 0)
            run.Write(projects, Started.AddMinutes(5));
        return run.Folder;
    }

    private IReadOnlyList<PendingRun> Find(string? own = null, Func<int, DateTime, bool>? isAlive = null)
    {
        var found = PendingRun.Find(root, own, isAlive ?? Over, Now);
        taken.AddRange(found);
        return found;
    }

    [Fact]
    public void A_run_that_is_over_is_offered_with_its_work()
    {
        var folder = Crashed(101, Project("Alpha"), Project("Beta"));

        var run = Find().Should().ContainSingle().Subject;

        run.Folder.Should().Be(folder);
        run.SavedAt.Should().Be(Started.AddMinutes(5));
        run.Projects.Select(p => p.Name).Should().Equal("Alpha", "Beta");
        run.Read(run.Projects[0]).Documents.Single().Content!.Text.Should().Be("Sub A()\r\nEnd Sub\r\n");
    }

    [Fact]
    public void A_run_still_going_is_not_offered()
    {
        var folder = Crashed(101, Project("Alpha"));

        Find(isAlive: (id, start) => id == 101 && start == Started).Should().BeEmpty();

        Directory.Exists(folder).Should().BeTrue("another IDE is still writing to it");
    }

    [Fact]
    public void This_run_s_own_folder_is_never_offered()
    {
        var own = Crashed(101, Project("Alpha"));

        Find(own).Should().BeEmpty();
    }

    [Fact]
    public void A_run_another_IDE_has_claimed_is_left_to_it()
    {
        Crashed(101, Project("Alpha"));
        Find().Should().ContainSingle();

        PendingRun.Find(root, null, Over, Now).Should().BeEmpty("the first IDE holds the claim while it runs");
    }

    [Fact]
    public void A_claim_given_up_leaves_the_run_for_the_next_start()
    {
        Crashed(101, Project("Alpha"));
        var first = Find().Single();

        first.Dispose();

        Find().Should().ContainSingle();
    }

    [Fact]
    public void A_run_that_captured_nothing_is_deleted_without_asking()
    {
        var folder = Crashed(101);

        Find().Should().BeEmpty();

        Directory.Exists(folder).Should().BeFalse("it ended before anything was edited, so there is nothing to offer");
    }

    [Fact]
    public void A_folder_with_no_record_yet_is_left_for_its_run_to_finish_making()
    {
        var folder = Directory.CreateDirectory(Path.Join(root, "20261028T130000000Z-102")).FullName;
        Directory.SetLastWriteTimeUtc(folder, Now.AddSeconds(-10));

        Find().Should().BeEmpty();
        Directory.Exists(folder).Should().BeTrue("its run may be between making the folder and writing run.json");

        Directory.SetLastWriteTimeUtc(folder, Now.AddMinutes(-5));
        Find().Should().BeEmpty();
        Directory.Exists(folder).Should().BeFalse("a run gone this long without a record never wrote one");
    }

    [Fact]
    public void A_run_written_by_a_newer_layout_is_left_alone()
    {
        var folder = Crashed(101, Project("Alpha"));
        var record = RecoveryRun.Read(folder)!;
        RecoveryRun.WriteRecord(folder, record with { Format = RecoveryRun.Format + 1 });

        Find().Should().BeEmpty();

        Directory.Exists(folder).Should().BeTrue("the HexIDE that wrote it can still offer it");
    }

    [Fact]
    public void A_record_that_names_a_file_outside_its_folder_is_not_read()
    {
        var folder = Crashed(101, Project("Alpha"));
        var record = RecoveryRun.Read(folder)!;
        var project = record.Projects[0];
        var escaping = project with
        {
            Documents = [project.Documents[0] with { File = "../../../../../../outside.bas" }],
        };
        RecoveryRun.WriteRecord(folder, record with { Projects = [escaping] });

        var run = Find().Single();

        run.Invoking(r => r.Read(r.Projects[0])).Should().Throw<IOException>();
    }

    [Fact]
    public void Each_project_dealt_with_comes_off_the_record_and_the_last_takes_the_folder()
    {
        var folder = Crashed(101, Project("Alpha"), Project("Beta"));
        var run = Find().Single();

        run.Resolve(run.Projects[0]);

        RecoveryRun.Read(folder)!.Projects.Select(p => p.Name).Should().Equal(["Beta"],
            "a project dealt with is not offered again by the next start");

        run.Resolve(run.Projects[0]);

        Directory.Exists(folder).Should().BeFalse();
    }

    [Fact]
    public void A_process_that_is_running_is_alive_only_with_its_own_start_time()
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        var start = self.StartTime.ToUniversalTime();

        PendingRun.IsAlive(Environment.ProcessId, start).Should().BeTrue();
        PendingRun.IsAlive(Environment.ProcessId, start.AddHours(-3)).Should().BeFalse(
            "a later process given the same id is not the run");
    }
}
