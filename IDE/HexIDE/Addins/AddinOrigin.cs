using System.Reflection;
using System.Runtime.Loader;

namespace HexIDE.Addins;

/// <summary>The add-in a contribution came from: its manifest id, which stays the same, and its title, for display.</summary>
internal readonly record struct AddinOrigin(string Id, string Title)
{
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
            var title = registry.GetAll().FirstOrDefault(addin => string.Equals(addin.Id, id, StringComparison.OrdinalIgnoreCase))?.Title;
            return new AddinOrigin(id, string.IsNullOrWhiteSpace(title) ? id : title);
        }

        var name = assembly.GetName().Name ?? "An add-in";
        return new AddinOrigin(name, name);
    }
}
