using HexIDE.Runtime.Making;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.ProjectElements;

internal static class ProjectTargets
{
    /// <summary>The target whose predefined #If constants are used; it does not select the run engine (#4821).</summary>
    internal static ProjectTarget Of(ProjectDefinition project)
    {
        // RDCore 0.2.1 registers ".rdproj" (#5404). VB6's Save As can also give a project an .rdproj name.
        if (project.AbsolutePath?.EndsWith(".rdproj", StringComparison.OrdinalIgnoreCase) == true &&
            (project.ContributedBy ?? Vb6ProjectMaker.Vb6ProjectTypeId) != Vb6ProjectMaker.Vb6ProjectTypeId)
            return ProjectTarget.RDCore;

        return ProjectEncodingRecords.ReadTarget(project.ExtensionTail);
    }
}
