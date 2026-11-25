using System.Runtime.CompilerServices;
using HexIDE.Addins;
using HexIDE.Projects;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests;

/// <summary>
/// VB6's project type as the Basic provider registers it (#5625): through the registry, under the provider's composed origin,
/// whose grant gives the type VB6's identity. A test that registers VB6's type does it here, so it registers it as the
/// product does.
/// </summary>
internal static class Vb6Types
{
    /// <summary>Registers VB6's type with <paramref name="types"/>, reading through <paramref name="format"/>, or VB6's own.</summary>
    internal static IDisposable Register(ContributedProjects types, HexIDE.IDE.IProjectFileFormat? format = null) =>
        types.Register(Vb6ProjectFileFormat.ProjectType(format), HexIDE.Desktop.BasicProviderComposition.Origin);

    /// <summary>A registry holding VB6's type alone.</summary>
    internal static ContributedProjects Alone()
    {
        var types = new ContributedProjects();
        Register(types);
        return types;
    }

    // A ProjectService a test makes without project types reads with VB6's, as the IDE does once the provider registers.
    [ModuleInitializer]
    internal static void UseVb6ByDefault() => ProjectService.DefaultTypes = Alone;
}
