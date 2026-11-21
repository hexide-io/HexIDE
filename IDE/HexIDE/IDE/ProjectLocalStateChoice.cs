using System.Collections.Generic;
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
}
