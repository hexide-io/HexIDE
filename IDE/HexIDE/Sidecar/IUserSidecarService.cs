using System.Threading.Tasks;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Sidecar;

public interface IUserSidecarService
{
    Task LoadAsync(ProjectDefinition project);
    Task SaveAsync(ProjectDefinition project);
}
