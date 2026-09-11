using System.Runtime.CompilerServices;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Where HexIDE's own client meets HexIDE's own server, over a real process.
/// </summary>
/// <remarks>
/// <b>Everything else about tracing is proved against a fake.</b> The client's wire tests drive a server
/// spoken by hand; the server's wire tests drive a client spoken by hand. Each proves one half agrees with
/// a fixture, and neither proves the two halves agree with <em>each other</em> — which is exactly the
/// failure this project already has written down about its own suite, where a client and a server by one
/// hand converged on shared assumptions rather than on the specification. Here the two halves were written
/// by different hands that never met, so the risk is larger rather than smaller.
///
/// <para>
/// So this launches the real executable and speaks the real client at it. It is the only test in the tree
/// that would notice if the two sides disagreed about the shape of a trace frame, the spelling of a level,
/// or which of them is supposed to say <c>off</c> out loud.
/// </para>
///
/// <para>
/// Skipped visibly when the server has not been built, and made fatal under the same variable that already
/// forbids a silently skipped foreign-server proof — see <see cref="RequiredServersTests"/>. A proof that
/// can quietly stop happening is worth very little, and this suite has paid for that lesson once.
/// </para>
/// </remarks>
public class BundledServerTraceRoundTripTests : IAsyncDisposable
{
    private VBLspClient? _client;

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            try { await _client.StopAsync(); } catch { /* teardown is best effort */ }
            try { await _client.DisposeAsync(); } catch { /* ditto */ }
        }
        GC.SuppressFinalize(this);
    }

    private VBLspClient Connect(string trace)
    {
        var loggerFactory = LoggerFactory.Create(b => { });
        var transport = new StdioProcessLspTransport(
            new LspServerInfo(BundledServer.Find()!, "", Path.GetTempPath()),
            loggerFactory.CreateLogger<StdioProcessLspTransport>());

        _client = new VBLspClient(
            transport, loggerFactory.CreateLogger<VBLspClient>(), "vb6", trace: trace);
        return _client;
    }

    /// <summary>The first trace notification to arrive, or null if none did within the timeout.</summary>
    private static async Task<LogTraceParams?> FirstTraceAsync(VBLspClient client, Func<Task> provoke)
    {
        var arrived = new TaskCompletionSource<LogTraceParams>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnTrace(object? _, LogTraceParams p) => arrived.TrySetResult(p);

        client.TraceReceived += OnTrace;
        try
        {
            await provoke();
            var completed = await Task.WhenAny(arrived.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            return completed == arrived.Task ? await arrived.Task : null;
        }
        finally
        {
            client.TraceReceived -= OnTrace;
        }
    }

    private const string Module = "Option Explicit\n\nSub Main()\n    Debug.Print \"hello\"\nEnd Sub\n";

    [BundledServerFact]
    public async Task TheServerTracesWhatTheClientAskedForAtTheHandshake()
    {
        // The whole loop in one assertion: the client puts a level in initialize, the server reads it,
        // decides to speak, frames a notification, and the client parses it back into the event. Any one
        // of those disagreeing about a field name or a spelling shows up here and nowhere else.
        var client = Connect(LspTraceValue.Verbose);
        await client.StartAsync(TestContext.Current.CancellationToken);

        var trace = await FirstTraceAsync(client, () =>
            client.OpenDocumentAsync("vb6://module/Module1", Module, TestContext.Current.CancellationToken));

        trace.Should().NotBeNull("the bundled server was asked for verbose tracing at initialize");
        trace!.Message.Should().NotBeNullOrWhiteSpace();
        trace.Verbose.Should().NotBeNull(
            "the verbose member is the entire difference between the two levels that are not off");
    }

    [BundledServerFact]
    public async Task AskingForNothingGetsNothing()
    {
        // The off path, end to end. Worth its own test because it is the default every user runs, and a
        // server that traced anyway would be a permanent cost imposed on people who never asked.
        var client = Connect(LspTraceValue.Off);
        await client.StartAsync(TestContext.Current.CancellationToken);

        var trace = await FirstTraceAsync(client, async () =>
        {
            await client.OpenDocumentAsync("vb6://module/Module1", Module, TestContext.Current.CancellationToken);
            // A second edit, so this is not merely "nothing had happened yet".
            await client.ChangeDocumentAsync(
                "vb6://module/Module1", 2, Module + "\n", TestContext.Current.CancellationToken);
        });

        // PROVE THE SERVER WAS ALIVE BEFORE READING ANYTHING INTO ITS SILENCE. Without this the test
        // passes when the executable is missing, the process died, or the handshake never completed —
        // every one of which produces no trace notifications and none of which is the thing being
        // asserted. Caught by hiding the server to check the skip had teeth and watching this one pass
        // anyway, which is the fail-open pattern this repository keeps paying for.
        client.IsRunning.Should().BeTrue("a silence from a server that never started proves nothing");
        var symbols = await client.RequestDocumentSymbolsAsync(
            "vb6://module/Module1", TestContext.Current.CancellationToken);
        symbols.Should().NotBeEmpty(
            "the server has to have parsed the document for its silence about tracing to mean anything");

        trace.Should().BeNull("nothing asked the server to talk about itself");
    }

    [BundledServerFact]
    public async Task TheLevelCanBeTurnedUpOnAServerThatIsAlreadyRunning()
    {
        // The one part configuration cannot reach. Proving it against the real server matters more than
        // the others, because $/setTrace is a notification: it is never acknowledged, so a client that
        // sent it wrongly would look identical to a server that ignored it.
        var client = Connect(LspTraceValue.Off);
        await client.StartAsync(TestContext.Current.CancellationToken);
        await client.OpenDocumentAsync("vb6://module/Module1", Module, TestContext.Current.CancellationToken);

        var trace = await FirstTraceAsync(client, async () =>
        {
            await client.SetTraceAsync(LspTraceValue.Messages, TestContext.Current.CancellationToken);
            await client.ChangeDocumentAsync(
                "vb6://module/Module1", 2, Module + "\n", TestContext.Current.CancellationToken);
        });

        trace.Should().NotBeNull("$/setTrace was sent and the server should have started talking");
        trace!.Verbose.Should().BeNull("the level asked for was messages, not verbose");
    }
}

