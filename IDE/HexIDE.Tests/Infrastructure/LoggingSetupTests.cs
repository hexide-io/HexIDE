using HexIDE.Infrastructure;
using Serilog.Events;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// The assertions that would have caught hexide-io/HexIDE#357.
///
/// <para>
/// The file sink set <c>fileSizeLimitBytes</c> and left <c>rollOnFileSizeLimit</c> at its default of
/// <c>false</c>. That combination does not cap the file and carry on; it stops the sink writing for
/// the rest of the process, with no warning in the log, none on stderr, and — measured — nothing on
/// Serilog's own <c>SelfLog</c>. What is lost is the <b>tail</b>, which is the end a post-mortem
/// reader opens the file at, and the reason the tail exists at all is usually the crash sitting in it.
/// </para>
///
/// <para>
/// Nothing in the tree exercised the logging contract, so nothing failed. The lesson is the one
/// already recorded for the foreign language servers: that <c>Log.Fatal(...)</c> returned is not an
/// assertion that anything was written. These tests read the bytes on disk instead — every one of
/// them drives the real <see cref="LoggingSetup.BuildConfiguration"/> rather than restating its
/// shape, so a sink option quietly dropped from production is a red test here.
/// </para>
///
/// <para>
/// Rolling on its own is only half the fix. It makes one session produce several files, which the
/// hand-rolled cross-session prune used to count as several sessions — so the retention tests below
/// pin the other half.
/// </para>
/// </summary>
public sealed class LoggingSetupTests : IDisposable
{
    private const string EarlySentinel = "EARLY_EVENT_SENTINEL";
    private const string TailSentinel = "TAIL_EVENT_SENTINEL";

