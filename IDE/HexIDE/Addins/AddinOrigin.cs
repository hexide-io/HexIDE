using System.Reflection;
using System.Runtime.Loader;

namespace HexIDE.Addins;

/// <summary>The add-in a contribution came from: its manifest id, which stays the same, and its title, for display.</summary>
internal readonly record struct AddinOrigin(string Id, string Title, bool IsFirstParty = false, bool IsDeveloperUnsigned = false)
{
    /// <summary>The namespaces the add-in's verified publisher record issues, or null when it carries none (#4258).</summary>
    public string[]? PublisherNamespaces { get; init; }

    /// <summary>
    /// Made by HexIDE's own startup code for something compiled into it, never read from a package: the Basic provider,
    /// which HexIDE.Desktop composes until phase 3 of #3411 loads it as an add-in (#5396). Nothing that reads a manifest
    /// sets it, so an add-in that names itself after the provider is not the provider.
    /// </summary>
    public bool IsComposed { get; init; }

    /// <summary>
    /// The add-in an assembly belongs to, by the load context the registry loaded it into, whose manifest names it. An
    /// assembly in no add-in's context, the host's own or a test's, is named by its simple name.
    /// </summary>
    /// <remarks>
    /// Not by the assembly's file: an add-in's <see cref="AddinInfo.AssemblyPath"/> is its package's folder, and two
    /// add-ins can ship assemblies of one name, or register through one shared helper.
    /// </remarks>
    public static AddinOrigin Of(Assembly assembly, IAddinRegistry registry)
    {
        if (AssemblyLoadContext.GetLoadContext(assembly) is AddinLoadContext { AddinId: { Length: > 0 } id })
        {
            var info = registry.GetAll().FirstOrDefault(addin => string.Equals(addin.Id, id, StringComparison.OrdinalIgnoreCase));
            var title = info?.Title;
            return new AddinOrigin(id, string.IsNullOrWhiteSpace(title) ? id : title) { PublisherNamespaces = info?.PublisherNamespaces };
        }

        var name = assembly.GetName().Name ?? "An add-in";
        return new AddinOrigin(name, name);
    }
}
