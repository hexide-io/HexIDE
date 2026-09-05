using HexIDE.Lsp;
using Microsoft.Extensions.Logging;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// A command HexIDE has not run before is announced, not launched quietly.
///
/// <para>
/// Section 5 of #255, and the one requirement there that is about trust rather than syntax. Typing a path
/// into your own configuration is consent; a file appearing with a path in it is not — and
/// <c>lsp-servers.json</c> is an ordinary file any process running as the user may write. An entry naming
/// an executable is launched on every start thereafter, so without this, writing that file is a durable way
/// to have the IDE run something indefinitely and silently.
/// </para>
/// </summary>
public class UnseenCommandTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "hexide-seen-" + Guid.NewGuid().ToString("N"));

    private readonly ILogger<LanguageServerConfigLoader> _logger =
        Substitute.For<ILogger<LanguageServerConfigLoader>>();

    public UnseenCommandTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private LanguageServerCommandStore Store() =>
        new(Path.Combine(_dir, "lsp-servers-seen.json"));

    private LanguageServerConfigLoader Loader(string json)
    {
        var path = Path.Combine(_dir, "lsp-servers.json");
        File.WriteAllText(path, json);
        return new LanguageServerConfigLoader(path, _logger);
    }

    private const string OneStdioServer = """
        {"version":1,"servers":[{"id":"rumdl","extensions":[".md"],"languageId":"markdown",
         "transport":"stdio","command":"rumdl","arguments":"server"}]}
        """;

    [Fact]
    public void AnUnseenCommandIsAnnouncedButTheEntryStillWorks()
    {
        // Announced, not rejected. Refusing to run what someone typed into their own file would be theatre;
        // the requirement is only that the launch is not silent.
        var result = Loader(OneStdioServer).Load([], Store());

        result.Entries.Should().ContainSingle();
        var problem = result.Problems.Should().ContainSingle().Subject;
        problem.Kind.Should().Be(LanguageServerConfigProblemKind.UnseenCommand);
        problem.EntryRejected.Should().BeFalse();
        problem.Message.Should().Contain("rumdl server");
    }

    [Fact]
    public void TheSameCommandIsNotAnnouncedTwice()
    {
        // Otherwise every start reports every server, and a notice that always fires is one nobody reads —
        // which would defeat the point more thoroughly than not having it.
        var store = Store();
        Loader(OneStdioServer).Load([], store);

        var second = Loader(OneStdioServer).Load([], store);

        second.Problems.Should().BeEmpty();
    }

    [Fact]
    public void ARecordedCommandSurvivesIntoANewSession()
    {
        // The store is the point: a fresh LanguageServerCommandStore over the same file must remember.
        Loader(OneStdioServer).Load([], Store());

        var nextSession = Loader(OneStdioServer).Load([], Store());

        nextSession.Problems.Should().BeEmpty();
    }

    [Fact]
    public void ChangingTheExecutableIsAnnouncedAgain()
    {
        var store = Store();
        Loader(OneStdioServer).Load([], store);

        var changed = Loader("""
            {"version":1,"servers":[{"id":"rumdl","extensions":[".md"],"languageId":"markdown",
             "transport":"stdio","command":"something-else","arguments":"server"}]}
            """).Load([], store);

        changed.Problems.Should().ContainSingle()
            .Which.Kind.Should().Be(LanguageServerConfigProblemKind.UnseenCommand);
    }

    [Fact]
    public void ChangingOnlyTheArgumentsIsAlsoAnnounced()
    {
        // THE case a naive implementation misses. `node` is harmless; `node /tmp/x.js` is whatever x.js
        // says. An attacker who could only alter arguments would otherwise be unannounced.
        var store = Store();
        Loader(OneStdioServer).Load([], store);

        var changed = Loader("""
            {"version":1,"servers":[{"id":"rumdl","extensions":[".md"],"languageId":"markdown",
             "transport":"stdio","command":"rumdl","arguments":"--exec /tmp/evil.sh"}]}
            """).Load([], store);

        changed.Problems.Should().ContainSingle()
            .Which.Kind.Should().Be(LanguageServerConfigProblemKind.UnseenCommand);
    }

    [Fact]
    public void ADisabledEntryIsNotAnnounced()
    {
        // Nothing will be launched, so there is nothing to announce — and warning about a command that will
        // not run trains people to ignore the warning.
        var result = Loader("""
            {"version":1,"servers":[{"id":"rumdl","enabled":false,"extensions":[".md"],
             "languageId":"markdown","transport":"stdio","command":"rumdl"}]}
            """).Load([], Store());

        result.Problems.Should().BeEmpty();
    }

    [Fact]
    public void AnEndpointHexideMerelyConnectsToIsNotAnnounced()
    {
        // Only stdio starts a process. A WebSocket endpoint is someone else's decision to have run
        // something, and reporting it would be announcing a program the IDE did not launch.
        var result = Loader("""
            {"version":1,"servers":[{"id":"remote","extensions":[".md"],"languageId":"markdown",
             "transport":"websocket","endpoint":"ws://localhost:1234"}]}
            """).Load([], Store());

        result.Problems.Should().BeEmpty();
    }

    [Fact]
    public void AnUnreadableStoreAnnouncesEverythingRatherThanNothing()
    {
        // Fails towards the nuisance. Announcing a command someone has already seen is annoying; staying
        // quiet about one they have not is the failure this exists to prevent — and deleting this file is
        // exactly what an attacker would do.
        var path = Path.Combine(_dir, "lsp-servers-seen.json");
        File.WriteAllText(path, "{ not json at all");

        var result = Loader(OneStdioServer).Load([], new LanguageServerCommandStore(path));

        result.Problems.Should().ContainSingle()
            .Which.Kind.Should().Be(LanguageServerConfigProblemKind.UnseenCommand);
    }

    [Fact]
    public void WithNoStoreSuppliedNothingIsAnnounced()
    {
        // The loader is usable without the trust check — tests of parsing should not have to care.
        var result = Loader(OneStdioServer).Load([]);

        result.Problems.Should().BeEmpty();
    }

    [Fact]
    public void TheBundledDefaultIsAnnouncedOnFirstRunLikeAnyOtherEntry()
    {
        // It is an ordinary entry, so it gets the ordinary treatment. Exempting it would mean the one
        // command that runs on every install is the one nobody is ever told about.
        var result = new LanguageServerConfigLoader(Path.Combine(_dir, "absent.json"), _logger)
            .Load(
                [new LanguageServerEntry
                {
                    Id = "hexide.vb6", Extensions = [".bas"], LanguageId = "vb6",
                    Transport = "stdio", Command = "HexIDE.VbLspServer",
                }],
                Store());

        result.Problems.Should().ContainSingle()
            .Which.EntryId.Should().Be("hexide.vb6");
    }
}
