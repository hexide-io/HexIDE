using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HexIDE.Lsp;

/// <summary>What the configuration came to, and everything wrong with it.</summary>
public sealed record LanguageServerConfigResult(
    IReadOnlyList<LanguageServerEntry> Entries,
    IReadOnlyList<LanguageServerConfigProblem> Problems);

/// <summary>
/// Reads <c>%AppData%/HexIDE/lsp-servers.json</c> and layers it over the entries HexIDE contributes itself.
///
/// <para>
/// <b>Layering rather than replacement is what makes the bundled server safe to express as an entry.</b>
/// Defaults live in code, so an improvement to one reaches an existing user, and a user who renders their
/// own file unusable recovers by deleting it rather than by reinstalling. That is the same shape every
/// other extension point here uses — themes over Classic, keymaps over a factory snapshot, translations
/// over canonical English, settings over their defaults.
/// </para>
///
/// <para>
/// Nothing here throws. A configuration file is the one input guaranteed to be written by hand, so every
/// way it can be wrong is an ordinary occurrence rather than an exceptional one, and the IDE has to start
/// regardless.
/// </para>
/// </summary>
public sealed class LanguageServerConfigLoader
{
    /// <summary>The shape this loader understands. A file declaring a later one is left alone.</summary>
    public const int SupportedVersion = 1;

    private readonly string _filePath;
    private readonly ILogger<LanguageServerConfigLoader> _logger;

    public LanguageServerConfigLoader(ILogger<LanguageServerConfigLoader> logger)
        : this(DefaultFilePath(), logger) { }

    /// <param name="filePath">Explicit path, so tests drive real files rather than a mocked filesystem.</param>
    public LanguageServerConfigLoader(string filePath, ILogger<LanguageServerConfigLoader> logger)
    {
        _filePath = filePath;
        _logger = logger;
    }

