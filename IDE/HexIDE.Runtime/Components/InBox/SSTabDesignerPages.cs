using System.Runtime.CompilerServices;
using Avalonia;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Serialization;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components.InBox;

/// <summary>Designer membership, kept by identity so renaming and undoing a deletion retain the page (#2301).</summary>
internal sealed class SSTabDesignerPages
{
    private static readonly ConditionalWeakTable<ComponentInstance, SSTabDesignerPages> Instances = new();
    private const double OffPage = 75000d / 15;
    private readonly ComponentInstance owner;
    private readonly List<List<ComponentInstance>> pages = [];
    private int showing;

    internal static SSTabDesignerPages For(ComponentInstance instance) => Instances.GetValue(instance, i => new(i));

    private SSTabDesignerPages(ComponentInstance instance)
    {
        owner = instance;
        var saved = instance.GetPropertyOrDefault(SSTabSavedState.Property);
        showing = saved?.Number("Tab", 0) ?? 0;
        for (var page = 0; page < (saved?.Number("Tabs", 3) ?? 3); page++)
        {
            var children = new List<ComponentInstance>();
            for (var index = 0; index < (saved?.Number($"Tab({page}).ControlCount", 0) ?? 0); index++)
            {
                var name = saved?.Value($"Tab({page}).Control({index})") as string;
                var matches = instance.ContainedControls.Where(c => string.Equals(c.GetPropertyOrDefault(NameProperty),
                    name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1 || PropertyText.ArrayIndexOf(matches[0]) is not null)
                    throw new NotImplementedException("SSTab array or unresolved saved control references are not measured");
                children.Add(matches[0]);
            }
            pages.Add(children);
        }
    }

    internal bool Select(Point point, Size size, IReadOnlyList<ComponentInstance> children)
    {
        var preview = (VBSSTab)SSTabComponentClass.Instance.InstantiateForDesigner(owner);
        preview.Measure(size);
        preview.Arrange(new Rect(size));
        if (preview.DesignerTabAt(point) is not { } next)
            return false;
        if (next == showing)
            return true;
        Synchronize(children);
        foreach (var child in pages[showing].Where(children.Contains))
            child.SetProperty(LeftProperty, child.GetPropertyOrDefault(LeftProperty) - OffPage);
        foreach (var child in pages[next].Where(children.Contains))
            child.SetProperty(LeftProperty, child.GetPropertyOrDefault(LeftProperty) + OffPage);
        var previous = showing;
        showing = next;
        Write(children, previous);
        return true;
    }

    internal void Synchronize(IReadOnlyList<ComponentInstance> children)
    {
        var onPage = children.Where(c => c.GetPropertyOrDefault(LeftProperty) + c.GetPropertyOrDefault(WidthProperty) >= 0
            && c.GetPropertyOrDefault(TopProperty) + c.GetPropertyOrDefault(HeightProperty) >= 0).ToList();
        var current = pages[showing].Where(onPage.Contains).ToList();
        current.AddRange(onPage.Where(c => !current.Contains(c)));
        var changed = !current.SequenceEqual(pages[showing].Where(children.Contains))
            || pages.Any(p => p.Any(c => !children.Contains(c)));
        var saved = owner.GetPropertyOrDefault(SSTabSavedState.Property);
        for (var page = 0; page < pages.Count; page++)
        {
            var live = pages[page].Where(children.Contains).ToList();
            for (var i = 0; i < live.Count; i++)
                changed |= !Equals(saved?.Value($"Tab({page}).Control({i})"), live[i].GetPropertyOrDefault(NameProperty));
        }
        if (!changed)
            return;
        // Retain deleted instances for undo; only live children are written.
        pages[showing].RemoveAll(c => children.Contains(c) && !onPage.Contains(c));
        foreach (var child in current)
        {
            if (!pages[showing].Contains(child))
                pages[showing].Add(child);
        }
        Write(children);
    }

    private void Write(IReadOnlyList<ComponentInstance> children, int? previous = null)
    {
        var properties = owner.GetPropertyOrDefault(SSTabSavedState.Property)?.CopyProperties()
            ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        void Set(string name, object value)
        {
            properties[name] = value;
            owner.UnknownRawProperties.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var writer = new VbFrmFormatSerializer(includeVersionHeader: false);
            if (name.EndsWith(".Enabled", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".ControlEnabled", StringComparison.OrdinalIgnoreCase))
                writer.WriteProperty(name, typeof(bool), (int)value == -1);
            else
                writer.WriteProperty(name, value.GetType(), value);
            owner.UnknownRawProperties.Add(new(name, [writer.GetOutput().TrimEnd('\r', '\n')]));
        }
        owner.UnknownRawProperties.RemoveAll(p => p.Name.StartsWith("Tab(", StringComparison.OrdinalIgnoreCase)
            && (p.Name.Contains(".Control(", StringComparison.OrdinalIgnoreCase)
                || p.Name.EndsWith(".ControlCount", StringComparison.OrdinalIgnoreCase)));
        foreach (var key in properties.Keys.Where(k => k.StartsWith("Tab(", StringComparison.OrdinalIgnoreCase)
                     && (k.Contains(".Control(", StringComparison.OrdinalIgnoreCase)
                         || k.EndsWith(".ControlCount", StringComparison.OrdinalIgnoreCase))).ToArray())
            properties.Remove(key);
        Set("Tab", showing);
        if (showing == 0)
            owner.UnknownRawProperties.RemoveAll(p => p.Name.Equals("Tab", StringComparison.OrdinalIgnoreCase));
        for (var page = 0; page < pages.Count; page++)
        {
            if (!properties.ContainsKey($"TabCaption({page})"))
                Set($"TabCaption({page})", $"Tab {page}");
            if (previous == page || showing == page)
                Set($"Tab({page}).ControlEnabled", showing == page ? -1 : 0);
            var live = pages[page].Where(children.Contains).ToList();
            for (var i = 0; i < live.Count; i++)
            {
                Set($"Tab({page}).Control({i})", live[i].GetPropertyOrDefault(NameProperty)!);
                Set($"Tab({page}).Control({i}).Enabled", 0);
            }
            Set($"Tab({page}).ControlCount", live.Count);
        }
        owner.SetProperty(SSTabSavedState.Property, new SSTabSavedState(properties));
    }
}
