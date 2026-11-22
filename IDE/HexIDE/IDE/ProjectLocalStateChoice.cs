using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.Localization;

namespace HexIDE.IDE;

internal static class ProjectLocalStateChoice
{
    internal static async Task<string?> Ask(IWindowManager windows, ILocalizationService localization,
        IReadOnlyList<string> candidates)
    {
        var names = string.Join("\n", candidates.Select((path, i) => $"{i + 1}. {path}"));
        var prompt = string.Format(localization.GetString("Str.ProjectLocalState.ChooseFormat"), names);
        while (true)
        {
            var answer = await windows.InputBox(prompt, Distribution.DisplayName, "");
            if (answer == null) return null;
            if (int.TryParse(answer, out var index) && index >= 1 && index <= candidates.Count)
                return candidates[index - 1];
        }
    }

    /// <summary>Tells the person that a project's sidecar is refused, so its marks will not be kept (#5163).</summary>
    internal static Task SidecarUnavailable(IWindowManager windows, ILocalizationService localization,
        string project, string statePath)
    {
        var reason = string.Format(localization.GetString("Str.ProjectLocalState.UnavailableFormat"), statePath);
        var text = string.Format(localization.GetString("Str.ProjectLocalState.SidecarUnavailableFormat"),
            Path.GetFileName(project), reason);
        return windows.MessageBox(text, Distribution.DisplayName, icon: MessageBoxIcon.Warning);
    }
}
