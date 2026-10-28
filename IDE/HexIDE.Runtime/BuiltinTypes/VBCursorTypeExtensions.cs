using System.Runtime.CompilerServices;
using Avalonia.Input;

namespace HexIDE.Runtime.BuiltinTypes;

public static class VBCursorTypeExtensions
{
    public static StandardCursorType ToStandardCursorType(this VBCursorType cursor)
    {
        return cursor switch
        {
            VBCursorType.Default => StandardCursorType.Arrow,
            VBCursorType.Arrow => StandardCursorType.Arrow,
            VBCursorType.Crosshair => StandardCursorType.Cross,
            VBCursorType.IBeam => StandardCursorType.Ibeam,
            // What vb6.exe draws on Windows 10 (#120): Icon is the plain arrow, since IDC_ICON is obsolete there and
            // LoadCursor gives nothing for it, and Size is the four-way size pointer, as Size All is.
            VBCursorType.Icon => StandardCursorType.Arrow,
            VBCursorType.Size => StandardCursorType.SizeAll,
            VBCursorType.SizeNESW => StandardCursorType.BottomLeftCorner,
            VBCursorType.SizeNS => StandardCursorType.SizeNorthSouth,
            VBCursorType.SizeNWSE => StandardCursorType.BottomRightCorner,
            VBCursorType.SizeWE => StandardCursorType.SizeWestEast,
            VBCursorType.UpArrow => StandardCursorType.UpArrow,
            VBCursorType.Hourglass => StandardCursorType.Wait,
            VBCursorType.NoDrop => StandardCursorType.No,
            VBCursorType.ArrowHourglass => StandardCursorType.AppStarting,
            VBCursorType.ArrowQuestion => StandardCursorType.Help,
            VBCursorType.SizeAll => StandardCursorType.SizeAll,
            VBCursorType.Custom => StandardCursorType.Arrow,
            _ => StandardCursorType.Arrow,
        };
    }

    public static Cursor ToCursor(this VBCursorType cursor)
    {
        var made = new Cursor(cursor.ToStandardCursorType());
        Made.AddOrUpdate(made, new StrongBox<VBCursorType>(cursor));
        return made;
    }

    /// <summary>
    /// The MousePointer <paramref name="cursor"/> was made from by <see cref="ToCursor"/>, or null for one the runtime did
    /// not make, such as a TextBox's I-beam from its theme. Avalonia's <see cref="Cursor"/> does not say what it shows, so
    /// the automation surface reads a running program's pointer from here (#2453).
    /// </summary>
    public static VBCursorType? PointerOf(Cursor? cursor) =>
        cursor is not null && Made.TryGetValue(cursor, out var pointer) ? pointer.Value : null;

    // Recorded, not cached: each call still makes its own Cursor, as callers compare them by reference.
    private static readonly ConditionalWeakTable<Cursor, StrongBox<VBCursorType>> Made = new();
}
