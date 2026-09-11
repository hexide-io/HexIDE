using System;
using Avalonia.Controls.ApplicationLifetimes;
using HexIDE.IDE;
using HexIDE.Projects;

namespace HexIDE;

public class Static
{
    public static bool IsBrowser = OperatingSystem.IsBrowser();

    public static PersonalityName Personality { get; set; } = PersonalityName.Vb6;

    /// <summary>Session developer mode — set once at startup from the <c>--developer-mode</c> CLI flag;
    /// never persisted. Surfaced to consumers via <see cref="IDE.IDeveloperModeService"/>.</summary>
    public static bool DeveloperMode { get; set; }

    /// <summary>
    /// Arm the protocol capture for every language-server connection — set once at startup from the
    /// <c>--capture-lsp</c> CLI flag; never persisted.
    /// </summary>
    /// <remarks>
    /// Read in <c>DISetup</c> when the conversation log is constructed, which is the only place early
    /// enough: servers start on the first document of a language they claim, and the desktop startup hook
    /// runs well after that. Deliberately not gated on <c>DEBUG</c> — the capture ships and the automation
    /// server does not.
    /// </remarks>
    public static bool CaptureLsp { get; set; }

    /// <summary>
    /// Suppresses the save-changes prompt on the next window close, discarding unsaved work. Set only by
    /// automation (the <c>shutdown_ide</c> MCP tool), which would otherwise wedge on a modal dialog.
    /// Never set from a user-driven path.
    /// </summary>
    public static bool ForceCloseWithoutPrompt { get; set; }

    public static bool ForceSingleView => false;

    public static bool SupportsWindowing { get; } = OperatingSystem.IsWindows() ||
                                             OperatingSystem.IsLinux() ||
                                             OperatingSystem.IsMacOS();

    public static bool SingleView { get; set; }

    public static MainView MainView { get; set; } = null!;

    public static MainViewViewModel RootViewModel { get; set; } = null!;

    public static Action<DISetup, IClassicDesktopStyleApplicationLifetime>? DesktopStartupHook { get; set; }

    public static Func<IProjectService, IProjectManager, Task>? StartupProjectHook { get; set; }
}