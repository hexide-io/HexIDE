// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// The server's own trace channel: `trace` at initialize and `$/setTrace` in, `$/logTrace` out.

using System.Globalization;
using System.Text.Json;
using EmmyLua.LanguageServer.Framework.Protocol.JsonRpc;
using EmmyLua.LanguageServer.Framework.Protocol.Model;
using EmmyLua.LanguageServer.Framework.Server;
using Serilog;

namespace HexIDE.VbLspServer;

/// <summary>The three levels this server understands. Absent, unreadable or unrecognised is never one of them.</summary>
internal enum LspTraceLevel
{
    Off,
    Messages,
    Verbose,
}

/// <summary>
/// Emits <c>$/logTrace</c>, and owns the level it is emitted at.
/// </summary>
/// <remarks>
/// <para>
/// <b>What goes on this channel, and what deliberately does not.</b> A client can already see every method
/// name, every payload and every elapsed time by capturing its own wire traffic, so echoing those back
/// would prove the notification works and teach nothing. What only this process knows is <em>why</em> an
/// analysis cost what it did: which of the two prediction stages answered, whether the wall-clock budget
/// expired and previous diagnostics were kept, and what came out. That is what is emitted here.
/// </para>
/// <para>
/// <b>Off costs nothing.</b> The level is checked before a message is composed, before a report is
/// requested from the parse, and before anything is written — so a server nobody has asked to trace does
/// exactly what it did before this file existed.
/// </para>
/// <para>
/// <b>The level is per connection.</b> Read from <c>initialize</c>, changed by <c>$/setTrace</c>, and held
/// nowhere else — a second connection to a second server process starts at off again, which is what the
/// protocol describes.
/// </para>
/// </remarks>
internal sealed class TraceReporter(LanguageServer server)
{
    // Unsynchronized deliberately, for the same reason DocumentStore's caches are: the SingleThreadScheduler
    // awaits each handler to completion before dispatching the next, so a $/setTrace write is strictly
    // ordered against every read rather than racing one. Reads do happen on pool threads — a parse hops
    // off-thread and its continuation may resume anywhere — but the await chain carries the happens-before
    // edge. Marking this volatile would advertise a concurrency the scheduler forbids.
    private LspTraceLevel _level = LspTraceLevel.Off;

    /// <summary>The current level. Off until a client says otherwise.</summary>
    public LspTraceLevel Level => _level;

    /// <summary>
    /// Whether anything at all should be emitted. Callers read this <em>before</em> doing the work that
    /// would feed a trace line, not after — that is what makes "off costs nothing" true rather than
    /// merely quiet.
    /// </summary>
    public bool IsEnabled => _level != LspTraceLevel.Off;

    // ── level, in ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies the <c>trace</c> member of <c>initialize</c> params. Absent means off, which is where the
    /// level already is — so an absent, null or unrecognised value all leave it alone.
    /// </summary>
    public void ApplyInitialize(TraceValue? value)
    {
        if (value is null)
        {
            Log.Debug("initialize carried no trace value; tracing stays off");
            return;
        }

        var raw = value.Value.Value;
        if (TryParseLevel(raw, out var level))
        {
            _level = level;
            Log.Information("trace level set to {Level} by initialize", Wire(level));
        }
        else
        {
            Log.Warning("initialize carried an unrecognised trace value {Value}; tracing stays off", raw);
        }
    }

    /// <summary>
    /// Applies a <c>$/setTrace</c> notification. Params are read straight from the request JSON rather
    /// than through a typed model, for the same reason the document-sync handlers do: a value this server
    /// does not recognise has to be observable as such, and a shape it does not expect must be survivable.
    /// </summary>
    public async Task ApplySetTraceAsync(JsonElement? @params)
    {
        var raw = ReadValue(@params);

        if (TryParseLevel(raw, out var level))
        {
            var previous = _level;
            _level = level;
            Log.Information("trace level {Previous} -> {Level} ($/setTrace)", Wire(previous), Wire(level));
            return;
        }

        // Ignored, not treated as off: a client that mistypes one notification has not asked for silence,
        // and silently downgrading would look exactly like a server that does not trace at all — the one
        // failure this whole channel exists to distinguish from.
        //
        // NB a fourth value, `compact`, is easy to believe in and does not exist. It appears in one
        // editor client library's own rendering enum, not on the wire: the pinned 3.17 model carries
        // exactly three values and the word appears nowhere in it, and 3.18 still defines
        // `off | messages | verbose`. Checked, because a comment asserting otherwise was written here
        // first and would have licensed accepting a level no client is entitled to send.
        Log.Warning("$/setTrace carried an unrecognised value {Value}; the level stays {Level}",
            raw ?? "(absent)", Wire(_level));

        // Tell the client, if it is listening at all. It cannot learn this any other way: $/setTrace is a
        // notification with no response, and the protocol defines no capability by which a client can ask
        // whether a server honours trace.
        await LogAsync(
            $"$/setTrace value '{raw ?? "(absent)"}' not recognised; level stays {Wire(_level)}",
            verbose: null).ConfigureAwait(false);
    }

