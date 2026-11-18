using System.Collections.Concurrent;
using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexIDE.Tests.LspClient;

public sealed class WatchedFileBatchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly ConcurrentQueue<DidChangeWatchedFilesParams> _sent = new();
    private readonly WatchedFileRegistrations _watcher;
    private readonly string _path;

    public WatchedFileBatchTests()
    {
        Directory.CreateDirectory(_root);
        _path = Path.Combine(_root, "file.txt");
        File.WriteAllText(_path, "old");
        _watcher = CreateWatcher(true);
    }

    private WatchedFileRegistrations CreateWatcher(bool correction) => new(() => [_root],
        p => { _sent.Enqueue(p); return Task.CompletedTask; }, NullLogger.Instance, correction);

    private void Register(int kind = 7) => _watcher.Register(JsonSerializer.SerializeToElement(new
    {
        registrations = new[] { new { id = "test", method = "workspace/didChangeWatchedFiles",
            registerOptions = new { watchers = new[] { new { globPattern = "*.txt", kind } } } } }
    }));

    // The events the disk watcher reports, injected as FSEvents delivers them on macOS (#3650: every write reports Changed,
    // then Created). Register also starts a real watcher, whose events for the same path merge with these in the batch.
    private void Disk(int type, WatchedFileRegistrations? watcher = null) => (watcher ?? _watcher).NotifyFromDisk(_path, type, _root);

    // What the next flush sent. The flush comes 150 ms after the first queued event, so this waits for it rather than a
    // fixed time, and then a little longer, so an event from the real watcher that lands just after joins this batch.
    private async Task<int[]> Batch()
    {
        SpinWait.SpinUntil(() => !_sent.IsEmpty, 5000);
        await Task.Delay(250, TestContext.Current.CancellationToken);
        return Drain();
    }

    // Nothing is sent: wait well past the 150 ms flush, then read.
    private async Task<int[]> NoBatch()
    {
        await Task.Delay(600, TestContext.Current.CancellationToken);
        return Drain();
    }

    private int[] Drain()
    {
        var events = new List<DidChangeWatchedFilesParams>();
        while (_sent.TryDequeue(out var sent)) events.Add(sent);
        return events.SelectMany(p => p.Changes).Select(e => e.Type).ToArray();
    }

    // macOS delivers a write made just before a watch starts to that watch about 10 ms later, and nothing written 100 ms or
    // more before it (#3650). Register only once the file has settled, so the real watcher adds no stale event to a batch.
    private static Task Settle() => Task.Delay(500, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ExistingFileEditIsChanged()
    {
        await Settle();
        Register();
        Disk(2); Disk(1);
        (await Batch()).Should().Equal(2);
    }

    [Fact]
    public async Task NewFileThenEditThenRecreation()
    {
        File.Delete(_path);
        await Settle();
        Register();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        File.WriteAllText(_path, "new");
        // NTFS gives a file made under a just-deleted file's name that file's creation time ("tunnelling"). Measured on
        // NTFS on 2026-11-18: recreated at once, and 2 s after the delete, the old time was kept; 17 s after, a new one.
        // APFS gives a new file a new birth time (measured on macOS 27.2.1, #3650), so set one as macOS would.
        File.SetCreationTimeUtc(_path, DateTime.UtcNow);
        Disk(2); Disk(1);
        (await Batch()).Should().Equal(1);
        Disk(2); Disk(1);
        (await Batch()).Should().Equal(2);
        File.Delete(_path);
        Disk(3);
        (await Batch()).Should().Equal(3);
        File.WriteAllText(_path, "recreated");
        File.SetCreationTimeUtc(_path, DateTime.UtcNow);
        Disk(2); Disk(1);
        (await Batch()).Should().Equal(1);
    }

    [Fact]
    public async Task CreatedOnlyDoesNotReceiveAnEdit()
    {
        await Settle();
        Register(1);
        Disk(2); Disk(1);
        (await NoBatch()).Should().BeEmpty();
    }

    [Fact]
    public async Task IdeCreatedRemainsCreated()
    {
        await Settle();
        Register();
        _watcher.Notify(_path, 1);
        (await Batch()).Should().Equal(1);
    }

    [Fact]
    public async Task IdeReportIsNotCorrectedByTheDisksEventsForTheSameWrite()
    {
        // The file predates the watch, so the disk's own Changed and Created would be corrected to Changed. Arriving in
        // the same batch as the IDE's report, they leave it as the IDE said it.
        await Settle();
        Register();
        _watcher.Notify(_path, 1);
        Disk(2); Disk(1);
        (await Batch()).Should().Equal(1);
    }

    [Fact]
    public async Task CorrectionOffRetainsLastCreated()
    {
        using var watcher = CreateWatcher(false);
        await Settle();
        watcher.Register(JsonSerializer.SerializeToElement(new { registrations = new[] { new { id = "off",
            method = "workspace/didChangeWatchedFiles", registerOptions = new { watchers = new[] { new { globPattern = "*.txt" } } } } } }));
        Disk(2, watcher); Disk(1, watcher);
        (await Batch()).Should().Equal(1);
    }

    [Fact]
    public async Task MissingFileRetainsReportedType()
    {
        File.Delete(_path);
        await Settle();
        Register();
        Disk(2); Disk(1);
        (await Batch()).Should().Equal(1);
    }

    public void Dispose()
    {
        _watcher.Dispose();
        Directory.Delete(_root, true);
    }
}
