namespace HexIDE.IDE;

/// <summary>
/// Session-scoped developer mode. <see cref="IsEnabled"/> is true for the current run only when the
/// IDE was launched with <c>--developer-mode</c>; it is never persisted, so a fresh launch without the
/// flag is never in developer mode. Dev-mode capabilities (e.g. loading unsigned add-ins) take effect
/// only when this is true <b>and</b> the relevant persisted capability setting is on.
/// </summary>
public interface IDeveloperModeService
{
    bool IsEnabled { get; }
}
