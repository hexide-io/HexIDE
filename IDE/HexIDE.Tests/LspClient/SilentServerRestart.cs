using HexIDE.Conversations;
using HexIDE.Lsp;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// The one restart the foreign-server suite allows a test, and the evidence it demands before giving it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists: rumdl stalls.</b> rumdl 0.2.64 intermittently accepts <c>initialize</c>, runs its whole handler,
/// and never answers, while staying alive. That happens with any client: a bare .NET reader, a blocking read on its
/// own thread, Python's <c>Popen</c>, and a request written 250 ms after the spawn all hit it, on about 0.3% to 3% of
/// starts depending on the runner (hexide-io/HexIDE#390). It is not ours, and it turned main red at random.
/// </para>
/// <para>
/// <b>Why the evidence is strict.</b> A retry hides whatever it retries, and the next silent server could be a fault
/// of ours. So a test is rerun only when its record shows every one of these, which together say the request
/// reached a live server and nothing came back:
/// </para>
/// <list type="number">
/// <item>the connection failed at <see cref="LanguageConnectionStage.HandshakeSent"/> because the handshake timed out,
/// not because it failed: a server that exits fails it at once, and its exit can reach the record after that;</item>
/// <item><c>initialize</c> was sent;</item>
/// <item>the channel tap saw its bytes reach the server's channel (#804);</item>
/// <item>the tap saw our read already waiting for the server's first bytes;</item>
/// <item>no byte ever arrived from the server;</item>
/// <item>the process had not exited by the time the handshake gave up.</item>
/// </list>
/// <para>
/// Not "the process is alive now": an abandoned server that wakes later writes into the pipe we closed and dies of
/// SIGPIPE (exit 141, seen in #390), so a later exit is the stall's normal ending, not evidence against it.
/// </para>
/// <para>
/// <b>Seen, and capped.</b> Every rerun is written to the test's output with its evidence, and on CI to the job's
/// annotations and summary, because a retry nobody can see is the silent skip this suite exists to prevent. More
/// than <see cref="AllowedPerRun"/> in one run fails the test that asked: at the measured rate two is already
/// unusual, and more says something has changed.
/// </para>
/// </remarks>
internal sealed class SilentServerRestart(int allowedPerRun, Action<string>? announce = null)
{
    /// <summary>Shared by every foreign-server test in this process: the cap is per run, not per class.</summary>
    public static SilentServerRestart ForThisRun { get; } = new(2);

    public int AllowedPerRun { get; } = allowedPerRun;

    private readonly Action<string> _announce = announce ?? Announce;

    private int _used;

    public int Used => Volatile.Read(ref _used);

    /// <summary>
    /// Runs <paramref name="body"/>, and once more if it failed because a server accepted <c>initialize</c> and said
    /// nothing. <paramref name="attempt"/> names the registry and record the body used; <paramref name="reset"/>
    /// disposes them and installs fresh ones for the rerun.
    /// </summary>
    public Task RunAsync(
        Func<Task> body,
        Func<(LspClientRegistry? Registry, ConversationLog Capture)> attempt,
        Func<Task> reset) =>
        RunAsync(
            body,
            () => attempt() is ({ } registry, var capture)
                ? SilentConnectionAsync(registry, capture)
                : Task.FromResult<string?>(null),
            reset);

    /// <summary>The same, with the evidence read by <paramref name="silence"/>: null when there is none.</summary>
    public async Task RunAsync(Func<Task> body, Func<Task<string?>> silence, Func<Task> reset)
    {
        try
        {
            await body();
            return;
        }
        catch (Exception first)
        {
            var evidence = await silence();
            if (evidence is null) throw;

            var used = Interlocked.Increment(ref _used);
            if (used > AllowedPerRun)
                throw new InvalidOperationException(
                    $"A foreign server accepted initialize and said nothing, and this is restart {used} in this run, "
                  + $"over the cap of {AllowedPerRun}. rumdl's stall (#390) runs well under that, so something has "
                  + $"changed: look at this record before raising the cap.\n{evidence}", first);

            _announce(
                $"Foreign-server restart {used} of {AllowedPerRun} allowed this run: rumdl's stall "
              + $"(hexide-io/HexIDE#390). The server accepted initialize and said nothing; rerunning the test once.\n"
              + evidence);
            await reset();

            try
            {
                await body();
            }
            catch (Exception second)
            {
                throw new InvalidOperationException(
                    "Restarted once because a foreign server accepted initialize and said nothing (#390), and the "
                  + $"rerun failed too. The first attempt's record:\n{evidence}", second);
            }
        }
    }

