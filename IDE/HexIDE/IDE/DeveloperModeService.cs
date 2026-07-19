namespace HexIDE.IDE;

/// <summary>
/// Session developer-mode state. <b>Compiled out of Release builds</b>: <see cref="IsEnabled"/> is
/// hard-wired to <c>false</c> unless this is a <c>DEBUG</c> build, so a distributed (Release) binary
/// has no code path to enter developer mode regardless of the <c>--developer-mode</c> flag. In Debug
/// builds it reflects <see cref="Static.DeveloperMode"/> (set once at startup from the flag; never
/// persisted). Injectable so the Options tree and add-in gate stay testable.
/// </summary>
internal sealed class DeveloperModeService : IDeveloperModeService
{
    public bool IsEnabled =>
#if DEBUG
        Static.DeveloperMode;
#else
        false;   // developer mode does not exist in Release builds
#endif
}
