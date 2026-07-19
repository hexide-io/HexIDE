using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using HexIDE.Addins;

namespace HexIDE.Addins;

/// <summary>
/// A collectible load context for one add-in package. Private dependencies resolve <b>only</b> from
/// the package's <em>verified manifest file list</em> — never from a <c>deps.json</c> or by probing the
/// folder — so a package can only ever load assemblies whose bytes were signed and hash-checked.
/// Assemblies the host already owns (HexIDE.Core, Avalonia, the BCL) fall through to the default context
/// so shared types — <c>IAddin</c>, <c>Control</c>, … — keep a single identity. Collectible so a later
/// phase can unload a revoked or updated add-in.
///
/// Combined with <see cref="PackageVerification"/>'s completeness check (a package may contain no
/// unlisted payload files), this means there is no path — managed or native, via <c>deps.json</c> or
/// otherwise — by which unverified code in the package directory can be loaded.
/// </summary>
internal sealed class AddinLoadContext : AssemblyLoadContext
{
    // Simple assembly name → absolute path, drawn from the verified manifest (managed assemblies only).
    private readonly Dictionary<string, string> _managedByName;

    public AddinLoadContext(string packageDir, AddinManifest manifest)
        : base(name: $"Addin:{manifest.Id}", isCollectible: true)
    {
        _managedByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in manifest.Files)
        {
            var ext = Path.GetExtension(f.Path);
            if (!ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                continue;
            // Last-writer-wins on duplicate simple names is acceptable; packages are flat in practice.
            _managedByName[Path.GetFileNameWithoutExtension(f.Path)] =
                Path.GetFullPath(Path.Combine(packageDir, f.Path));
        }
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Defer host/framework assemblies to the default context so types stay shared. Returning null
        // lets the runtime fall back to default resolution.
        if (assemblyName.Name is not { } name)
            return null;

        foreach (var loaded in Default.Assemblies)
            if (string.Equals(loaded.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                return null;

        // Otherwise: only an assembly the signed manifest lists may load from the package.
        return _managedByName.TryGetValue(name, out var path) ? LoadFromAssemblyPath(path) : null;
    }

    // No LoadUnmanagedDll override is needed: the manifest-completeness check guarantees the package
    // directory contains no unlisted files, so the runtime's default native probing of that directory
    // can only ever find a signed, hash-verified native library.
}
