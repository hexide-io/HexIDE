using System.Threading.Tasks;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Sidecar;

public interface IUserSidecarService
{
    Task LoadAsync(ProjectDefinition project);
    Task SaveAsync(ProjectDefinition project);

    /// <summary>Writes every change still owed to any project's sidecar, for closing the IDE.</summary>
    void FlushAll();
}
