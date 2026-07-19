namespace HexIDE.IDE;

/// <summary>
/// Factory-default values for the user-facing <see cref="ISettingsService"/> options.
/// Single source of truth: <see cref="SettingsService"/>'s field initializers and JSON
/// defaults derive from these constants, and the Tools&#8594;Options "Restore Defaults" /
/// "Reset All" actions read them when previewing a reset.
/// <para>
/// Window geometry is intentionally excluded — it is window state, not a user option
/// (see <see cref="WindowData.Default"/>).
/// </para>
/// </summary>
public static class SettingsDefaults
{
    // ── Editor ──────────────────────────────────────────────────
    public const bool RequireVariableDeclaration = false;
    public const bool AutoListMembers = true;
    public const bool AutoQuickInfo = true;
    public const bool AutoIndent = true;
    public const int TabWidth = 4;
    public const bool FormatOnSave = true;
    public const bool IsMinimapVisible = true;

    // ── Form designer grid ──────────────────────────────────────
    public const bool ShowGrid = true;
    public const int GridWidth = 8;
    public const int GridHeight = 8;
    public const bool AlignToGrid = true;

    // ── Environment ─────────────────────────────────────────────
    public const bool PromptForProjectOnStartup = true;
    public const string ActiveTheme = "Classic";
    public const string ActiveKeymap = "Default";
    /// <summary>"system" follows the OS UI culture each launch; a specific pack id (e.g. "en") pins it.</summary>
    public const string ActiveLanguage = "system";

    /// <summary>Permits loading add-in packages with a missing/invalid signature — but only takes
    /// effect when the session is in developer mode (<c>--developer-mode</c>). Off by default. Editable
    /// only via the dev-mode-gated Developer options page. See <c>IDeveloperModeService</c>.</summary>
    public const bool LoadUnsignedAddins = false;

    /// <summary>Watch loaded component files and reload them when changed outside the IDE. On by default.</summary>
    public const bool ReloadFilesChangedOutsideIde = true;

    // ── Toolbar visibility ──────────────────────────────────────
    public const bool IsStandardToolbarVisible = true;
    public const bool IsEditToolbarVisible = false;
    public const bool IsDebugToolbarVisible = false;
    public const bool IsFormEditorToolbarVisible = false;

    // ── Advanced ────────────────────────────────────────────────
    public const string? LspWebSocketUrl = null;
    public const string? RevocationListUrl = null;
}
