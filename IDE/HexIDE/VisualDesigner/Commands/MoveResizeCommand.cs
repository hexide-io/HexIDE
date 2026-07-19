using System.Collections.Generic;
using System.Linq;
using Avalonia;
using HexIDE.Runtime.Components;

namespace HexIDE.VisualDesigner.Commands;

internal class MoveResizeCommand : IDesignerCommand
{
    private readonly List<(ComponentInstanceViewModel Vm, Rect Before, Rect After)> _entries;

    public string Description { get; }

    internal MoveResizeCommand(IReadOnlyList<(ComponentInstanceViewModel Vm, Rect Before, Rect After)> entries)
    {
        _entries = [.. entries];
        Description = BuildDescription(_entries);
    }

    public void Execute(FormEditViewModel vm)
    {
        foreach (var e in _entries)
        {
            e.Vm.Left = e.After.Left;
            e.Vm.Top = e.After.Top;
            e.Vm.Width = e.After.Width;
            e.Vm.Height = e.After.Height;
        }
    }

    public void Undo(FormEditViewModel vm)
    {
        foreach (var e in _entries)
        {
            e.Vm.Left = e.Before.Left;
            e.Vm.Top = e.Before.Top;
            e.Vm.Width = e.Before.Width;
            e.Vm.Height = e.Before.Height;
        }
    }

    private static string BuildDescription(List<(ComponentInstanceViewModel Vm, Rect Before, Rect After)> entries)
    {
        if (entries.Count == 0) return "Move";
        bool anyResize = entries.Any(e => e.Before.Width != e.After.Width || e.Before.Height != e.After.Height);
        bool anyMove = entries.Any(e => e.Before.Left != e.After.Left || e.Before.Top != e.After.Top);
        if (entries.Count == 1)
        {
            var target = entries[0].Vm;
            if (target.Instance.BaseClass is FormComponentClass) return "Resize Form";
            if (anyResize && !anyMove) return $"Resize: {target.Name}";
            if (anyMove && !anyResize) return $"Move: {target.Name}";
            return $"Move/Resize: {target.Name}";
        }
        return anyResize ? $"Move/Resize: {entries.Count} controls" : $"Move: {entries.Count} controls";
    }
}