    private const long PartLimitBytes = 2048;
    private const int RetainedParts = 5;

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "HexIDE.LoggingSetupTests", Guid.NewGuid().ToString("N"));

    public LoggingSetupTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* a temp directory left behind is not a test failure */ }
    }

    // ---------------------------------------------------------------- the sink

    [Fact]
    public void AnEventEmittedAfterThePartLimitIsReachedIsStillOnDisk()
    {
        WriteASessionThatOverflowsItsFirstPart();

        // The whole defect in one assertion. Before the fix this event was simply not written:
        // measured on the pinned Serilog 4.3.0 / Serilog.Sinks.File 6.0.0, 26 of 201 events reached
        // the file and the closing Log.Fatal was not among them.
        ReadAllLogText().Should().Contain(
            TailSentinel,
            "a size limit must bound the file, not silence the sink — the last thing a crashing "
          + "session writes is the thing its log exists for");
    }

    [Fact]
    public void ThePartDiscardedWhenTheSessionOverflowsIsTheOldestOne()
    {
        WriteASessionThatOverflowsItsFirstPart();

        // A cap that drops the newest data inverts what a size limit is for. This is the assertion
        // that says which end goes: retention keeps the tail and sheds the head.
        var text = ReadAllLogText();
        text.Should().Contain(TailSentinel);
        text.Should().NotContain(
            EarlySentinel,
            "the opening of a long session is what retention is supposed to shed");
    }

    [Fact]
    public void ASessionCannotGrowPastItsPartLimitTimesItsRetainedPartCount()
    {
        WriteASessionThatOverflowsItsFirstPart();

        var files = Directory.GetFiles(_dir);
        files.Length.Should().BeLessThanOrEqualTo(
            RetainedParts, "retention is what turns rolling back into a bounded amount of disk");

        // Roughly 300 KB of events went in. The bound below is ~10 KB, so this distinguishes
        // "bounded" from "grew without limit" by more than an order of magnitude rather than by a
        // margin that a slightly larger event would erase.
        var total = files.Sum(f => new FileInfo(f).Length);
        total.Should().BeLessThan(
            2 * RetainedParts * PartLimitBytes,
            "a part is closed on the write that would exceed the limit, so each may overshoot by "
          + "one event — but never by a whole part");
    }

    // ----------------------------------------------------------- the retention

    [Fact]
    public void PruningCountsSessionsRatherThanFiles()
    {
        // Nine sessions, each with rolled parts, is 27 files. Counting files keeps 7 of them —
        // barely two sessions — and takes the opening parts of the sessions it claims to have kept.
        for (var session = 1; session <= 9; session++)
        {
            GivenSessionFiles($"2026091{session}-120000", parts: 3);
        }

        LoggingSetup.PruneOldLogs(_dir, "ide-", keepSessions: 7);

        DistinctSessionsOnDisk().Should().Be(
            7, "the retained count is a number of sessions, not a number of files");
        Directory.GetFiles(_dir).Should().HaveCount(
            21, "every part of a retained session is part of that session's history");
    }

    [Fact]
    public void PruningKeepsTheNewestSessionsAndDeletesTheOldest()
    {
        GivenSessionFiles("20260101-000000", parts: 4);
        GivenSessionFiles("20260601-000000", parts: 1);
        GivenSessionFiles("20260911-235959", parts: 2);

        LoggingSetup.PruneOldLogs(_dir, "ide-", keepSessions: 2);

        Directory.GetFiles(_dir, "ide-20260101-000000*").Should().BeEmpty(
            "the oldest session goes first, and it goes whole");
        Directory.GetFiles(_dir, "ide-20260601-000000*").Should().HaveCount(1);
        Directory.GetFiles(_dir, "ide-20260911-235959*").Should().HaveCount(2);
    }

    [Fact]
    public void PruningLeavesAnotherSubsystemsLogsAlone()
    {
        GivenSessionFiles("20260101-000000", parts: 1);
        File.WriteAllText(Path.Combine(_dir, "lsp-20260101-000000.log"), "not ours\n");

        LoggingSetup.PruneOldLogs(_dir, "ide-", keepSessions: 0);

        Directory.GetFiles(_dir, "ide-*").Should().BeEmpty();
        Directory.GetFiles(_dir, "lsp-*").Should().HaveCount(
            1, "the prefix is the whole of what makes a file this subsystem's to delete");
    }

    [Theory]
    [InlineData("ide-20260911-120000.log", "20260911-120000")]
    [InlineData("ide-20260911-120000_001.log", "20260911-120000")]
    [InlineData("ide-20260911-120000_147.log", "20260911-120000")]
    [InlineData("ide-something_else.log", "something_else")]
    [InlineData("ide-trailing_.log", "trailing_")]
    public void APartIsAttributedToTheSessionThatWroteIt(string fileName, string expected)
    {
        LoggingSetup.SessionKeyOf(fileName, "ide-").Should().Be(expected);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Drives the production sink configuration with a small part limit: one early sentinel, enough
    /// filler to roll well past <see cref="RetainedParts"/> parts, then a fatal tail sentinel.
    /// </summary>
    private void WriteASessionThatOverflowsItsFirstPart()
    {
        var path = Path.Combine(_dir, "ide-20260911-120000.log");

        using (var log = LoggingSetup
                   .BuildConfiguration(path, LogEventLevel.Information, PartLimitBytes, RetainedParts)
                   .CreateLogger())
        {
            log.Information(EarlySentinel);
            for (var i = 0; i < 3000; i++)
            {
                log.Information("filler event {Index} {Padding}", i, new string('.', 48));
            }
            log.Fatal(new InvalidOperationException("the crash the log exists for"), TailSentinel);
        }
    }

    private string ReadAllLogText() =>
        string.Concat(Directory.GetFiles(_dir).Order().Select(File.ReadAllText));

    private void GivenSessionFiles(string stamp, int parts)
    {
        File.WriteAllText(Path.Combine(_dir, $"ide-{stamp}.log"), $"{stamp} part 0\n");
        for (var part = 1; part < parts; part++)
        {
            File.WriteAllText(Path.Combine(_dir, $"ide-{stamp}_{part:000}.log"), $"{stamp} part {part}\n");
        }
    }

    private int DistinctSessionsOnDisk() =>
        Directory.GetFiles(_dir, "ide-*.log")
            .Select(f => LoggingSetup.SessionKeyOf(Path.GetFileName(f), "ide-"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
}
