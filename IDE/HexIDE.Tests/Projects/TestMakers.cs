using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.Making;

namespace HexIDE.Tests.Projects;

/// <summary>
/// The Basic provider's maker for VB6's projects, registered as the composition registers it (#5621). A
/// <see cref="ProjectService"/> a test makes has VB6's type and no maker, so Make refuses until one is registered.
/// </summary>
internal static class TestMakers
{
    /// <summary>The provider's identity, as HexIDE.Desktop composes it, which its grant names.</summary>
    internal static AddinOrigin BasicProvider { get; } = new(RegistrationGrants.BasicProvider, "HexIDE.Basic") { IsComposed = true };

    /// <summary>
    /// Registers the provider's maker on <paramref name="service"/>, finding the standalone runner under
    /// <paramref name="runtimeDirectory"/>, with <paramref name="toolchain"/> as VB6's toolchain where a test gives one.
    /// </summary>
    internal static ProjectService WithMaker(this ProjectService service, string runtimeDirectory, IProjectToolchain? toolchain = null)
    {
        service.Makers.Register(new Vb6ProjectMaker(toolchain) { RuntimeDirectory = runtimeDirectory }, BasicProvider);
        return service;
    }

    /// <summary>A registry of project types holding VB6's alone, with the provider's maker for it.</summary>
    internal static ContributedProjects Vb6WithMaker(string runtimeDirectory, IProjectToolchain? toolchain = null)
    {
        var types = HexIDE.Tests.Infrastructure.Vb6Types.Alone();
        types.Makers.Register(new Vb6ProjectMaker(toolchain) { RuntimeDirectory = runtimeDirectory }, BasicProvider);
        return types;
    }
}
