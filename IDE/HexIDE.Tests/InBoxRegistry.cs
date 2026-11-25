using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Runtime;

namespace HexIDE.Tests;

/// <summary>
/// A component registry holding what the Basic provider registers, as the IDE's does once the provider has started
/// (#5594). The registry itself starts empty.
/// </summary>
internal static class InBoxRegistry
{
    /// <summary>The provider's identity, as HexIDE.Desktop composes it.</summary>
    internal static readonly AddinOrigin Provider = new(RegistrationGrants.BasicProvider, "HexIDE.Basic") { IsComposed = true };

    /// <summary>A new registry with the provider's classes and toolbox items, from the provider's own lists.</summary>
    internal static ComponentRegistry Create()
    {
        var registry = new ComponentRegistry();
        foreach (var component in BasicProvider.Components)
            registry.Register(component, Provider);
        foreach (var (component, iconKey) in BasicProvider.Toolbox)
            registry.RegisterToolboxItem(component, iconKey, Provider);
        foreach (var library in BasicProvider.Libraries)
            registry.RegisterLibrary(library, () => BasicProvider.ControlsOf(library), Provider);
        return registry;
    }

    /// <summary>
    /// A language registry holding the provider's editing helpers, as the IDE's does once the provider has started (#5403).
    /// </summary>
    internal static AddinLanguageEditingService LanguageEditing()
    {
        var editing = new AddinLanguageEditingService();
        editing.Register(BasicProvider.LanguageEditing, Provider);
        return editing;
    }
}
