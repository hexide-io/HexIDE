using System.ComponentModel;

namespace HexIDE.IDE;

public record WindowData(string State, int X, int Y, int Width, int Height)
{
    public static WindowData Default => new("Maximized", 100, 100, 1280, 800);
}

/// <summary>
/// Persistent IDE settings (Tools > Options).
/// Implementations store values between sessions (e.g. JSON file).
/// Raises <see cref="PropertyChanged"/> so consumers can react to changes.
/// </summary>
public interface ISettingsService : INotifyPropertyChanged
{
    // ── Editor tab ──────────────────────────────────────────────

    /// <summary>
    /// When true, new modules/forms start with <c>Option Explicit</c>.
    /// </summary>
    bool RequireVariableDeclaration { get; set; }

    /// <summary>
    /// Show IntelliSense completion list automatically.
    /// </summary>
    bool AutoListMembers { get; set; }

    /// <summary>
    /// Show hover / Quick Info tooltips automatically.
    /// </summary>
    bool AutoQuickInfo { get; set; }

    /// <summary>
    /// Automatically indent new lines to match the previous line.
    /// </summary>
    bool AutoIndent { get; set; }

    /// <summary>
    /// Number of spaces per indentation level (1–32).
    /// </summary>
    int TabWidth { get; set; }

    /// <summary>
    /// Apply LSP formatting (keyword casing + indentation) on Ctrl+S.
    /// </summary>
    bool FormatOnSave { get; set; }

    // ── General tab ─────────────────────────────────────────────

    /// <summary>
    /// Show the dot grid on the form designer canvas.
    /// </summary>
    bool ShowGrid { get; set; }

    /// <summary>
    /// Horizontal grid spacing in pixels.
    /// </summary>
    int GridWidth { get; set; }

    /// <summary>
    /// Vertical grid spacing in pixels.
    /// </summary>
    int GridHeight { get; set; }

    /// <summary>
    /// Snap control edges to the grid when moving/resizing.
    /// </summary>
    bool AlignToGrid { get; set; }

    // ── Environment tab ─────────────────────────────────────────

    /// <summary>
    /// Show the New Project dialog when the IDE starts.
    /// </summary>
    bool PromptForProjectOnStartup { get; set; }

    /// <summary>
    /// Name of the active IDE chrome theme (e.g. "Classic", "Dark").
    /// </summary>
    string ActiveTheme { get; set; }

    /// <summary>
    /// Name of the active keyboard shortcut pack (e.g. "Default", "VB6").
    /// </summary>
    string ActiveKeymap { get; set; }

    /// <summary>
    /// Id of the active IDE-chrome language pack. <c>"system"</c> (the default) follows the OS UI
    /// culture each launch; a specific pack id (e.g. <c>"en"</c>) pins that language. A pack-selector
    /// string only — deliberately decoupled from thread culture (see <c>LocalizationService</c>), so it
    /// never disturbs the invariant-culture lock that keeps VB6 number parsing stable.
    /// </summary>
    string ActiveLanguage { get; set; }

    /// <summary>
    /// Permits loading add-in packages whose signature is missing or invalid. <b>Gated by developer
    /// mode</b>: it has no effect unless the session was launched with <c>--developer-mode</c>
    /// (see <c>IDeveloperModeService</c>). Off by default; editable only via the Developer options page.
    /// </summary>
    bool LoadUnsignedAddins { get; set; }

    /// <summary>
    /// When true (the default), the IDE watches loaded component files (<c>.frm/.ctl/.pag/.bas/.cls</c>)
    /// and reloads them when they change on disk outside the IDE — silently when there are no unsaved
    /// edits, or via a conflict dialog when there are. When false, the file watcher is inert.
    /// </summary>
    bool ReloadFilesChangedOutsideIde { get; set; }

    // ── Toolbar visibility ──────────────────────────────────────────

    /// <summary>Standard toolbar (VB6 default: visible).</summary>
    bool IsStandardToolbarVisible { get; set; }

    /// <summary>Edit toolbar (VB6 default: hidden).</summary>
    bool IsEditToolbarVisible { get; set; }

    /// <summary>Debug toolbar (VB6 default: hidden).</summary>
    bool IsDebugToolbarVisible { get; set; }

    /// <summary>Form Editor toolbar (VB6 default: hidden).</summary>
    bool IsFormEditorToolbarVisible { get; set; }

    // ── Editor extras ───────────────────────────────────────────────

    /// <summary>Show the code-editor minimap (default: visible).</summary>
    bool IsMinimapVisible { get; set; }

    // ── Window state ────────────────────────────────────────────

    /// <summary>
    /// Window position, size, and state for the next launch.
    /// </summary>
    WindowData Window { get; set; }

    // ── Language server ─────────────────────────────────────────

    /// <summary>
    /// Optional WebSocket endpoint (e.g. <c>ws://localhost:8123/</c>) for the VB6 LSP server.
    /// When set, the IDE connects to the language server over WebSocket instead of spawning it as a
    /// stdio subprocess. The <c>HEXIDE_LSP_WS_URL</c> environment variable overrides this value.
    /// Null/empty = default stdio subprocess transport.
    /// </summary>
    string? LspWebSocketUrl { get; set; }

    /// <summary>
    /// Optional URL of a HexIDE-root-signed add-in revocation list. When set, it is fetched at startup
    /// (the list at <c>&lt;url&gt;</c>, its signature at <c>&lt;url&gt;.sig</c>) with a short timeout and
    /// merged by freshest-validly-signed-wins. Null/empty (default) = bundled floor + cache only, no fetch.
    /// </summary>
    string? RevocationListUrl { get; set; }

    // ── Persistence ─────────────────────────────────────────────

    /// <summary>
    /// Persists the current values to disk.
    /// </summary>
    void Save();

    /// <summary>
    /// Reloads values from disk, discarding in-memory changes.
    /// </summary>
    void Load();
}
