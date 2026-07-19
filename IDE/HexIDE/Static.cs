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