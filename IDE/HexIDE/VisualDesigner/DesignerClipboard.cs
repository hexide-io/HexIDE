using System.Collections.Generic;
using System.Linq;
using Avalonia;
using HexIDE.Runtime.Components;

namespace HexIDE.VisualDesigner;

internal static class DesignerClipboard
{
    /// <summary>
    /// A copied control, its contents, and where it was copied from.
    ///
    /// The container matters because copying is the one place a coordinate can silently change space. A
    /// control inside a Frame records a Frame-relative Left; paste it onto the form and that number becomes
    /// an absolute one, so the copy appears somewhere it was never put. Paste it back into the same Frame and
    /// the number is still correct.
    /// </summary>
    /// <param name="SourceContainer">
    /// The model component this was copied out of — matched by reference, since four sibling controls can
    /// share one name. Null when nothing had recorded a container, which is every control on a form the
    /// designer built.
    /// </param>
    /// <param name="SourceContainerOrigin">
    /// That container's accumulated canvas origin, captured at copy time rather than looked up at paste time:
    /// the clipboard outlives the form it was filled from, so by then the container may not exist to ask.
    /// </param>
    /// <param name="UnknownProperties">
    /// Its properties HexIDE does not model, with the companion records they cite. Not in <paramref name="Properties"/>,
    /// which is only what the property bag holds, so a paste used to leave them behind (#1453).
    /// </param>
    /// <param name="PreservedChildren">
    /// The controls inside it HexIDE does not model, kept as the text they were read as with the records they cite. They
    /// are no <see cref="ComponentInstance"/>, so <paramref name="Children"/> cannot hold them (#1453).
    /// </param>
    internal record ClipboardEntry(
        IComponentClass BaseClass,
        IReadOnlyDictionary<PropertyClass, object?> Properties,
        ComponentInstance? SourceContainer,
        Point SourceContainerOrigin,
        IReadOnlyList<ClipboardEntry> Children,
        IReadOnlyList<ComponentInstance.UnknownRawProperty> UnknownProperties,
        IReadOnlyList<ComponentInstance.PreservedSubtree> PreservedChildren);

    private static IReadOnlyList<ClipboardEntry>? _contents;

    public static IReadOnlyList<ClipboardEntry>? Contents => _contents;

    /// <summary>
    /// The <c>Object =</c> lines of the form the contents came from: the libraries its controls are declared from. A
    /// control HexIDE does not model names its class, not the OCX that holds it, so it can be pasted only into a form
    /// that declares them too.
    /// </summary>
    public static IReadOnlyList<string> SourceDeclarations { get; private set; } = [];

    /// <summary>The <c>Object =</c> lines among a form's header lines.</summary>
    public static IEnumerable<string> Declarations(IEnumerable<string> headerLines) =>
        headerLines.Select(line => line.Trim())
                   .Where(line => line.StartsWith("Object", System.StringComparison.OrdinalIgnoreCase));

    public static void Set(IEnumerable<ComponentInstanceViewModel> components)
    {
        var selection = components.ToList();
        var selected = new HashSet<ComponentInstance>(selection.Select(v => v.Instance));

        // Only the roots of the selection are captured at top level; a selected control inside a selected
        // container arrives as part of that container's subtree instead, so it is not copied twice.
        bool IsRoot(ComponentInstanceViewModel vm)
        {
            for (var container = vm.Instance.Container; container is not null; container = container.Container)
                if (selected.Contains(container))
                    return false;
            return true;
        }

        _contents = selection.Where(IsRoot).Select(Capture).ToList();
        SourceDeclarations = selection.FirstOrDefault()?.Owner.FormDefinition is { } form
            ? Declarations(form.HeaderLines).ToList()
            : [];
    }

    private static ClipboardEntry Capture(ComponentInstanceViewModel vm)
    {
        var children = new List<ClipboardEntry>();
        foreach (var child in vm.Instance.ContainedControls)
            if (vm.Owner.TryGetViewModel(child, out var childVm))
                children.Add(Capture(childVm));

        // A property snapshot rather than the instance: the copy is rebuilt from fresh instances at paste
        // time, which is also what stops a pasted container being handed the original's children by
        // reference.
        return new ClipboardEntry(
            vm.Instance.BaseClass,
            vm.Instance.GetAllSetProperties(),
            vm.Instance.Container,
            vm.ContainerBounds.Position,
            children,
            [.. vm.Instance.UnknownRawProperties],
            [.. vm.Instance.PreservedChildSubtrees]);
    }

    public static void Clear()
    {
        _contents = null;
        SourceDeclarations = [];
    }
}