/// <summary>Finds the bundled VB6 server, wherever this repository last built it.</summary>
internal static class BundledServer
{
    private static readonly string ExeName = OperatingSystem.IsWindows()
        ? "HexIDE.VbLspServer.exe"
        : "HexIDE.VbLspServer";

    /// <summary>
    /// The configuration this test assembly was built in, and the only one it will accept a server from.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately not a search across configurations, and this was measured rather than assumed.</b>
    /// An earlier version probed Debug and then fell back to Release. Hiding the Debug server to check the
    /// skip had teeth did not produce a skip: it silently found a months-old Release build and ran the
    /// round trip against that. A test whose whole purpose is proving these two halves agree is worthless
    /// if it can quietly compare today's client to yesterday's server.
    /// </remarks>
    private const string Configuration =
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    /// <summary>
    /// Why the built server cannot be trusted, or null when it can.
    /// </summary>
    /// <remarks>
    /// <b>An out-of-date binary is worse than an absent one, and this was measured the hard way.</b> A
    /// contributor working on the server rebased, ran these tests, and got two failures that read exactly
    /// like their own change having broken the trace round trip. It had not: the binary on disk predated
    /// the feature and could not emit a trace notification at all. Absence skips; staleness ran, waited
    /// twenty seconds, and then blamed the wrong person.
    ///
    /// <para>
    /// Compared against the newest source rather than against a build stamp, because the question is not
    /// "was this built" but "was it built since the thing it is being asked to demonstrate". A minute of
    /// slack absorbs a build finishing between the compile and the copy.
    /// </para>
    /// </remarks>
    public static string? StaleReason()
    {
        if (Find() is not { } exe) return null;

        var built = File.GetLastWriteTimeUtc(exe);
        var sources = new DirectoryInfo(Path.Combine(RepoRoot(), "LspServer"));
        if (!sources.Exists) return null;

        var newest = sources
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => f.LastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();

        return newest > built.AddMinutes(1)
            ? $"the bundled VB6 language server was built at {built:u} and its source has changed since "
            + $"({newest:u}). Rebuild it with: cd LspServer && dotnet build HexIDE.VbLspServer/ — these "
            + "tests are skipped rather than run against a binary that predates what they assert."
            : null;
    }

    /// <summary>The built server executable, or null when this tree has not built one.</summary>
    /// <remarks>
    /// The IDE half and the server half are separate builds, and running the IDE's tests does not produce
    /// the server. So this looks where a build would have left it rather than assuming one ran, and its
    /// absence is a skip rather than a failure — except where the environment says otherwise.
    /// </remarks>
    public static string? Find()
    {
        string[] candidates =
        [
            Path.Combine(RepoRoot(), "LspServer", "HexIDE.VbLspServer", "bin", Configuration, "net10.0", ExeName),

            // Also where a desktop build copies it, which is the shape a developer who has run the IDE will
            // have. Same configuration, for the reason above.
            Path.Combine(RepoRoot(), "IDE", "HexIDE.Desktop", "bin", Configuration, "net10.0", ExeName),
        ];

        return Array.Find(candidates, File.Exists);
    }

    /// <summary>
    /// The monorepo root, identified by holding both halves rather than by a solution file — there is one
    /// of those in the root <em>and</em> in <c>IDE/</c>, so searching by name stops a level too early.
    /// </summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                             && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips — visibly — when the bundled server has not been built.
/// </summary>
/// <remarks>
/// <b>Always a skip, never a throw.</b> Under xunit v3 a test whose attribute constructor throws is
/// discarded at discovery, so enforcement written here would fail open: a green run with the test simply
/// absent. That has happened in this suite before, and the enforcement therefore lives in an ordinary
/// <c>[Fact]</c> that discovery cannot swallow.
/// </remarks>
public sealed class BundledServerFactAttribute : FactAttribute
{
    public BundledServerFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
            : base(sourceFilePath, sourceLineNumber)
    {
        if (BundledServer.StaleReason() is { } stale)
        {
            Skip = stale;
            return;
        }

        if (BundledServer.Find() is not null) return;

        Skip = "The bundled VB6 language server has not been built. Run "
             + "`cd LspServer && dotnet build HexIDE.VbLspServer/` to build it. The IDE's own tests do not "
             + "produce it, so a tree that has only ever built the IDE skips these.";
    }
}
