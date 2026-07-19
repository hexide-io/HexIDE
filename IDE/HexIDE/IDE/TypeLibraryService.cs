using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.TypeLibrary;
using Serilog;

namespace HexIDE.IDE;

public class TypeLibraryService : ITypeLibraryService
{
    // Keyed by (Guid, Version) — immutable TypeLibInfo records, session lifetime.
    private readonly ConcurrentDictionary<(string Guid, string Version), TypeLibInfo?> _cache = new();

    public Task<TypeLibInfo?> GetTypeLibInfoAsync(
        VbReference reference,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return Task.FromResult<TypeLibInfo?>(null);

        var key = (reference.Guid, reference.Version);
        if (_cache.TryGetValue(key, out var cached))
            return Task.FromResult(cached);

        return string.IsNullOrEmpty(reference.LibPath)
            ? Task.Run(() => LoadByGuid(key), cancellationToken)
            : Task.Run(() => LoadByPath(key, reference.LibPath!), cancellationToken);
    }

    private TypeLibInfo? LoadByPath((string Guid, string Version) key, string libPath)
    {
        if (_cache.TryGetValue(key, out var cached)) return cached;

        if (!File.Exists(libPath))
        {
            Log.Debug("TypeLibraryService: library file not found: {Path}", libPath);
            _cache[key] = null;
            return null;
        }

        var info = TypeLibReader.Read(libPath);
        if (info == null)
            Log.Debug("TypeLibraryService: TypeLibReader returned null for {Path}", libPath);

        _cache[key] = info;
        return info;
    }

    private TypeLibInfo? LoadByGuid((string Guid, string Version) key)
    {
        if (_cache.TryGetValue(key, out var cached)) return cached;

        Log.Debug("TypeLibraryService: resolving by GUID {Guid} v{Version}", key.Guid, key.Version);
        var info = TypeLibReader.ReadByGuid(key.Guid, key.Version);
        if (info == null)
            Log.Debug("TypeLibraryService: registry lookup returned null for GUID {Guid}", key.Guid);

        _cache[key] = info;
        return info;
    }
}
