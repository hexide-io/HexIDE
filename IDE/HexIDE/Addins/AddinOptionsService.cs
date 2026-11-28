using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HexIDE.Addins;

/// <summary>
/// Collects add-in-contributed Options pages, keyed by the contributing assembly's path
/// (captured from the add-in that calls <see cref="RegisterOptionsPage"/>). The Options dialog
/// looks a page up by the add-in's <see cref="AddinInfo.AssemblyPath"/>.
/// </summary>
public sealed class AddinOptionsService : IOptionsPageContributor
{
    private readonly List<Registration> _pages = [];
    private readonly List<(string Owner, bool Composed, string Name, HexIDE.Lsp.AddinServerConnection Connection)> _servers = [];

    // A composed owner's servers, the Basic provider's, are listed on no package's page, whatever id the package names (#5597).
    internal void TrackServer(string owner, string name, HexIDE.Lsp.AddinServerConnection connection, bool composed = false)
    {
        // A server taken out (its add-in disabled or its consent revoked) leaves the list, so its page shows only live servers.
        _servers.RemoveAll(server => server.Connection.IsTakenOut);
        _servers.Add((owner, composed, name, connection));
    }

    internal IReadOnlyList<(string Name, HexIDE.Lsp.AddinServerConnection Connection)> ServersFor(string owner) =>
        _servers.Where(server => !server.Connection.IsTakenOut && !server.Composed
            && string.Equals(server.Owner, owner, StringComparison.OrdinalIgnoreCase))
            .Select(server => (server.Name, server.Connection)).ToList();


    // NoInlining keeps RegisterOptionsPage a real stack frame so GetCallingAssembly resolves to
    // the add-in that called it (not this assembly).
    [MethodImpl(MethodImplOptions.NoInlining)]
    public IOptionsPageHandle RegisterOptionsPage(Func<object> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var path = Normalize(Assembly.GetCallingAssembly().Location);
        var registration = new Registration(path, factory);
        _pages.Add(registration);
        var handle = new Handle(() => _pages.Remove(registration));
        AddinInitializationScope.Record(handle.Dispose);
        return handle;
    }

    /// <summary>The contributed control factory for the add-in at <paramref name="addinAssemblyPath"/>, or null.</summary>
    public Func<object>? GetFactoryFor(string addinAssemblyPath)
    {
        var norm = Normalize(addinAssemblyPath);
        return _pages.FirstOrDefault(p => string.Equals(p.Path, norm, StringComparison.OrdinalIgnoreCase))?.Factory;
    }

    private static string Normalize(string path)
    {
        try { return string.IsNullOrEmpty(path) ? path : Path.GetFullPath(path); }
        catch { return path; }
    }

    private sealed record Registration(string Path, Func<object> Factory);

    private sealed class Handle(Action onDispose) : IOptionsPageHandle
    {
        private Action? _onDispose = onDispose;
        public void Dispose() { _onDispose?.Invoke(); _onDispose = null; }
    }
}
