using System;
using System.Collections.Generic;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Sidecar;

public partial class UserSidecarService
{
    private void ApplyConditions(ProjectDefinition project, UserSidecarData data, HashSet<string> owned)
    {
        foreach (var document in DocumentLookup.DocumentsOf(project))
        {
            if (!owned.Contains(document.Name)) continue;
            var shift = DocumentContent.TextOf(document, null, format, reader).HeaderLineCount;
            Apply(data.CodeBreakpointConditions, shift);
            Apply(data.HeaderBreakpointConditions, 0);

            void Apply(Dictionary<string, Dictionary<int, string>>? entries, int offset)
            {
                if (entries == null) return;
                foreach (var (name, conditions) in entries)
                {
                    if (!NameIn(name).Equals(document.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var (line, expression) in conditions)
                        if (breakpointService.IsBreakpoint(document, line + offset))
                            breakpointService.SetCondition(document, line + offset, expression);
                }
            }
        }
    }

    private Dictionary<string, Dictionary<int, string>> CollectConditions(ProjectDefinition project,
        Dictionary<string, Dictionary<int, string>>? source, ProjectState state, bool header)
    {
        var result = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in DocumentLookup.DocumentsOf(project))
        {
            var shift = DocumentContent.TextOf(document, null, format, reader).HeaderLineCount;
            foreach (var line in breakpointService.GetBreakpoints(document))
            {
                if ((line <= shift) != header || breakpointService.GetCondition(document, line) is not { } expression)
                    continue;
                if (!result.TryGetValue(document.Name, out var conditions))
                    result[document.Name] = conditions = [];
                conditions[header ? line : line - shift] = expression;
            }
        }

        // As with marks, carry entries for missing documents, except lines this session wrote itself.
        if (source == null || state.Owned == null) return result;
        var wrote = header ? state.WroteHeader?.Breakpoints : state.Wrote?.Breakpoints;
        foreach (var (name, conditions) in source)
        {
            if (state.Owned.Contains(NameIn(name))) continue;
            foreach (var (line, expression) in conditions)
            {
                if (wrote != null && wrote.TryGetValue(name, out var own) && own.Contains(line)) continue;
                if (!result.TryGetValue(name, out var carried))
                    result[name] = carried = [];
                carried.TryAdd(line, expression);
            }
        }
        return result;
    }
}
