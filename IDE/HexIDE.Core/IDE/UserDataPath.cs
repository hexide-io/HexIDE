namespace HexIDE.IDE;

/// <summary>
/// Where HexIDE keeps a user's own files — settings, consent, revocations, recent projects, language
/// server configuration.
///
/// <para>
/// <b>Exists because the framework's answer is wrong on two of the three platforms.</b> Ten call sites
/// used <c>Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)</c>, which on Unix
/// returns an <b>empty string</b> when <c>XDG_CONFIG_HOME</c> is unset — and it is unset on most Linux
/// distributions by default, because applications are expected to default to <c>~/.config</c>
/// themselves. Measured on the same Ubuntu 24.04 image CI runs:
/// </para>
///
/// <code>
/// ApplicationData        = []
/// LocalApplicationData   = [/root/.local/share]
/// UserProfile            = [/root]
/// XDG_CONFIG_HOME        = []
/// </code>
///
/// <para>
/// <c>Path.Combine("", "HexIDE", "settings.json")</c> is <c>HexIDE/settings.json</c> — a <b>relative</b>
/// path. Every per-user file then landed wherever the process happened to be started from, and moved when
/// that changed. Settings silently not persisting is the mild end; a revocation that cannot be found is
/// a revocation that is not applied (hexide-io/HexIDE#280).
/// </para>
///
/// <para>
/// Note that <c>LocalApplicationData</c> resolves correctly, because .NET maps it to
/// <c>XDG_DATA_HOME</c> <em>with</em> a <c>$HOME/.local/share</c> fallback. <c>ApplicationData</c> has no
/// such fallback. That asymmetry is the whole bug, and it is why "just use the framework" is not the
/// answer here.
/// </para>
/// </summary>
public static class UserDataPath
{
    /// <summary>
    /// The directory holding HexIDE's per-user files, created if absent.
    ///
    /// <para>
    /// Windows keeps <c>%AppData%\HexIDE</c>, which already worked. Unix follows the XDG convention the
    /// framework skips: <c>$XDG_CONFIG_HOME/HexIDE</c> when set, else <c>$HOME/.config/HexIDE</c>.
    /// </para>
    /// </summary>
    public static string Directory => Path.Combine(Base(), "HexIDE");

    /// <summary>A named file inside <see cref="Directory"/>.</summary>
    public static string For(string fileName) => Path.Combine(Directory, fileName);

    /// <summary>
    /// The platform's per-user configuration root.
    ///
    /// <para>
    /// <b>Throws rather than returning a relative path.</b> Every consequence of getting this wrong is
    /// silent — a setting that does not persist, a consent re-asked, a revocation not found — so the one
    /// outcome worth being loud about is not knowing where the user's files live. A process with no
    /// <c>HOME</c> and no <c>XDG_CONFIG_HOME</c> on Unix is not a situation to guess through.
    /// </para>
    /// </summary>
    private static string Base()
    {
        if (OperatingSystem.IsWindows())
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Rooted(windows, "%AppData%");
        }

        if (Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg)
            return Rooted(xdg, "XDG_CONFIG_HOME");

        // The fallback .NET omits. UserProfile rather than the HOME variable directly: it is what the
        // framework itself consults, and it stays correct where HOME is unset but the passwd entry is not.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(Rooted(home, "the user's home directory"), ".config");
    }

    private static string Rooted(string candidate, string what) =>
        !string.IsNullOrEmpty(candidate) && Path.IsPathRooted(candidate)
            ? candidate
            : throw new InvalidOperationException(
                $"Cannot locate HexIDE's per-user directory: {what} resolved to "
              + $"'{candidate}', which is not an absolute path. Refusing to fall back to a relative one — "
              + "settings, consent and add-in revocations would then be written relative to whichever "
              + "directory HexIDE happened to be started from, and silently not found again.");
}
