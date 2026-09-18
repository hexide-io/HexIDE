namespace HexIDE.Lsp;

/// <summary>
/// Produces the content of a file a language server reads before it can analyse anything — a compilation
/// database, a project descriptor, a tool configuration.
///
/// <para>
/// <b>A provider returns content and never writes.</b> The path check, the user's consent and the write
/// itself belong to one place, so that adding a provider cannot widen what providers are able to do. That
/// matters most for the providers this tree will not have written.
/// </para>
/// </summary>
public interface IWorkspaceArtifactProvider
{
    /// <summary>
    /// How configuration names this provider. Stable, because it is written in a user's file.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The content to write for this project, or <c>null</c> for "nothing to describe".
    /// </summary>
    /// <remarks>
    /// <b>Null is an ordinary answer, not a failure.</b> A descriptor for a language the project contains
    /// no files of has nothing to say, and writing an empty one is worse than writing none: a server that
    /// finds an empty descriptor concludes the project is empty, where a server that finds none falls back
    /// to whatever it does unaided.
    /// </remarks>
    string? Produce(WorkspaceArtifactContext context);
}

/// <summary>
/// What a provider is given.
/// </summary>
/// <param name="WorkspaceRoot">
/// The absolute host path of the workspace root. The artefact's own path is resolved against this, and a
/// provider emitting paths generally wants them relative to it.
/// </param>
/// <param name="Project">The project being described.</param>
public sealed record WorkspaceArtifactContext(string WorkspaceRoot, WorkspaceProjectSnapshot Project);

/// <summary>
/// The providers this session knows, looked up by the name configuration uses.
/// </summary>
/// <remarks>
/// A registry rather than a fixed set because the point of the seam is that the set is open: a provider
/// contributed by an extension resolves here exactly as a built-in one does, and configuration cannot tell
/// the difference.
/// </remarks>
public interface IWorkspaceArtifactProviderRegistry
{
    /// <summary>
    /// The provider of that name, or <c>null</c> when nothing has registered one.
    /// </summary>
    /// <remarks>
    /// Names are compared case-insensitively. They are written by hand in a configuration file, and a
    /// provider that silently fails to resolve over letter case would present as a server that starts and
    /// says nothing — the failure this whole capability exists to remove.
    /// </remarks>
    IWorkspaceArtifactProvider? Find(string name);

    /// <summary>Every registered provider's name, for reporting what was available when one did not resolve.</summary>
    IReadOnlyCollection<string> Names { get; }
}

/// <summary>
/// The ordinary registry: whatever was supplied at construction.
/// </summary>
/// <remarks>
/// Deliberately not mutable. Providers are resolved while a server is starting, and a set that could change
/// underneath that would make "which provider produced this file" unanswerable after the fact.
/// </remarks>
public sealed class WorkspaceArtifactProviderRegistry : IWorkspaceArtifactProviderRegistry
{
    private readonly Dictionary<string, IWorkspaceArtifactProvider> _byName;

    public WorkspaceArtifactProviderRegistry(IEnumerable<IWorkspaceArtifactProvider> providers)
    {
        _byName = new Dictionary<string, IWorkspaceArtifactProvider>(StringComparer.OrdinalIgnoreCase);

        // Last registration of a name wins, matching how a user entry replaces a default elsewhere in this
        // configuration. Silently, because the alternative — refusing to start over a duplicate — would let
        // one extension stop the IDE.
        foreach (var provider in providers) _byName[provider.Name] = provider;
    }

    public IWorkspaceArtifactProvider? Find(string name) =>
        _byName.TryGetValue(name, out var provider) ? provider : null;

    public IReadOnlyCollection<string> Names => _byName.Keys;
}

/// <summary>
/// A server's declared need for a generated file, after the configuration has been checked.
/// </summary>
/// <remarks>
/// Only ever built by the loader, and only for a declaration that passed every check, so the launch path
/// can act on one without re-validating. The path is kept relative because that is what was written and
/// what must be shown back to the user; it is resolved against the workspace at the moment of writing,
/// since the workspace can change while the configuration does not.
/// </remarks>
/// <param name="RelativePath">Where the file goes, relative to the workspace root.</param>
/// <param name="ProviderName">The provider that produces it.</param>
public sealed record WorkspaceArtifactSpec(string RelativePath, string ProviderName);
