using HexIDE.Conversations;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Capture limits as they arrive from the file somebody wrote by hand.
/// </summary>
/// <remarks>
/// <b>Driven against real files, for the reason the sibling suite gives:</b> a loader's entire job is
/// reading text a human typed, and every defect worth catching lives at that boundary.
///
/// <para>
/// <b>The assertion that matters most is that a corrected value is reported.</b> A limit is clamped rather
/// than rejected, because a zero or three extra digits are ordinary typing accidents and refusing the whole
/// configuration over one would cost a working capture. That makes silence the hazard: a user who typed
/// two gigabytes and got sixteen megabytes must be told, or they will spend the afternoon wondering why
/// their capture keeps dropping things.
/// </para>
/// </remarks>
public class CaptureLimitsFromConfigurationTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "hexide-capcfg-" + Guid.NewGuid().ToString("N"));

    private readonly ILogger<LanguageServerConfigLoader> _logger =
        Substitute.For<ILogger<LanguageServerConfigLoader>>();

    public CaptureLimitsFromConfigurationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private LanguageServerConfigResult Load(string? json)
    {
        var path = Path.Combine(_dir, "lsp-servers.json");
        if (json is not null) File.WriteAllText(path, json);

        return new LanguageServerConfigLoader(path, _logger).Load([Vb6()]);
    }

    private static LanguageServerEntry Vb6() => new()
    {
        Id = "hexide.vb6",
        Extensions = [".bas", ".cls", ".frm"],
        LanguageId = "vb6",
        Transport = "stdio",
        Command = "HexIDE.VbLspServer",
    };

    [Fact]
    public void WithNothingWrittenTheMeasuredDefaultsApply()
    {
        var result = Load(null);

        result.CaptureLimits.Should().Be(CaptureLimits.Default);
        result.PerServerCaptureLimits.Should().BeEmpty("no entry asked for anything different");
    }

    [Fact]
    public void OnlyTheFieldsWrittenAreChanged()
    {
        // Layering, not replacement. A file naming one limit must not silently reset the other four to
        // whatever a record's positional defaults happen to be.
        var result = Load("""
            { "version": 1, "capture": { "frameBytes": 131072 } }
            """);

        result.CaptureLimits.FrameBytes.Should().Be(131072);
        result.CaptureLimits.EnvelopeEntries.Should().Be(CaptureLimits.Default.EnvelopeEntries);
        result.CaptureLimits.PayloadBytesPerConnection
            .Should().Be(CaptureLimits.Default.PayloadBytesPerConnection);
    }

    [Fact]
    public void AServerCanRaiseItsOwnBudgetWithoutRaisingEveryoneElses()
    {
        // The case the design singles out: measured over an identical twenty-edit script, one server
        // returned roughly a hundred times another's inbound bytes. A single number either starves the
        // quiet server's history or pays for the noisy one everywhere.
        var result = Load("""
            {
              "version": 1,
              "servers": [
                { "id": "clangd", "extensions": [".cpp"], "languageId": "cpp",
                  "transport": "stdio", "command": "clangd",
                  "capture": { "payloadBytesPerConnection": 67108864 } }
              ]
            }
            """);

        result.PerServerCaptureLimits.Should().ContainKey("clangd");
        result.PerServerCaptureLimits["clangd"].PayloadBytesPerConnection.Should().Be(67108864);
        result.CaptureLimits.PayloadBytesPerConnection
            .Should().Be(CaptureLimits.Default.PayloadBytesPerConnection,
                "one server's entry must not decide what the others are allowed to cost");
    }

    [Fact]
    public void AServerOverrideLayersOverTheFilesOwnDefaults()
    {
        var result = Load("""
            {
              "version": 1,
              "capture": { "frameBytes": 131072 },
              "servers": [
                { "id": "clangd", "extensions": [".cpp"], "languageId": "cpp",
                  "transport": "stdio", "command": "clangd",
                  "capture": { "envelopeEntries": 50000 } }
              ]
            }
            """);

        var clangd = result.PerServerCaptureLimits["clangd"];
        clangd.EnvelopeEntries.Should().Be(50000, "its own override");
        clangd.FrameBytes.Should().Be(131072, "and the file's default for everything it did not write");
    }

    [Fact]
    public void AValueOutOfRangeIsCorrectedAndSaidToHaveBeen()
    {
        // THE assertion. Clamping is right and silence is not: a user who typed four gigabytes and got a
        // clamp with no word about it has no way to find out why their capture behaves as it does.
        var result = Load("""
            { "version": 1, "capture": { "payloadBytesPerConnection": 9999999999999 } }
            """);

        result.CaptureLimits.PayloadBytesPerConnection
            .Should().BeLessThan(9999999999999, "the machine does not have that");

        result.Problems.Should().Contain(p =>
                p.EntryId == null
                && p.Message.Contains("capture:")
                && p.Message.Contains("payload bytes per connection")
                && !p.EntryRejected,
            "a corrected limit is a file-level problem, reported through the channel that is already "
          + "rendered, and it does not reject anything");
    }

    [Fact]
    public void ACorrectionOnAServerNamesThatServer()
    {
        var result = Load("""
            {
              "version": 1,
              "servers": [
                { "id": "clangd", "extensions": [".cpp"], "languageId": "cpp",
                  "transport": "stdio", "command": "clangd",
                  "capture": { "envelopeEntries": 0 } }
              ]
            }
            """);

        result.PerServerCaptureLimits["clangd"].EnvelopeEntries.Should().BeGreaterThan(0);
        result.Problems.Should().Contain(p => p.EntryId == "clangd" && p.Message.Contains("this server"),
            "a problem with no entry id would send the user looking through the whole file");
    }

    [Fact]
    public void AGlobalCeilingWrittenOnAServerIsReportedRatherThanObeyed()
    {
        // Reported rather than ignored, because a user who wrote it believes they raised something. And
        // not obeyed, because the ceiling is shared: one entry must not decide the total.
        var result = Load("""
            {
              "version": 1,
              "servers": [
                { "id": "clangd", "extensions": [".cpp"], "languageId": "cpp",
                  "transport": "stdio", "command": "clangd",
                  "capture": { "globalPayloadBytes": 1073741824 } }
              ]
            }
            """);

        result.CaptureLimits.GlobalPayloadBytes
            .Should().Be(CaptureLimits.Default.GlobalPayloadBytes);
        result.PerServerCaptureLimits["clangd"].GlobalPayloadBytes
            .Should().Be(CaptureLimits.Default.GlobalPayloadBytes);

        result.Problems.Should().Contain(p =>
            p.EntryId == "clangd"
            && p.Kind == LanguageServerConfigProblemKind.IgnoredField
            && p.Message.Contains("globalPayloadBytes"));
    }

    [Fact]
    public void NothingWrittenForAServerLeavesItOutOfTheMapEntirely()
    {
        // Absent is the ordinary case rather than a missing value, and the capture reads the shared default
        // for anything not in the map. An empty object counts as nothing written.
        var result = Load("""
            {
              "version": 1,
              "servers": [
                { "id": "clangd", "extensions": [".cpp"], "languageId": "cpp",
                  "transport": "stdio", "command": "clangd", "capture": { } }
              ]
            }
            """);

        result.PerServerCaptureLimits.Should().NotContainKey("clangd");
    }

    [Fact]
    public void AFileFromALaterHexideLeavesTheLimitsAlone()
    {
        // Half-reading a future format is worse than not reading it, and that has to include the limits:
        // a capture configured from a file the loader refused would be configured from nothing.
        var result = Load("""
            { "version": 99, "capture": { "frameBytes": 1048576 } }
            """);

        result.CaptureLimits.Should().Be(CaptureLimits.Default);
    }

    [Fact]
    public async Task TheCaptureHonoursTheLimitsItWasGiven()
    {
        // End to end, because a configuration that is read correctly and then not applied is the same
        // outcome as one that was never read.
        var result = Load("""
            {
              "version": 1,
              "capture": { "envelopeEntries": 200 },
              "servers": [
                { "id": "clangd", "extensions": [".cpp"], "languageId": "cpp",
                  "transport": "stdio", "command": "clangd",
                  "capture": { "envelopeEntries": 4000 } }
              ]
            }
            """);

        await using var log = new ConversationLog(
            result.CaptureLimits, perConnection: result.PerServerCaptureLimits);

        log.LimitsFor("clangd").EnvelopeEntries.Should().Be(4000);
        log.LimitsFor("hexide.vb6").EnvelopeEntries.Should().Be(200, "it wrote no override of its own");
    }
}
