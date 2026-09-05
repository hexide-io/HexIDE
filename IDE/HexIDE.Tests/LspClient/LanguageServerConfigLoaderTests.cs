using HexIDE.Lsp;
using Microsoft.Extensions.Logging;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Contract tests for the language-server configuration, driven against <b>real files</b>.
///
/// <para>
/// A loader's entire job is reading a file a human wrote, so a mocked filesystem would assert nothing that
/// matters — every bug worth catching here (a comment that should be allowed and is not, an entry that
/// takes the whole file down with it, a merge that loses a default) exists only at the boundary with the
/// text on disk.
/// </para>
/// </summary>
public class LanguageServerConfigLoaderTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "hexide-lspcfg-" + Guid.NewGuid().ToString("N"));

    private readonly ILogger<LanguageServerConfigLoader> _logger =
        Substitute.For<ILogger<LanguageServerConfigLoader>>();

    public LanguageServerConfigLoaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private LanguageServerConfigLoader LoaderFor(string? json)
    {
        var path = Path.Combine(_dir, "lsp-servers.json");
        if (json is not null) File.WriteAllText(path, json);
        return new LanguageServerConfigLoader(path, _logger);
    }

    /// <summary>The bundled row, in the shape a default is contributed in.</summary>
    private static LanguageServerEntry BundledVb6() => new()
    {
        Id = "hexide.vb6",
        DisplayName = "HexIDE VB6 Language Server",
        Extensions = [".bas", ".cls", ".frm"],
        LanguageId = "vb6",
        Transport = "stdio",
        Command = "HexIDE.VbLspServer",
        Priority = -100,
    };

    // ── Defaults and absence ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void WithNoFileAtAllTheDefaultsApply()
    {
        // The commonest case by far, and the one that must never depend on the feature working.
        var result = LoaderFor(null).Load([BundledVb6()]);

        result.Entries.Should().ContainSingle().Which.Id.Should().Be("hexide.vb6");
        result.Problems.Should().BeEmpty("an absent configuration is normal, not a fault");
    }

    [Fact]
    public void DeletingTheFileRestoresTheDefaults()
    {
        // The stated recovery path for a user who has broken their own configuration, so it is a
        // requirement rather than an incidental consequence of the layering.
        var path = Path.Combine(_dir, "lsp-servers.json");
        File.WriteAllText(path, """{"version":1,"servers":[{"id":"hexide.vb6","enabled":false}]}""");
        var loader = new LanguageServerConfigLoader(path, _logger);
        loader.Load([BundledVb6()]).Entries.Single().Enabled.Should().BeFalse();

        File.Delete(path);

        loader.Load([BundledVb6()]).Entries.Single().Enabled.Should().BeNull("the default is back, unmodified");
    }

    // ── Layering ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AUserEntryAddsToTheDefaults()
    {
        var result = LoaderFor("""
            {
              "version": 1,
              "servers": [
                { "id": "rumdl", "extensions": [".md"], "languageId": "markdown",
                  "transport": "stdio", "command": "rumdl", "arguments": "server" }
              ]
            }
            """).Load([BundledVb6()]);

        // Defaults come first, and a user entry naming no default is appended.
        result.Entries.Select(e => e.Id).Should().Equal(["hexide.vb6", "rumdl"]);
        result.Problems.Should().BeEmpty();
    }

    [Fact]
    public void AUserEntryWithADefaultsIdReplacesItWholesale()
    {
        // Wholesale, not field-by-field. Field merge would mean a user could not REMOVE something a default
        // set, and would make the effective configuration something they cannot read off their own file.
        var result = LoaderFor("""
            {
              "version": 1,
              "servers": [
                { "id": "hexide.vb6", "displayName": "My VB6 server", "extensions": [".bas"],
                  "languageId": "vb6", "transport": "stdio", "command": "my-server" }
              ]
            }
            """).Load([BundledVb6()]);

        var entry = result.Entries.Should().ContainSingle().Subject;
        entry.Command.Should().Be("my-server");
        entry.Priority.Should().BeNull("the default's priority is replaced, not inherited");
    }

    [Fact]
    public void AReplacedDefaultKeepsItsPositionRatherThanMovingToTheEnd()
    {
        var result = LoaderFor("""
            {
              "version": 1,
              "servers": [
                { "id": "rumdl", "extensions": [".md"], "languageId": "markdown",
                  "transport": "stdio", "command": "rumdl" },
                { "id": "hexide.vb6", "extensions": [".bas"], "languageId": "vb6",
                  "transport": "stdio", "command": "my-server" }
              ]
            }
            """).Load([BundledVb6()]);

        // Overriding a default is not the same as removing it and adding a new one.
        result.Entries.Select(e => e.Id).Should().Equal(["hexide.vb6", "rumdl"]);
    }

    [Fact]
    public void AnEntryCanBeSwitchedOffWithoutBeingReplaced()
    {
        // Disabling must be easier than deleting, or nobody will do it. An entry that will never be
        // launched is not required to name a command it does not have.
        var result = LoaderFor("""
            {"version":1,"servers":[{"id":"hexide.vb6","enabled":false}]}
            """).Load([BundledVb6()]);

        result.Entries.Should().ContainSingle().Which.Enabled.Should().BeFalse();
        result.Problems.Should().BeEmpty("a disabled entry needs nothing but an id");
    }

    // ── The file is hand-written ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void CommentsAndTrailingCommasAreAllowed()
    {
        // This is the one file that ships explaining itself, which JSON cannot do without comments. If they
        // are rejected, the header block that tells a user how to recover takes the file down with it.
        var result = LoaderFor("""
            // HexIDE language servers. Delete this file to restore the defaults.
            {
              "version": 1,
              "servers": [
                {
                  "id": "rumdl",
                  "extensions": [".md"],   // markdown only
                  "languageId": "markdown",
                  "transport": "stdio",
                  "command": "rumdl",
                },
              ],
            }
            """).Load([]);

        result.Entries.Should().ContainSingle().Which.Id.Should().Be("rumdl");
        result.Problems.Should().BeEmpty();
    }

    // ── One bad entry never takes the others down ─────────────────────────────────────────────────────

    [Fact]
    public void AMalformedEntryIsRejectedAndTheRestStillApply()
    {
        var result = LoaderFor("""
            {
              "version": 1,
              "servers": [
                { "id": "broken", "extensions": [".x"], "languageId": "x", "transport": "stdio" },
                { "id": "rumdl", "extensions": [".md"], "languageId": "markdown",
                  "transport": "stdio", "command": "rumdl" }
              ]
            }
            """).Load([BundledVb6()]);

        result.Entries.Select(e => e.Id).Should().Equal("hexide.vb6", "rumdl");
        var problem = result.Problems.Should().ContainSingle().Subject;
        problem.EntryId.Should().Be("broken");
        problem.EntryRejected.Should().BeTrue();
        problem.Message.Should().Contain("command");
    }

    [Fact]
    public void AnEntryWithNoIdIsRejectedBecauseNothingCouldReferToIt()
    {
        var result = LoaderFor("""
            {"version":1,"servers":[{"extensions":[".md"],"languageId":"markdown",
             "transport":"stdio","command":"rumdl"}]}
            """).Load([]);

        result.Entries.Should().BeEmpty();
        result.Problems.Should().ContainSingle().Which.EntryRejected.Should().BeTrue();
    }

    [Fact]
    public void AnUnknownTransportRejectsThatEntryAlone()
    {
        var result = LoaderFor("""
            {"version":1,"servers":[
              {"id":"odd","extensions":[".x"],"languageId":"x","transport":"carrier-pigeon"},
              {"id":"rumdl","extensions":[".md"],"languageId":"markdown","transport":"stdio","command":"rumdl"}
            ]}
            """).Load([]);

        result.Entries.Should().ContainSingle().Which.Id.Should().Be("rumdl");
        result.Problems.Should().ContainSingle().Which.Message.Should().Contain("carrier-pigeon");
    }

    // ── A typo is the failure that actually happens ───────────────────────────────────────────────────

    [Fact]
    public void AMisspelledFieldIsReportedAndTheEntryIsStillRejectedForWhatIsMissing()
    {
        // THE case this design exists for. "comand" is silently ignored by any ordinary deserializer, the
        // entry keeps its default of no command, and the user gets a server that fails for no visible
        // reason. Both facts are reported: what was not understood, and what was therefore missing.
        var result = LoaderFor("""
            {"version":1,"servers":[{"id":"rumdl","extensions":[".md"],"languageId":"markdown",
             "transport":"stdio","comand":"rumdl"}]}
            """).Load([]);

        result.Entries.Should().BeEmpty();
        result.Problems.Should().HaveCount(2);
        result.Problems.Should().Contain(p => !p.EntryRejected && p.Message.Contains("comand"));
        result.Problems.Should().Contain(p => p.EntryRejected && p.Message.Contains("command"));
    }

    [Fact]
    public void AnUnrecognisedFieldOnAnOtherwiseValidEntryKeepsTheEntry()
    {
        var result = LoaderFor("""
            {"version":1,"servers":[{"id":"rumdl","extensions":[".md"],"languageId":"markdown",
             "transport":"stdio","command":"rumdl","futureThing":42}]}
            """).Load([]);

        result.Entries.Should().ContainSingle().Which.Id.Should().Be("rumdl");
        var problem = result.Problems.Should().ContainSingle().Subject;
        problem.EntryRejected.Should().BeFalse("an unrecognised field is not grounds to discard the rest");
        problem.Message.Should().Contain("futureThing");
    }

    // ── Whole-file failures ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void UnparseableJsonLeavesTheDefaultsIntactAndSaysSo()
    {
        var result = LoaderFor("{ this is not json").Load([BundledVb6()]);

        result.Entries.Should().ContainSingle().Which.Id.Should().Be("hexide.vb6");
        result.Problems.Should().ContainSingle().Which.EntryRejected.Should().BeTrue();
    }

    [Fact]
    public void AFileFromANewerHexideIsIgnoredRatherThanGuessedAt()
    {
        // A later version may mean something different by the same field name. Half-reading it is worse
        // than not reading it — the same call the add-in consent store makes about a future schema.
        var result = LoaderFor("""
            {"version":99,"servers":[{"id":"rumdl","extensions":[".md"],"languageId":"markdown",
             "transport":"stdio","command":"rumdl"}]}
            """).Load([BundledVb6()]);

        result.Entries.Select(e => e.Id).Should().Equal("hexide.vb6");
        result.Problems.Should().ContainSingle().Which.Message.Should().Contain("99");
    }

    [Fact]
    public void AnEmptyServerListIsNotAnError()
    {
        var result = LoaderFor("""{"version":1,"servers":[]}""").Load([BundledVb6()]);

        result.Entries.Should().ContainSingle().Which.Id.Should().Be("hexide.vb6");
        result.Problems.Should().BeEmpty();
    }

    [Fact]
    public void IdsAreComparedExactlyRatherThanLoosely()
    {
        // An id is an identifier, not prose. Two ids differing only in case are two servers, and folding
        // them would silently replace a default with something that merely resembles it.
        var result = LoaderFor("""
            {"version":1,"servers":[{"id":"HexIDE.VB6","extensions":[".bas"],"languageId":"vb6",
             "transport":"stdio","command":"other"}]}
            """).Load([BundledVb6()]);

        result.Entries.Select(e => e.Id).Should().Equal("hexide.vb6", "HexIDE.VB6");
    }
}
