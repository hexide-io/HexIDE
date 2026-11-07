using HexIDE.Lsp;
using Microsoft.Extensions.Logging;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// The file's top-level <c>arguments</c>: arguments added to an entry by id without replacing it (#262). The bundled
/// server's command is found at run time, so before this a user could replace that entry but not adjust it.
/// </summary>
public class LanguageServerArgumentsOverrideTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hexide-lspargs-" + Guid.NewGuid().ToString("N"));

    public LanguageServerArgumentsOverrideTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private LanguageServerConfigResult Load(string json, params LanguageServerEntry[] defaults)
    {
        var path = Path.Combine(_dir, "lsp-servers.json");
        File.WriteAllText(path, json);
        return new LanguageServerConfigLoader(path, Substitute.For<ILogger<LanguageServerConfigLoader>>()).Load(defaults);
    }

    /// <summary>As the bundled entry is contributed: a command found at run time, and no arguments of its own.</summary>
    private static LanguageServerEntry Bundled() => new()
    {
        Id = "hexide.vb6", Extensions = [".bas"], LanguageId = "vb6", Transport = "stdio",
        Command = @"C:\wherever\it\was\found\HexIDE.VbLspServer.exe", Arguments = "",
    };

    [Fact]
    public void Arguments_are_added_to_the_bundled_server_and_its_found_command_is_kept()
    {
        var result = Load("""{"version":1,"arguments":{"hexide.vb6":["--log","verbose"]}}""", Bundled());

        var entry = result.Entries.Single();
        entry.Command.Should().Be(@"C:\wherever\it\was\found\HexIDE.VbLspServer.exe", "the command is not the user's to write");
        entry.Arguments!.List.Should().Equal("--log", "verbose");
        result.Problems.Should().BeEmpty();
    }

    [Fact]
    public void The_override_adds_to_an_entrys_own_arguments_rather_than_replacing_them()
    {
        var result = Load("""
            {"version":1,
             "servers":[{"id":"md","extensions":[".md"],"languageId":"markdown","transport":"stdio","command":"rumdl","arguments":["server"]}],
             "arguments":{"md":["--verbose"]}}
            """);

        result.Entries.Single().Arguments!.List.Should().Equal("server", "--verbose");
    }

    [Fact]
    public void Two_command_lines_join_with_a_space()
    {
        var result = Load("""
            {"version":1,
             "servers":[{"id":"md","extensions":[".md"],"languageId":"markdown","transport":"stdio","command":"rumdl","arguments":"server"}],
             "arguments":{"md":"--verbose \"a b\""}}
            """);

        result.Entries.Single().Arguments!.Text.Should().Be("server --verbose \"a b\"");
    }

    [Fact]
    public void A_list_added_to_a_command_line_is_refused_rather_than_guessed_at()
    {
        // Joining them would mean quoting the list for a command line, or splitting the command line into a list, and
        // either way the arguments the server receives are HexIDE's guess. Same form, or nothing.
        var result = Load("""
            {"version":1,
             "servers":[{"id":"md","extensions":[".md"],"languageId":"markdown","transport":"stdio","command":"rumdl","arguments":"server"}],
             "arguments":{"md":["--verbose"]}}
            """);

        result.Entries.Single().Arguments!.Text.Should().Be("server", "the entry is unchanged");
        result.Problems.Should().ContainSingle(p => p.EntryId == "md" && !p.EntryRejected)
            .Which.Message.Should().Contain("same form");
    }

    [Fact]
    public void Arguments_for_an_id_no_entry_has_are_reported()
    {
        var result = Load("""{"version":1,"arguments":{"hexide.vb7":["--log"]}}""", Bundled());

        result.Entries.Single().Arguments!.Text.Should().BeEmpty("nothing else is touched");
        result.Problems.Should().ContainSingle(p => p.EntryId == "hexide.vb7")
            .Which.Message.Should().Contain("no server");
    }

    [Fact]
    public void Arguments_for_a_server_HexIDE_does_not_start_are_reported()
    {
        var result = Load("""
            {"version":1,
             "servers":[{"id":"ws","extensions":[".md"],"languageId":"markdown","transport":"websocket","endpoint":"ws://localhost:1234/"}],
             "arguments":{"ws":["--verbose"]}}
            """);

        result.Entries.Single().Arguments.Should().BeNull();
        result.Problems.Should().ContainSingle(p => p.EntryId == "ws" && p.Message.Contains("starts no process"));
    }

    [Fact]
    public void A_replacing_entry_takes_the_override_too_since_it_names_the_id_not_the_default()
    {
        var result = Load("""
            {"version":1,
             "servers":[{"id":"hexide.vb6","extensions":[".bas"],"languageId":"vb6","transport":"stdio","command":"my-vb6-server"}],
             "arguments":{"hexide.vb6":["--extra"]}}
            """, Bundled());

        var entry = result.Entries.Single();
        entry.Command.Should().Be("my-vb6-server");
        entry.Arguments!.List.Should().Equal("--extra");
    }

    [Fact]
    public void The_default_handed_in_is_not_changed()
    {
        // Defaults are code, and an override that wrote into the instance it was given would carry into the next load.
        var bundled = Bundled();

        Load("""{"version":1,"arguments":{"hexide.vb6":["--log"]}}""", bundled);

        bundled.Arguments!.Text.Should().BeEmpty();
        bundled.Arguments.List.Should().BeNull();
    }

    [Fact]
    public void The_added_arguments_are_part_of_the_command_announced_as_new()
    {
        // Arguments are part of a command's identity (LaunchedCommandOf): adding one changes what runs.
        var path = Path.Combine(_dir, "lsp-servers.json");
        File.WriteAllText(path, """{"version":1,"arguments":{"hexide.vb6":["--inject"]}}""");
        var seen = new LanguageServerCommandStore(Path.Combine(_dir, "seen.json"));

        var result = new LanguageServerConfigLoader(path, Substitute.For<ILogger<LanguageServerConfigLoader>>())
            .Load([Bundled()], seen);

        result.Problems.Should().ContainSingle(p => p.Kind == LanguageServerConfigProblemKind.UnseenCommand)
            .Which.Message.Should().Contain("--inject");
    }
}
