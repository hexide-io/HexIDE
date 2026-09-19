namespace HexIDE.Lsp;

/// <summary>
/// Turns a server entry's <c>workspaceArtifact</c> object into a checked <see cref="WorkspaceArtifactSpec"/>,
/// or into the reason it cannot be one.
///
/// <para>
/// <b>One function, used by both the loader and the registration factory.</b> The loader calls it to report
/// what is wrong, the factory calls it to obtain the spec, and they must agree: a declaration reported as
/// fine and then silently dropped, or vice versa, is a configuration the user cannot reason about. Two
/// copies of these rules would drift the first time one gained a check.
/// </para>
/// </summary>
public static class WorkspaceArtifactSpecReader
{
    /// <summary>
    /// The spec this declaration describes, or null with the reason.
    /// </summary>
    /// <param name="entryId">The server entry, for naming it in a problem.</param>
    /// <param name="declared">The declaration, or null when the entry made none.</param>
    /// <param name="providers">
    /// The providers available, when the caller knows them. Null skips only the provider-exists check —
    /// every other rule is structural and is applied regardless.
    /// </param>
    /// <param name="spec">The checked spec, when there is one.</param>
    /// <param name="problems">Everything wrong with the declaration. Empty when it is fine or absent.</param>
    /// <returns>True when the entry declared a usable artefact.</returns>
    /// <remarks>
    /// <b>A fault here never rejects the entry.</b> Every problem returned is advisory: a server that reads
    /// no descriptor may still be useful, and a server that does not start certainly is not. Refusing to
    /// launch over a misspelled provider name would be a wildly disproportionate answer, and is the same
    /// judgement the trace field already makes.
    /// </remarks>
    public static bool TryRead(
        string entryId,
        WorkspaceArtifactEntry? declared,
        IWorkspaceArtifactProviderRegistry? providers,
        out WorkspaceArtifactSpec? spec,
        out IReadOnlyList<LanguageServerConfigProblem> problems)
    {
        spec = null;
        var found = new List<LanguageServerConfigProblem>();
        problems = found;

        if (declared is null) return false;

        void Reject(string message) => found.Add(new LanguageServerConfigProblem(
            entryId, message, false, LanguageServerConfigProblemKind.Configuration));

        if (declared.Unrecognized is { Count: > 0 })
            found.Add(new LanguageServerConfigProblem(
                entryId,
                $"Unrecognised {(declared.Unrecognized.Count == 1 ? "field" : "fields")} in "
              + $"'{entryId}' workspaceArtifact: "
              + $"{string.Join(", ", declared.Unrecognized.Keys.Order(StringComparer.Ordinal))}. "
              + "Ignored — check the spelling.",
                false,
                LanguageServerConfigProblemKind.UnrecognisedField));

        var path = declared.Path?.Trim();
        var provider = declared.Provider?.Trim();

        if (string.IsNullOrWhiteSpace(path))
        {
            Reject($"'{entryId}' declares a workspaceArtifact with no path. No file will be generated.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            Reject($"'{entryId}' declares a workspaceArtifact at '{path}' but names no provider, "
                 + "so nothing can produce it. No file will be generated.");
            return false;
        }

        if (IsAbsolute(path))
        {
            Reject($"'{entryId}' declares a workspaceArtifact at '{path}', which is an absolute path. "
                 + "It must be relative to the workspace root. No file will be generated.");
            return false;
        }

        if (EscapesWorkspace(path))
        {
            Reject($"'{entryId}' declares a workspaceArtifact at '{path}', which leads outside the "
                 + "workspace. No file will be generated.");
            return false;
        }

        if (providers is not null && providers.Find(provider) is null)
        {
            Reject($"'{entryId}' declares a workspaceArtifact produced by '{provider}', which is not a "
                 + $"provider this build knows. {AvailableProviders(providers)} No file will be generated.");
            return false;
        }

        spec = new WorkspaceArtifactSpec(path, provider);
        return true;
    }

    private static string AvailableProviders(IWorkspaceArtifactProviderRegistry providers) =>
        providers.Names.Count == 0
            ? "No providers are registered."
            : $"Available: {string.Join(", ", providers.Names.Order(StringComparer.OrdinalIgnoreCase))}.";

    /// <summary>
    /// Whether this is rooted anywhere, asked without trusting the host to agree about separators.
    /// </summary>
    /// <remarks>
    /// <b><c>Path.IsPathRooted</c> alone is not enough, and the reason is the separator trap in reverse.</b>
    /// It answers about the host: on Linux, <c>C:\somewhere</c> is not rooted and <c>\\server\share</c> is
    /// an ordinary relative filename, so a declaration that is plainly absolute to the person who wrote it
    /// would pass. The check is on the text, so a configuration file means the same thing on every host it
    /// is opened on — which matters because this file syncs between machines.
    /// </remarks>
    private static bool IsAbsolute(string path) =>
        Path.IsPathRooted(path)
        || path.StartsWith('/') || path.StartsWith('\\')
        || (path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]));

    /// <summary>
    /// Whether any segment climbs out of the workspace.
    /// </summary>
    /// <remarks>
    /// Split on <b>both</b> separators, for the same reason. A backslash is an ordinary filename character
    /// on Linux, so splitting on the host's separator alone would let <c>..\..\elsewhere.json</c> through
    /// there while catching it on Windows — a write outside the workspace on exactly the platform CI runs.
    /// Checked on the text rather than by resolving against a root, because this runs when the
    /// configuration is read and the workspace is not known yet.
    /// </remarks>
    private static bool EscapesWorkspace(string path) =>
        path.Split('/', '\\').Any(segment => segment.Trim() == "..");
}
