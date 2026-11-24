using HexIDE.Desktop;
using HexIDE.IDE;
using HexIDE.Runtime;

namespace HexIDE.Integration.Tests;

/// <summary>
/// A component registry holding what the Basic provider registers, as the IDE's does once the provider has started
/// (#5594). The registry itself starts empty.
/// </summary>
internal static class InBoxRegistry
{
    /// <summary>A new registry with the provider's classes and toolbox items, from the provider's own lists.</summary>
    internal static ComponentRegistry Create() => Fill(new ComponentRegistry());

    /// <summary>Registers the provider's classes and toolbox items in <paramref name="registry"/>, under its own identity.</summary>
    internal static ComponentRegistry Fill(ComponentRegistry registry)
    {
        foreach (var component in BasicProvider.Components)
            registry.Register(component, BasicProviderComposition.Origin);
        foreach (var (component, iconKey) in BasicProvider.Toolbox)
            registry.RegisterToolboxItem(component, iconKey, BasicProviderComposition.Origin);
        return registry;
    }
}