    // ── trace, out ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Reports one document analysis — the parse the client just triggered, from the inside.</summary>
    public Task ReportAnalysisAsync(string uri, DocumentAnalysis analysis)
    {
        if (_level == LspTraceLevel.Off) return Task.CompletedTask;
        return LogAsync(
            Summarise(uri, analysis),
            _level == LspTraceLevel.Verbose ? Detail(analysis) : null);
    }

    /// <summary>
    /// Reports a decision the server took about a document that the client cannot infer from the wire —
    /// it sees an empty diagnostics array, which is indistinguishable from a file with nothing wrong.
    /// </summary>
    public Task ReportDocumentRefusedAsync(string uri, string reason) =>
        LogAsync($"{uri}: {reason}", verbose: null);

    /// <summary>
    /// Composes and sends one <c>$/logTrace</c>. The <c>verbose</c> member is written ONLY at the verbose
    /// level — at <c>messages</c> the protocol asks for the summary line alone, and a client that received
    /// the detail anyway would be showing what it explicitly did not ask for.
    /// </summary>
    private Task LogAsync(string message, string? verbose)
    {
        var level = _level;
        if (level == LspTraceLevel.Off) return Task.CompletedTask;

        var payload = LspJson.Doc(w =>
        {
            w.WriteStartObject();
            w.WriteString("message", message);
            if (level == LspTraceLevel.Verbose && verbose is not null)
                w.WriteString("verbose", verbose);
            w.WriteEndObject();
        });

        return server.SendNotification(new NotificationMessage("$/logTrace", payload));
    }

    // ── wording ─────────────────────────────────────────────────────────────────────────────────

    private static string Summarise(string uri, DocumentAnalysis a) => a.Outcome switch
    {
        ParseOutcome.BudgetExhausted =>
            $"{uri}: parse budget exhausted after {Ms(a.Elapsed)}; previous diagnostics kept",
        ParseOutcome.InputTooLarge =>
            $"{uri}: {a.SourceLength} chars is over the live-analysis ceiling; not parsed",
        ParseOutcome.NestingTooDeep =>
            $"{uri}: nesting too deep to analyse; abandoned after {Ms(a.Elapsed)}",
        _ =>
            $"{uri}: {Stage(a.Prediction)}, {Ms(a.Elapsed)}, "
          + $"{Plural(a.DiagnosticCount, "diagnostic")}, {Plural(a.SymbolCount, "symbol")}",
    };

    private static string Detail(DocumentAnalysis a) => string.Join('\n',
    [
        $"prediction: {Explain(a.Prediction)}",
        $"parse: {Ms(a.Elapsed)} of a {Ms(VbDiagnosticsProvider.ParseBudget)} budget",
        $"outcome: {Explain(a.Outcome)}",
        $"source: {a.SourceLength} chars",
        $"diagnostics: {a.DiagnosticCount}",
        $"symbols: {a.SymbolCount}",
    ]);

    private static string Stage(ParsePrediction p) => p switch
    {
        ParsePrediction.Sll => "SLL",
        ParsePrediction.Ll  => "LL fallback",
        _                   => "no parse",
    };

    private static string Explain(ParsePrediction p) => p switch
    {
        ParsePrediction.Sll => "SLL — the fast path answered; no LL re-parse was needed",
        ParsePrediction.Ll  => "LL — SLL bailed (a syntax error, or VB6's call-vs-array ambiguity) "
                             + "and the authoritative LL(*) re-parse produced the tree",
        _                   => "none — no stage completed",
    };

    private static string Explain(ParseOutcome o) => o switch
    {
        ParseOutcome.Parsed          => "parsed",
        ParseOutcome.InputTooLarge   => "refused: over the live-analysis size ceiling",
        ParseOutcome.NestingTooDeep  => "abandoned: the parse depth guard fired",
        _                            => "abandoned: the wall-clock budget expired; every cache kept its "
                                      + "previous contents and the parse is still running, orphaned",
    };

    private static string Ms(TimeSpan t) =>
        t.TotalMilliseconds.ToString("0.#", CultureInfo.InvariantCulture) + " ms";

    private static string Plural(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static string Wire(LspTraceLevel level) => level switch
    {
        LspTraceLevel.Messages => "messages",
        LspTraceLevel.Verbose  => "verbose",
        _                      => "off",
    };

    // ── parsing ─────────────────────────────────────────────────────────────────────────────────

    private static string? ReadValue(JsonElement? @params) =>
        @params is { ValueKind: JsonValueKind.Object } p
        && p.TryGetProperty("value", out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>
    /// The protocol's three values, matched exactly. Not case-insensitively: <c>TraceValues</c> is an
    /// enumeration of literals, so accepting "Verbose" would be inventing a value the specification does
    /// not define and quietly disagreeing with the next server the same client talks to.
    /// </summary>
    private static bool TryParseLevel(string? raw, out LspTraceLevel level)
    {
        switch (raw)
        {
            case "off":      level = LspTraceLevel.Off;      return true;
            case "messages": level = LspTraceLevel.Messages; return true;
            case "verbose":  level = LspTraceLevel.Verbose;  return true;
            default:         level = LspTraceLevel.Off;      return false;
        }
    }
}
