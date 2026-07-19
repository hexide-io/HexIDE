using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.Components;

namespace HexIDE.VisualDesigner;

internal static class DesignerClipboard
{
    internal record ClipboardEntry(
        IComponentClass BaseClass,
        IReadOnlyDictionary<PropertyClass, object?> Properties);

    private static IReadOnlyList<ClipboardEntry>? _contents;

    public static IReadOnlyList<ClipboardEntry>? Contents => _contents;

    public static void Set(IEnumerable<ComponentInstanceViewModel> components)
    {
        _contents = components
            .Select(vm => new ClipboardEntry(vm.Instance.BaseClass, vm.Instance.GetAllSetProperties()))
            .ToList();
    }

    public static void Clear() => _contents = null;
}
