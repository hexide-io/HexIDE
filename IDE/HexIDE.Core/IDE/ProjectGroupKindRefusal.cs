using System;
using System.Collections.Generic;

namespace HexIDE.IDE;

/// <summary>A VB6 group cannot safely list another project system's file (oracle PR #4308).</summary>
public sealed class ProjectGroupKindRefusal : OperationCanceledException
{
    public string Group { get; }
    public IReadOnlyList<string> Members { get; }

    public ProjectGroupKindRefusal(string group, IReadOnlyList<string> members)
        : base($"{group} can't be saved as a project group: VB6 would open {string.Join(", ", members)} as code modules, not as projects. "
               + "Remove them from the group, or save each project on its own. Nothing was written.")
    {
        Group = group;
        Members = members;
    }
}