    public static string DefaultFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "HexIDE", "lsp-servers.json");
    }

    /// <summary>
    /// The defaults with the user's file layered over them, and everything that was wrong with it.
    ///
    /// <para>
    /// A user entry sharing a default's id <b>replaces</b> that default rather than merging field by field.
    /// Field-level merge would mean an entry could not remove something the default set, and would make the
    /// effective configuration something the user cannot read off their own file.
    /// </para>
    /// </summary>
    public LanguageServerConfigResult Load(
        IReadOnlyList<LanguageServerEntry> defaults, LanguageServerCommandStore? seen = null)
    {
        var problems = new List<LanguageServerConfigProblem>();
        var user = ReadUserEntries(problems);

        // Defaults first, then user entries replace by id and new ones append. Ordinal because an id is an
        // identifier, not prose — two ids differing only by case are two servers.
        var byId = new Dictionary<string, LanguageServerEntry>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var entry in defaults.Concat(user))
        {
            if (!Validate(entry, problems, out var id))
                continue;
            if (!byId.ContainsKey(id))
                order.Add(id);
            byId[id] = entry;
        }

        var entries = order.Select(id => byId[id]).ToList();
        AnnounceUnseenCommands(entries, seen, problems);   // instance: it logs
        return new LanguageServerConfigResult(entries, problems);
    }

    /// <summary>
    /// Reports any entry naming a command the IDE has not launched before, and records it as seen.
    ///
    /// <para>
    /// The entry is <b>not</b> rejected. The overwhelmingly common case is a user who just wrote it, and
    /// refusing to run what someone typed into their own file would be theatre. What this prevents is the
    /// launch being <em>silent</em>: <c>lsp-servers.json</c> is an ordinary file any process running as the
    /// user may write, and an entry naming an executable is launched on every start thereafter.
    /// </para>
    ///
    /// <para>
    /// <b>Recorded as seen at the same moment it is announced</b>, so a command is reported once rather than
    /// on every start. That is only sound because announcing costs a notice: were this a gate, recording
    /// before the user had actually answered would defeat it.
    /// </para>
    ///
    /// <para>
    /// A disabled entry is skipped — nothing will be launched, so there is nothing to announce, and warning
    /// about a command that will not run trains people to ignore the warning.
    /// </para>
    /// </summary>
    private void AnnounceUnseenCommands(
        IReadOnlyList<LanguageServerEntry> entries,
        LanguageServerCommandStore? seen,
        List<LanguageServerConfigProblem> problems)
    {
        if (seen is null) return;

        foreach (var entry in entries)
        {
            if (entry.Enabled == false || entry.Id is not { } id) continue;
            if (LaunchedCommandOf(entry) is not { } command) continue;

            if (!seen.IsNewOrChanged(id, command)) continue;

            problems.Add(new LanguageServerConfigProblem(
                id,
                $"'{id}' will run a command HexIDE has not run before: {command}",
                false,
                LanguageServerConfigProblemKind.UnseenCommand));

            // Warning, not debug, and deliberately not only carried in the problem list. Until something
            // renders these (#259), the log is the ONLY place this reaches a person — and a notice nobody
            // can see is the failure this requirement exists to prevent.
            _logger.LogWarning(
                "Language server '{Id}' will run a command HexIDE has not run before: {Command}", id, command);

            seen.Record(id, command);
        }
    }

    /// <summary>
    /// What this entry will actually launch, or null when it launches nothing.
    ///
    /// <para>
    /// Only the stdio transport starts a process. A WebSocket endpoint or a pipe HexIDE merely connects to
    /// is someone else's decision to have run something, and announcing it would be reporting a fact about
    /// a program the IDE did not start.
    /// </para>
    ///
    /// <para>
    /// Arguments are part of the identity. <c>node</c> is harmless; <c>node /tmp/x.js</c> is whatever
    /// <c>x.js</c> says, so changing the arguments changes what runs just as surely as changing the
    /// executable.
    /// </para>
    /// </summary>
    private static string? LaunchedCommandOf(LanguageServerEntry entry)
    {
        if (!string.Equals(entry.Transport?.Trim(), "stdio", StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.IsNullOrWhiteSpace(entry.Command)) return null;

        return string.IsNullOrWhiteSpace(entry.Arguments)
            ? entry.Command.Trim()
            : $"{entry.Command.Trim()} {entry.Arguments.Trim()}";
    }

    private IReadOnlyList<LanguageServerEntry> ReadUserEntries(List<LanguageServerConfigProblem> problems)
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogDebug("No language server configuration at {Path}; using defaults", _filePath);
                return [];
            }

            // Comments and trailing commas are allowed in this one file. It is hand-edited, it is the file
            // a user can lock themselves out of, and it therefore ships explaining itself — which JSON
            // cannot do without them.
            var options = new JsonSerializerOptions(LanguageServerConfigJsonContext.Default.Options)
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };

            var text = File.ReadAllText(_filePath);
            var file = JsonSerializer.Deserialize<LanguageServerConfigFile>(text, options);
            if (file is null)
            {
                problems.Add(new LanguageServerConfigProblem(null, "The configuration file is empty.", true));
                return [];
            }

            if (file.Version > SupportedVersion)
            {
                // Ignored rather than guessed at. A file from a newer HexIDE may mean something different by
                // the same field, and half-reading it is worse than not reading it — the same call the
                // add-in consent store makes about a future schema.
                problems.Add(new LanguageServerConfigProblem(
                    null,
                    $"The configuration declares version {file.Version}, but this HexIDE understands "
                  + $"{SupportedVersion}. It has been ignored; the built-in servers are in use.",
                    true));
                return [];
            }

            return file.Servers ?? [];
        }
        catch (Exception ex)
        {
            // Every entry is lost, which is why the message has to reach a person and not only the log.
            _logger.LogWarning(ex, "Could not read language server configuration at {Path}", _filePath);
            problems.Add(new LanguageServerConfigProblem(
                null, $"The configuration file could not be read: {ex.Message}", true));
            return [];
        }
    }

    /// <summary>
    /// Whether an entry can be used, recording why not when it cannot.
    ///
    /// <para>
    /// Only what is needed to <em>reach</em> the server is required. A disabled entry needs nothing but an
    /// id — demanding a valid command from an entry that will never be launched would make switching a
    /// server off harder than deleting it, which is backwards.
    /// </para>
    /// </summary>
    private static bool Validate(
        LanguageServerEntry entry, List<LanguageServerConfigProblem> problems, out string id)
    {
        id = entry.Id ?? "";

        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            problems.Add(new LanguageServerConfigProblem(
                null, "An entry has no id, so nothing can refer to it. It has been ignored.", true));
            return false;
        }

        // Reported before the enabled check: a typo is worth surfacing whether or not the entry is on,
        // because "I disabled it and it still does not work when I re-enable it" is the same confusion
        // deferred.
        if (entry.Unrecognized is { Count: > 0 })
            problems.Add(new LanguageServerConfigProblem(
                entry.Id,
                $"Unrecognised {(entry.Unrecognized.Count == 1 ? "field" : "fields")}: "
              + $"{string.Join(", ", entry.Unrecognized.Keys.Order(StringComparer.Ordinal))}. "
              + "Ignored — check the spelling.",
                false,
                LanguageServerConfigProblemKind.UnrecognisedField));

        if (entry.Enabled == false)
            return true;

        var missing = new List<string>();

        if (entry.Extensions is null or { Length: 0 })
            missing.Add("extensions");
        if (string.IsNullOrWhiteSpace(entry.LanguageId))
            missing.Add("languageId");

        switch (entry.Transport?.Trim().ToLowerInvariant())
        {
            case "stdio":
                if (string.IsNullOrWhiteSpace(entry.Command)) missing.Add("command");
                break;
            case "websocket":
                if (string.IsNullOrWhiteSpace(entry.Endpoint)) missing.Add("endpoint");
                break;
            case "pipe":
                if (string.IsNullOrWhiteSpace(entry.PipeName)) missing.Add("pipeName");
                break;
            case null or "":
                missing.Add("transport");
                break;
            default:
                problems.Add(new LanguageServerConfigProblem(
                    entry.Id,
                    $"Unknown transport '{entry.Transport}'. Expected stdio, pipe or websocket. "
                  + "The entry has been ignored.",
                    true));
                return false;
        }

        if (missing.Count == 0)
            return true;

        problems.Add(new LanguageServerConfigProblem(
            entry.Id,
            $"Missing required {(missing.Count == 1 ? "field" : "fields")}: {string.Join(", ", missing)}. "
          + "The entry has been ignored.",
            true));
        return false;
    }
}
