using System.Linq;
using HexIDE.Automation;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Desktop.Server;

/// <summary>What a tool that needs a run engine says when none runs the startup project (#3514).</summary>
internal static class NoRunEngine
{
    /// <summary>
    /// That no engine runs the project, and what can: the launches add-ins offer for it, or, with none, a debug adapter.
    /// Written to stand alone or to follow another sentence.
    /// </summary>
    /// <remarks>
    /// The server's own words are English, as it is compiled out of a Release build. The menu paths come from the IDE's
    /// strings, because <c>invoke_menu_item</c> matches what the menu displays in whatever language the IDE is using.
    /// </remarks>
    public static string Explain(NoEngineStartup none, ILocalizationService strings)
    {
        var who = none.Project.ContributorTitle is { } title ? $", which the add-in {title} read" : "";
        var cannot = $"No run engine in this IDE runs '{none.Project.Name}'{who}, so run_project and the step tools can't start it.";
        return $"{cannot} {(none.Launches.Count > 0 ? WithLaunches(none, strings) : WithNoLaunch(strings))}";
    }

    private static string WithLaunches(NoEngineStartup none, ILocalizationService strings)
    {
        var format = strings.GetString("Str.Menu.Run.RunWith.Launch");
        var paths = none.Launches.Select(launch =>
            $"'{MenuPathOf(strings, "Str.Menu.Run", "Str.Menu.Run.StartWith")}/{MenuPath.StripAccessKey(string.Format(format, launch.Title, launch.Origin))}'");
        return $"A launch add-ins offer for it can: invoke_menu_item with {string.Join(" or ", paths)}, and get_debug_state then "
               + "follows that session.";
    }

    private static string WithNoLaunch(ILocalizationService strings) =>
        "No add-in offers a launch for it. To run it under a debug adapter, answer_next_file_dialog with the adapter's "
        + $"configuration file, then invoke_menu_item with '{MenuPathOf(strings, "Str.Menu.Run", "Str.Menu.Run.StartExternal")}'.";

    private static string MenuPathOf(ILocalizationService strings, params string[] keys) =>
        string.Join('/', keys.Select(key => WithoutEllipsis(MenuPath.StripAccessKey(strings.GetString(key)))));

    // invoke_menu_item doesn't need the "…" that says an item opens a dialog, and the path reads better without it.
    private static string WithoutEllipsis(string header) => header.TrimEnd('…', '.', ' ');
}
