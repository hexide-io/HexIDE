using System;
using System.Collections.Generic;
using HexIDE.Lsp;
using HexIDE.Runtime.TypeLibrary;

namespace HexIDE.IDE;

/// <summary>
/// Referenced libraries' own names, read from their type libraries: by registry GUID and version, else from a local file.
/// Once a session per library, and synchronously, so the first artefact written for a project already has them.
/// </summary>
/// <remarks>
/// The workspace artefact service hands this to a provider only after consent; see <see cref="IWorkspaceLibraryNames"/>.
/// A path that isn't a local file is never passed on, so a project naming a share can't make HexIDE contact it.
/// </remarks>
internal sealed class TypeLibraryNames : IWorkspaceLibraryNames
{
    /// <summary>Reads a type library's own name: GUID, version and a local path in, name or null out.</summary>
    /// <remarks>Swapped by tests.</remarks>
    internal Func<string, string, string?, string?> Read { get; set; } = TypeLibReader.ReadName;
    private readonly Dictionary<(Guid? Guid, string Version, string? Path), string?> _names = [];

    public string? NameOf(WorkspaceProjectReference reference)
    {
        var path = TypeLibReader.IsLocalPath(reference.HostPath) ? reference.HostPath : null;
        if (reference.Guid is null && path is null) return null;
        var key = (reference.Guid, $"{reference.Major ?? 0}.{reference.Minor ?? 0}", path);
        lock (_names)
        {
            if (!_names.TryGetValue(key, out var name))
                _names[key] = name = Read(reference.Guid?.ToString("B") ?? "", key.Item2, path);
            return name;
        }
    }
}
