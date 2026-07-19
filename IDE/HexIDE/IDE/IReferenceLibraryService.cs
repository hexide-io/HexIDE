using System.Collections.Generic;

namespace HexIDE.IDE;

public record AvailableReference(string Guid, string Version, string Name, string? LibPath);

public interface IReferenceLibraryService
{
    IReadOnlyList<AvailableReference> GetAvailableReferences();
}
