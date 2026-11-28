using System.Collections.Generic;
using System.Linq;
using Avalonia;
using HexIDE.Runtime.Components;

namespace HexIDE.VisualDesigner.Commands;

internal class MoveResizeCommand : IDesignerCommand
{
    internal readonly record struct Geometry(Rect Bounds, (double X1, double Y1, double X2, double Y2)? Endpoints)
    {
        internal static Geometry Capture(ComponentInstanceViewModel vm)
        {
            if (!vm.IsLine)
                return new Geometry(new Rect(vm.Left, vm.Top, vm.Width, vm.Height), null);

            // The selection box rounds to pixels. Keep the Line's endpoints in its container's scale.
            var instance = vm.Instance;
            return new Geometry(default, (instance[VBProperties.X1Property], instance[VBProperties.Y1Property],
                instance[VBProperties.X2Property], instance[VBProperties.Y2Property]));
        }

        internal void Apply(ComponentInstanceViewModel vm)
        {
            if (Endpoints is { } line)
            {
                RestoreEndpoint(vm.Instance, VBProperties.X1Property, line.X1);
                RestoreEndpoint(vm.Instance, VBProperties.Y1Property, line.Y1);
                RestoreEndpoint(vm.Instance, VBProperties.X2Property, line.X2);
                RestoreEndpoint(vm.Instance, VBProperties.Y2Property, line.Y2);
                return;
            }

            vm.Left = Bounds.Left;
            vm.Top = Bounds.Top;
            vm.Width = Bounds.Width;
            vm.Height = Bounds.Height;
        }

        private static void RestoreEndpoint(ComponentInstance instance, PropertyClass<double> property, double value)
        {
            // Writing an unchanged endpoint could make an omitted coordinate explicit on the next save.
            if (!instance[property].Equals(value))
                instance.SetProperty(property, value);
        }
    }

    internal readonly record struct Entry(ComponentInstanceViewModel Vm, Geometry Before, Geometry After);

    private readonly List<Entry> _entries;

    public string Description { get; }
    public DesignerUndoName DisplayName { get; }

    internal MoveResizeCommand(IReadOnlyList<(ComponentInstanceViewModel Vm, Rect Before, Rect After)> entries)
        : this([.. entries.Select(e => new Entry(e.Vm, new Geometry(e.Before, null), new Geometry(e.After, null)))])
    {
    }

    internal MoveResizeCommand(IReadOnlyList<Entry> entries)
    {
        _entries = [.. entries];
        (Description, DisplayName) = BuildNames(_entries);
    }

    public void Execute(FormEditViewModel vm)
    {
        foreach (var e in _entries)
            e.After.Apply(e.Vm);
    }

    public void Undo(FormEditViewModel vm)
    {
        foreach (var e in _entries)
            e.Before.Apply(e.Vm);
    }

    private static (string Description, DesignerUndoName DisplayName) BuildNames(List<Entry> entries)
    {
        if (entries.Count == 0) return ("Move", DesignerUndoName.Localized("Str.Designer.Undo.Move"));
        // A Line's entry holds its endpoints and no box, so it is a move, never a resize (#5797).
        bool anyResize = entries.Any(e => e.Before.Bounds.Width != e.After.Bounds.Width || e.Before.Bounds.Height != e.After.Bounds.Height);
        bool anyMove = entries.Any(e => e.Before.Bounds.Left != e.After.Bounds.Left || e.Before.Bounds.Top != e.After.Bounds.Top
            || !Nullable.Equals(e.Before.Endpoints, e.After.Endpoints));
        if (entries.Count == 1)
        {
            var target = entries[0].Vm;
            if (target.Instance.BaseClass.Role == ComponentRole.Form)
                return ("Resize Form", DesignerUndoName.Localized("Str.Designer.Undo.ResizeForm"));
            var name = target.Name;
            if (anyResize && !anyMove)
                return ($"Resize: {name}", DesignerUndoName.Localized("Str.Designer.Undo.ResizeControl", name));
            if (anyMove && !anyResize)
                return ($"Move: {name}", DesignerUndoName.Localized("Str.Designer.Undo.MoveControl", name));
            return ($"Move/Resize: {name}", DesignerUndoName.Localized("Str.Designer.Undo.MoveResizeControl", name));
        }
        return anyResize
            ? ($"Move/Resize: {entries.Count} controls", DesignerUndoName.Localized("Str.Designer.Undo.MoveResizeControls", entries.Count))
            : ($"Move: {entries.Count} controls", DesignerUndoName.Localized("Str.Designer.Undo.MoveControls", entries.Count));
    }
}