    private static async Task<string?> SilentConnectionAsync(LspClientRegistry registry, ConversationLog capture)
    {
        await capture.DrainAsync();
        foreach (var connection in registry.Connections)
        {
            if (Evidence(connection, capture.Snapshot(connection.Id), out _) is { } evidence)
                return evidence + "\n" + ConnectionDiagnostics.Explain(connection, capture);
        }
        return null;
    }

    /// <summary>
    /// What the record shows when this connection's server accepted <c>initialize</c> and stayed silent, or null,
    /// with <paramref name="refusal"/> naming the first piece of evidence that is missing.
    /// </summary>
    public static string? Evidence(
        LanguageServerConnection connection, IReadOnlyList<ConversationEnvelope> wire, out string refusal)
    {
        if (connection.State != LanguageConnectionState.Failed)
        {
            refusal = $"the connection is {connection.State}, not Failed";
            return null;
        }

        if (connection.Attempt is not { ReachedStage: LanguageConnectionStage.HandshakeSent } attempt
            || attempt.Steps.LastOrDefault() is not { Stage: LanguageConnectionStage.HandshakeSent } stopped)
        {
            refusal = "the connection did not stop at HandshakeSent, so initialize was never sent";
            return null;
        }

        if (stopped.Outcome != LanguageConnectionStepOutcome.StoppedHere || stopped.At is not { } at)
        {
            refusal = $"the handshake ended {stopped.Outcome}, not StoppedHere";
            return null;
        }

        if (stopped.Detail?.StartsWith(VBLspClient.InitializeTimeoutDetail, StringComparison.Ordinal) != true)
        {
            refusal = $"the handshake failed rather than timed out: {stopped.Detail}";
            return null;
        }

        var gaveUp = attempt.StartedAt + at;

        if (!wire.Any(e => e is { Direction: ConversationDirection.Sent, Kind: ConversationEntryKind.Request, Method: "initialize" }))
        {
            refusal = "the record has no initialize request";
            return null;
        }

        if (!Noted(wire, gaveUp, d => d.EndsWith(ChannelTapStream.ReachedChannelNote, StringComparison.Ordinal)))
        {
            refusal = "the tap did not see initialize's bytes reach the server's channel";
            return null;
        }

        if (!Noted(wire, gaveUp, d => d == ChannelTapStream.BeganWaitingNote))
        {
            refusal = "the tap did not see our read waiting for the server";
            return null;
        }

        if (Noted(wire, DateTimeOffset.MaxValue, d => d.EndsWith(ChannelTapStream.ArrivedNote, StringComparison.Ordinal)))
        {
            refusal = "bytes arrived from the server";
            return null;
        }

        if (Noted(wire, gaveUp, d => d.StartsWith(StdioProcessLspTransport.ProcessExitedNote, StringComparison.Ordinal)))
        {
            refusal = "the process exited before the handshake gave up";
            return null;
        }

        refusal = "";
        return $"connection '{connection.Id}' sent initialize, its bytes reached the server and our read was waiting, "
             + $"nothing came back, and the process was alive when the handshake gave up after {at.TotalSeconds:0.0}s";
    }

    private static bool Noted(IReadOnlyList<ConversationEnvelope> wire, DateTimeOffset until, Func<string, bool> matches) =>
        wire.Any(e => e.Kind == ConversationEntryKind.Lifecycle && e.Detail is { } d && e.Timestamp <= until && matches(d));

    private static void Announce(string text)
    {
        var headline = text.Split('\n')[0];
        var test = TestContext.Current.Test?.TestDisplayName ?? "(unknown test)";

        TestContext.Current.TestOutputHelper?.WriteLine(text);
        TestContext.Current.SendDiagnosticMessage($"{test}: {headline}");

        // On CI, also where a reader of the run will see it without opening a log: an annotation, and the job summary.
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            Console.WriteLine($"::warning title=Foreign-server restart ({test})::{headline}");
            if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
            {
                try
                {
                    File.AppendAllText(summary, $"\n### Foreign-server restart: {test}\n\n```\n{text}\n```\n");
                }
                catch (IOException)
                {
                    // The annotation and the test output already carry it.
                }
            }
        }
    }
}
