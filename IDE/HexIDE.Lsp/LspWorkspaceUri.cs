namespace HexIDE.Lsp;

/// <summary>
/// How a workspace directory is spelled as a URI — in one place, because two places would drift.
/// </summary>
/// <remarks>
/// <b>This exists because two things have to agree and nothing was making them.</b> The client sends the
/// workspace to a server as <c>rootUri</c> at initialize; a launched server may also be told it on its own
/// command line, through the <c>{workspaceUri}</c> placeholder. A server that takes both and finds them
/// naming different directories does not fail — it loads one workspace and answers questions about the
/// other, which presents as a server that is simply wrong about the code in front of it.
///
/// <para>
/// Two independently-written conversions would agree on the easy paths and part company on a UNC share, a
/// trailing separator, or a path needing percent-encoding. Sharing the spelling is the only way the
/// agreement is a fact rather than a coincidence.
/// </para>
/// </remarks>
public static class LspWorkspaceUri
{
    /// <summary>
    /// The absolute <c>file:</c> URI for a workspace directory, or null when there is not one to express.
    /// </summary>
    /// <remarks>
    /// Null covers both "no project is open" and "this path cannot be made into a URI". The caller decides
    /// what that costs — for the client it costs the root, for a launch it costs the launch — but neither
    /// should be guessing at a URI.
    /// </remarks>
    public static string? For(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;

        try
        {
            return new Uri(Path.GetFullPath(directory)).AbsoluteUri;
        }
        catch (Exception)
        {
            // Deliberately silent: the two callers log with their own context, and a path that cannot be
            // expressed is a fact about the path rather than an error in expressing it.
            return null;
        }
    }
}
