using System;
using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Sidecar;

public partial class UserSidecarService
{
    // Older builds keyed these files by the .vbp name. Prefer an entry already under the loaded name,
    // and rewrite the fallback before applying the usual line-number migration. Also used on save's fresh read.
    private static bool MigrateLoadedNames(ProjectDefinition project, UserSidecarData data, HashSet<string>? owned)
    {
        var changed = false;
        foreach (var module in project.Modules)
        {
            if (module.ProjectLineName is not { } old || owned?.Contains(module.Name) != true)
                continue;
            Move(data.Bookmarks);
            Move(data.Breakpoints);
            Move(data.FileBookmarks);
            Move(data.FileBreakpoints);
            Move(data.CodeBookmarks);
            Move(data.CodeBreakpoints);
            Move(data.HeaderBookmarks);
            Move(data.HeaderBreakpoints);
            Move(data.CodeBreakpointConditions);
            Move(data.HeaderBreakpointConditions);

            void Move<T>(Dictionary<string, T>? entries)
            {
                if (entries is null) return;
                var previous = entries.Keys.Where(key => NameIn(key).Equals(old, StringComparison.OrdinalIgnoreCase)).ToArray();
                var hasLoaded = entries.Keys.Any(key => NameIn(key).Equals(module.Name, StringComparison.OrdinalIgnoreCase));
                foreach (var key in previous)
                {
                    var value = entries[key];
                    entries.Remove(key);
                    if (!hasLoaded) entries[key[..^NameIn(key).Length] + module.Name] = value;
                    changed = true;
                }
            }
        }
        return changed;
    }
}
