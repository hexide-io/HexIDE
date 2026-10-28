using System.IO;
using HexIDE.Recovery;

namespace HexIDE.Tests.Recovery;

/// <summary>
/// When a run's backup is written, kept and deleted (hexide-io/HexIDE#499 tasks 1.1, 1.2 and 1.5): deleted after a
/// clean exit, kept when the IDE closes without anyone deciding about its unsaved work.
/// </summary>
public sealed class RecoveryServiceTests : IDisposable
{
    private static readonly DateTime Started = new(2026, 10, 28, 13, 45, 12, DateTimeKind.Utc);
    private readonly string root = Path.Join(Path.GetTempPath(), "hexide-recovery-service-" + Guid.NewGuid().ToString("N"));
    private readonly IProjectService projects = Substitute.For<IProjectService>();
    private readonly IClock clock = Substitute.For<IClock>();

    public RecoveryServiceTests() => clock.UtcNow.Returns(Started.AddMinutes(5));

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private RecoveryService Begun()
    {
        var service = new RecoveryService(projects, clock, () => RecoveryRun.Begin(root, 4242, Started));
        service.Begin().Should().BeTrue();
        return service;
    }

    private void Unsaved(string text) => projects.CaptureUnsavedWork().Returns(
    [
        new UnsavedProject("Test", null, null, new Vb6TextEncoding(1252), new UnsavedFile("Test.vbp", "Type=Exe\r\n"),
            [new("Module1", "StandardModule", null, new Vb6TextEncoding(1252), new UnsavedFile("Module1.bas", text))]),
    ]);

    [Fact]
    public async Task Unsaved_work_is_written_to_the_run_s_folder()
    {
        var service = Begun();
        Unsaved("Public Sub Typed()\r\n");

        await service.Capture();

        var record = RecoveryRun.Read(service.Folder!)!;
        record.Capture.Should().Be("s1");
        File.ReadAllText(Path.Combine(service.Folder!, record.Projects[0].Documents[0].File!))
            .Should().Be("Public Sub Typed()\r\n");
    }

    [Fact]
    public async Task A_capture_identical_to_the_last_is_not_written_again()
    {
        var service = Begun();
        Unsaved("same");
        await service.Capture();

        await service.Capture();

        RecoveryRun.Read(service.Folder!)!.Capture.Should().Be("s1", "an idle IDE doesn't rewrite its backup every tick");
    }

    [Fact]
    public async Task A_clean_exit_deletes_the_run_s_folder()
    {
        var service = Begun();
        Unsaved("unsaved, then discarded at the prompt");
        await service.Capture();
        var folder = service.Folder!;

        service.EndRun();

        Directory.Exists(folder).Should().BeFalse("somebody decided about the work, so nothing is owed");
    }

    [Fact]
    public void An_exit_nobody_decided_keeps_the_latest_work()
    {
        var service = Begun();
        Unsaved("typed after the last tick");

        service.KeepForRecovery();
        service.EndRun();

        var record = RecoveryRun.Read(service.Folder!)!;
        File.ReadAllText(Path.Combine(service.Folder!, record.Projects[0].Documents[0].File!))
            .Should().Be("typed after the last tick", "a terminate signal backs up now, not at the next tick");
    }

    [Fact]
    public async Task Nothing_is_captured_once_the_run_has_ended()
    {
        var service = Begun();
        var folder = service.Folder!;
        service.EndRun();
        Unsaved("after the close");

        await service.Capture();

        Directory.Exists(folder).Should().BeFalse("a write after the close would bring the folder back as a crashed run");
    }

    [Fact]
    public async Task A_run_whose_folder_cannot_be_made_carries_on_without_recovery()
    {
        var service = new RecoveryService(projects, clock, () => throw new IOException("read-only"));

        service.Begin().Should().BeFalse();
        Unsaved("unsaved");
        await service.Capture();
        service.EndRun();

        projects.DidNotReceive().CaptureUnsavedWork();
    }

    [Fact]
    public async Task A_capture_that_fails_is_tried_again_at_the_next_tick()
    {
        var service = Begun();
        var fails = true;
        IReadOnlyList<UnsavedProject> work =
        [
            new UnsavedProject("Test", null, null, new Vb6TextEncoding(1252), new UnsavedFile("Test.vbp", "Type=Exe\r\n"), []),
        ];
        projects.CaptureUnsavedWork().Returns(_ => fails ? throw new InvalidOperationException("a render failed") : work);

        await service.Capture();
        fails = false;
        await service.Capture();

        RecoveryRun.Read(service.Folder!)!.Capture.Should().Be("s1");
    }
}
