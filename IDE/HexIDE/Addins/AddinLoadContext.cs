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
/// Collectible so a later phase can unload a revoked or updated add-in.
///
/// <para>
/// <b>Which copy wins.</b> An explicit, enumerated set — see <see cref="MustShareWithHost"/> — always
/// resolves to the host's assembly; everything else prefers the add-in's own verified copy. That split is
/// the whole point of the context: an add-in may bring its own container, its own serializer, its own
/// logging, at its own versions, and none of it collides with the host's.
/// </para>
///
/// <para>
/// This replaces a policy that deferred to <c>Default.Assemblies</c> by simple name — "if the host has
/// something called this, the host wins". That was wrong in two ways, neither of which shows up with a
/// single first-party add-in and both of which become a compatibility contract the moment third-party
/// packages exist:
/// </para>
/// <list type="number">
/// <item><b>Version-blind.</b> Host with v8 loaded silently won over a package shipping v9, so an add-in
///   could not rely on its own dependency versions and found out as a <c>MissingMethodException</c> at
///   the call site rather than an error at load.</item>
/// <item><b>Order-dependent.</b> <c>Default.Assemblies</c> is what happens to be loaded <em>so far</em>.
///   A host assembly loaded lazily, after an add-in had already resolved its own copy, left two
///   assemblies of one simple name live in different contexts — and any type crossing between them fails
///   to cast, with a message that names the same type twice.</item>
/// </list>
///
/// <para>
/// Fixed now, while exactly one first-party add-in exists and nothing depends on the old behaviour.
/// Afterwards it is not really changeable: which copy wins is observable to every third-party package.
/// </para>
/// </summary>
internal sealed class AddinLoadContext : AssemblyLoadContext
{
    // Simple assembly name → absolute path, drawn from the verified manifest (managed assemblies only).
    private readonly Dictionary<string, string> _managedByName;

    private readonly List<string> _shadowedByHost = [];

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

    /// <summary>Assemblies the package shipped that were ignored in favour of the host's copy, in the
    /// order they were first requested.</summary>
    ///
    /// <remarks>
    /// Shadowing is correct — those types have to unify — but it is silent, and the symptom it produces
    /// (a method that exists on the author's machine and not at runtime) gives no hint of the cause.
    /// Recording it turns that into something the registry can name in a log line or the add-in details
    /// pane. It is a diagnostic, never a failure: a package that carries a redundant copy of a shared
    /// assembly still loads and still works.
    /// </remarks>
    public IReadOnlyList<string> ShadowedByHost => _shadowedByHost;

    /// <summary>
    /// True for assemblies whose types cross the add-in boundary, and which must therefore be the same
    /// assembly on both sides. Everything not named here is the add-in's own business.
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    /// <c>HexIDE.*</c> is the contract itself — <c>IAddin</c>, <c>IHexIdeHost</c> and every type they
    /// carry. A second copy would make an add-in unable to implement the interface the host is looking for.
    /// </para>
    /// <para>
    /// <b>Avalonia is here even though it appears in no contract signature</b>, and that is the
    /// non-obvious entry. <c>IToolWindowContributor.Register(…, Func&lt;object&gt; factory)</c> and
    /// <c>IOptionsPageContributor.RegisterOptionsPage(Func&lt;object&gt;)</c> are deliberately typed as
    /// <c>object</c> so the contract stays UI-framework-agnostic — but the add-in hands back a control
    /// and the <em>host</em> casts it. The dependency is invisible to the compiler and mandatory at
    /// runtime, so a package resolving its own Avalonia would fail that cast rather than fail to build.
    /// </para>
    /// <para>
    /// The framework assemblies are shared for the ordinary reason: they are the runtime, and a private
    /// copy of the BCL is never what anyone wants. That does mean a package shipping a newer
    /// <c>System.Text.Json</c> than the host gets the host's — deliberate, and the reason
    /// <see cref="ShadowedByHost"/> records it.
    /// </para>
    /// </remarks>
    internal static bool MustShareWithHost(string name) =>
        name.Equals("HexIDE", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("HexIDE.", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)
        || name.Equals("System", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
        || name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)
        || name.Equals("netstandard", StringComparison.OrdinalIgnoreCase)
        || name.Equals("WindowsBase", StringComparison.OrdinalIgnoreCase);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not { } name)
            return null;

        // Shared: defer to the default context. Returning null lets the runtime resolve it there, which
        // is what keeps IAddin, Control and the BCL a single identity on both sides of the boundary.
        if (MustShareWithHost(name))
        {
            if (_managedByName.ContainsKey(name) && !_shadowedByHost.Contains(name, StringComparer.OrdinalIgnoreCase))
                _shadowedByHost.Add(name);
            return null;
        }

        // Everything else: the add-in's own verified copy if it shipped one, at its own version,
        // independent of what the host happens to have loaded. Not in the package ⇒ default resolution,
        // so a package may still rely on something the host provides without carrying it.
        return _managedByName.TryGetValue(name, out var path) ? LoadFromAssemblyPath(path) : null;
    }

    // No LoadUnmanagedDll override is needed: the manifest-completeness check guarantees the package
    // directory contains no unlisted files, so the runtime's default native probing of that directory
    // can only ever find a signed, hash-verified native library.
}
