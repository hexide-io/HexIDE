using System.Threading;
using System.Threading.Tasks;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.TypeLibrary;

namespace HexIDE.IDE;

public interface ITypeLibraryService
{
    /// Returns null on non-Windows, if the library file cannot be located, or if loading fails.
    Task<TypeLibInfo?> GetTypeLibInfoAsync(
        VbReference reference,
        CancellationToken cancellationToken = default);
}
