using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace HexIDE.Runtime.AvaloniaInterop;

// Opaque VB Longs, scoped to a run. Retired entries remain known so Draw can distinguish them from native HDCs (#4849).
internal sealed class DrawingHandles
{
    private static readonly ConditionalWeakTable<Canvas, DrawingHandles> tables = new();

    internal static void Register(Canvas canvas, DrawingHandles handles) => tables.Add(canvas, handles);
    internal static DrawingHandles For(Canvas canvas) => tables.GetValue(canvas, _ => new DrawingHandles());

    private readonly Dictionary<int, RuntimeDrawing?> surfaces = new();
    private int next;

    internal int Issue(RuntimeDrawing drawing)
    {
        var handle = checked(++next);
        surfaces.Add(handle, drawing);
        return handle;
    }

    internal void Retire(int handle)
    {
        if (handle != 0) surfaces[handle] = null;
    }

    internal RuntimeDrawing? Find(int handle)
    {
        if (handle == 0) return null;
        if (!surfaces.TryGetValue(handle, out var drawing))
            throw new NotImplementedException("ListImage.Draw into an hDC that this program's forms and pictures didn't give out is not "
                + "implemented: HexIDE can't draw into a device context from outside the program (#4849)");
        return drawing?.HasHandle(handle) == true ? drawing : null;
    }
}
