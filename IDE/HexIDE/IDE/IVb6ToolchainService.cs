using System.Threading.Tasks;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

/// <summary>
/// Make with VB6 and Run > Start with VB6, as a user meets them: the dialogs, the trust prompt, the compiler's diagnostics
/// in the editor, and starting what was made. The build itself is the project type's toolchain, which its maker brings
/// (#5621).
/// </summary>
public interface IVb6ToolchainService
{
    /// <summary>Whether <paramref name="project"/>'s type has a toolchain, and its compiler is installed here.</summary>
    bool IsAvailableFor(ProjectDefinition? project);

    Task<bool> MakeWithVb6Async(ProjectDefinition project);
    Task RunWithVb6Async(ProjectDefinition project);
}
