using System.Threading.Tasks;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

public interface IVb6ToolchainService
{
    bool IsAvailable { get; }
    string? Vb6ExePath { get; }
    Task<bool> MakeWithVb6Async(ProjectDefinition project);
    Task RunWithVb6Async(ProjectDefinition project);
}
