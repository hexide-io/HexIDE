namespace HexIDE.IDE;

/// <summary>
/// Where crash recovery keeps each IDE run's copy of unsaved work (hexide-io/HexIDE#499).
/// </summary>
/// <remarks>
/// <para>
/// <b>Local, never roaming.</b> Settings live in <see cref="UserDataPath"/>, which on Windows is the roaming
/// <c>%AppData%\HexIDE</c> and can follow a user to another machine. Unsaved work belongs to the machine it was typed
/// on, so Windows uses <c>%LocalAppData%\HexIDE\Recovery</c>, and Unix uses the XDG state directory, which that
/// convention reserves for state an application reuses on restart: <c>$XDG_STATE_HOME/HexIDE/Recovery</c>, by default
/// <c>~/.local/state/HexIDE/Recovery</c>. A session started with <c>--user-data-dir</c> keeps it in <c>Recovery</c>
/// under that directory instead, so the session stays self-contained.
/// </para>
/// <para>
/// <b>Never beside a project.</b> A project's folder and its user sidecar can be committed to source control.
/// </para>
/// <para>
/// .NET's <c>LocalApplicationData</c> is no help on Unix: it maps to <c>XDG_DATA_HOME</c>, not state, so the Unix
/// directory is worked out here, as <see cref="UserDataPath"/> works out its own.
/// </para>
/// </remarks>
public static class RecoveryPath
{
    /// <summary>The directory holding one folder per IDE run. Not created here.</summary>
    /// <exception cref="InvalidOperationException">The platform's directory resolves to a relative or empty path.</exception>
    public static string Directory => For(
        UserDataPath.IsRedirected ? UserDataPath.Directory : null,
        OperatingSystem.IsWindows(),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetEnvironmentVariable("XDG_STATE_HOME"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>
    /// <see cref="Directory"/> from its inputs, so each platform's rule can be tested on any host.
    /// </summary>
    /// <param name="redirected">The directory named with <c>--user-data-dir</c>, or null.</param>
    /// <param name="windows">Whether the host is Windows.</param>
    /// <param name="localAppData">Windows' local application data directory.</param>
    /// <param name="xdgStateHome"><c>XDG_STATE_HOME</c>, or null when unset.</param>
    /// <param name="home">The user's home directory.</param>
    internal static string For(string? redirected, bool windows, string localAppData, string? xdgStateHome, string home)
    {
        if (redirected is not null)
            return Path.Combine(redirected, "Recovery");

        if (windows)
            return Path.Combine(Rooted(localAppData, "%LocalAppData%"), "HexIDE", "Recovery");

        // The XDG specification has an implementation ignore a relative path in any of its variables, rather than use it.
        if (xdgStateHome is { Length: > 0 } && Path.IsPathRooted(xdgStateHome))
            return Path.Combine(xdgStateHome, "HexIDE", "Recovery");

        return Path.Combine(Rooted(home, "the user's home directory"), ".local", "state", "HexIDE", "Recovery");
    }

    // Loud rather than relative, for the reason UserDataPath gives: a relative path would put the backup wherever the
    // IDE happened to be started from, and the next start would not find it.
    private static string Rooted(string candidate, string what) =>
        !string.IsNullOrEmpty(candidate) && Path.IsPathRooted(candidate)
            ? candidate
            : throw new InvalidOperationException(
                $"Cannot locate HexIDE's recovery directory: {what} resolved to '{candidate}', which is not an absolute "
              + "path. Unsaved work is not backed up this session.");
}
