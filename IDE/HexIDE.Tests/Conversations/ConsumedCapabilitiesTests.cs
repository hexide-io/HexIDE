using System.Text.RegularExpressions;
using HexIDE.Lsp;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.Conversations;

/// <summary>
/// Keeps the list of capabilities this client consumes honest about the code that consumes them.
/// </summary>
/// <remarks>
/// <b>A hand-written list of what a program does is a document, and documents here rot unless something
/// checks them.</b> This one decides what a capture reports as "advertised and unused", which is the entry
/// a server author most wants — so a stale list does not merely go quiet, it tells them something false
/// about their own server.
///
/// <para>
/// The same shape as the protocol coverage guard: read the source, extract what is actually there, and
/// fail when the two disagree. Wiring a new method and forgetting the list breaks the build instead of
/// silently reporting that capability as unused for the rest of the project's life.
/// </para>
///
/// <para>
/// <b>It used to read only the gates, and that was exactly where the bug was</b> (hexide-io/HexIDE#394).
/// <c>textDocumentSync</c> is read by dedicated readers, because its two shapes mean different things and a
/// gate cannot answer for it, and <c>executeCommandProvider</c> is read by the command-routing reader. Both
/// were missing from the list, so every server that advertised document sync was told the client ignored it,
/// one entry before the client sent <c>didOpen</c> because of it.
/// </para>
/// </remarks>
public partial class ConsumedCapabilitiesTests
{
    /// <remarks>
    /// Matches all three gates. <c>CanServeQuietly</c> consumes a capability exactly as <c>CanServe</c> does
    /// and differs only in whether its ABSENCE is worth a warning — so leaving it out would report a
    /// capability this client takes up as one it ignores, which is the falsehood this guard exists to stop.
    /// </remarks>
    [GeneratedRegex("""CanServe(?:Experimental|Quietly)?\("([A-Za-z.]+)"\)""")]
    private static partial Regex GateCall();

    /// <summary>A call from the client into one of the shared capability readers.</summary>
    [GeneratedRegex("""\bServerCapabilities\.(\w+)\(""")]
    private static partial Regex ReaderCall();

    /// <summary>A static member of <c>ServerCapabilities</c>; its body runs to the next one.</summary>
    [GeneratedRegex("""(?:public|private|internal) static [^=({;]*?\b(\w+)\(""")]
    private static partial Regex StaticMember();

    /// <summary>A top-level capability looked up by name. Every reader calls the capabilities object <c>caps</c>.</summary>
    [GeneratedRegex("""\bcaps\.TryGetProperty\("([A-Za-z]+)",""")]
    private static partial Regex TopLevelRead();

    /// <summary>
    /// Readers that take the capability's name as an argument. The name is at the call site, where
    /// <see cref="GateCall"/> already reads it.
    /// </summary>
    private static readonly string[] ReadersTakingAName = ["Supports", "SupportsExperimental", "IsEnabled"];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                             && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the repository root should be findable from {0}", AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string ClientSource()
    {
        var path = Path.Combine(RepoRoot(), "IDE", "HexIDE.Lsp", "VBLspClient.cs");
        File.Exists(path).Should().BeTrue("the client's source is what this guard reads");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Every top-level capability the client reads through a dedicated reader rather than a gate, followed
    /// through the readers' private helpers — which is where the sync readers look <c>textDocumentSync</c> up.
    /// </summary>
    private static IReadOnlyCollection<string> ReadThroughReaders(string client)
    {
        var path = Path.Combine(RepoRoot(), "IDE", "HexIDE.Core", "Lsp", "Messages", "LspMessages.cs");
        var source = File.ReadAllText(path);
        var at = source.IndexOf("public record ServerCapabilities(", StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0, "the readers live on the ServerCapabilities record");
        var record = source[at..];

        var starts = StaticMember().Matches(record).ToList();
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < starts.Count; i++)
        {
            var end = i + 1 < starts.Count ? starts[i + 1].Index : record.Length;
            bodies.TryAdd(starts[i].Groups[1].Value, record[starts[i].Index..end]);
        }

        var called = ReaderCall().Matches(client)
            .Select(m => m.Groups[1].Value)
            .Where(n => !ReadersTakingAName.Contains(n, StringComparer.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        called.Should().NotBeEmpty("the client calls the sync readers, so finding none means the pattern broke");
        called.Should().OnlyContain(n => bodies.ContainsKey(n),
            "a reader this guard cannot find is one whose capability it silently leaves out");

        var reads = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(called);
        while (pending.TryDequeue(out var name))
        {
            if (!seen.Add(name)) continue;
            var body = bodies[name];
            foreach (Match read in TopLevelRead().Matches(body)) reads.Add(read.Groups[1].Value);
            foreach (var helper in bodies.Keys.Where(h => h != name && Regex.IsMatch(body, $@"\b{h}\(")))
                pending.Enqueue(helper);
        }

        // A container rather than a capability: the client reaches inside it through CanServeExperimental,
        // and NoteHandshake skips it by name for the same reason.
        reads.Remove("experimental");
        return reads;
    }

    [Fact]
    public void TheReadersAreFollowedToTheCapabilitiesTheyRead()
    {
        // The case the gate-only guard missed, pinned by name so the reader walk cannot pass by finding
        // nothing at all.
        ReadThroughReaders(ClientSource()).Should().Contain(["textDocumentSync", "executeCommandProvider"]);
    }

    [Fact]
    public void TheListMatchesEveryCapabilityTheClientActuallyReads()
    {
        var client = ClientSource();
        var gated = GateCall()
            .Matches(client)
            .Select(m => m.Groups[1].Value)
            .Concat(ReadThroughReaders(client))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        gated.Should().NotBeEmpty(
            "finding nothing would mean the pattern stopped matching, and a guard that matches nothing "
          + "passes for the wrong reason");

        VBLspClient.ConsumedCapabilities.Order(StringComparer.Ordinal).Should().Equal(gated,
            "every capability the client reads belongs in the list, and nothing else does — otherwise a "
          + "capture tells a server author their capability is unused when this client uses it, or the "
          + "reverse");
    }

    [Fact]
    public void TheListHasNoDuplicates()
    {
        VBLspClient.ConsumedCapabilities.Should().OnlyHaveUniqueItems();
    }
}
