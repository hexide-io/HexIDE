using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

// #988: docs/vb6-fidelity-oracle/the-drawing-methods-line-pset-circle-cls-and-point.md and the coordinate-system page.
// State belongs to the running object, not the designer. AutoRedraw's repaint/event semantics remain #991.
internal sealed class RuntimeDrawing
{
    private static readonly ConditionalWeakTable<Control, RuntimeDrawing> drawings = new();
    private readonly Control owner;
    private readonly Canvas host;
    private int currentX, currentY;
    private int drawWidth, drawStyle, drawMode, fillStyle;
    private VBColor fillColor;
    private bool autoRedraw, fontTransparent;
    private int[] pixels = [];
    private int width, height;
    private WriteableBitmap? bitmap;
    private bool dirty;

    private RuntimeDrawing(Control owner, Canvas host, ComponentInstance component)
    {
        this.owner = owner;
        this.host = host;
        drawWidth = VBLoader.RawNumber(component, "DrawWidth") ?? 1;
        drawStyle = VBLoader.RawNumber(component, "DrawStyle") ?? 0;
        drawMode = VBLoader.RawNumber(component, "DrawMode") ?? 13;
        fillStyle = VBLoader.RawNumber(component, "FillStyle") ?? (int)component.GetPropertyOrDefault(VBProperties.FillStyleProperty);
        fillColor = SavedFillColor(component);
        autoRedraw = VBLoader.RawDouble(component, "AutoRedraw") is { } savedAuto
            ? savedAuto != 0 : component.GetPropertyOrDefault(VBProperties.AutoRedrawProperty);
        fontTransparent = (VBLoader.RawDouble(component, "FontTransparent") ?? -1) != 0;
        drawings.Add(owner, this);
        drawings.Add(host, this);
        owner.SizeChanged += (_, _) => { if (pixels.Length != 0) Publish(); };
        host.DetachedFromVisualTree += (_, _) =>
        {
            host.Background = Brushes.Transparent;
            bitmap?.Dispose();
            bitmap = null;
        };
        host.AttachedToVisualTree += (_, _) => { if (pixels.Length != 0) Publish(); };
    }

    private static VBColor SavedFillColor(ComponentInstance component)
    {
        foreach (var property in component.UnknownRawProperties)
        {
            if (!property.Name.Equals("FillColor", StringComparison.OrdinalIgnoreCase) || property.Lines.Count == 0) continue;
            var line = property.Lines[0];
            if (VBColor.TryParse(line[(line.IndexOf('=') + 1)..].Trim(), out var colour)) return colour;
            if (VBLoader.RawDouble(component, "FillColor") is { } saved && double.IsFinite(saved)
                && saved == Math.Truncate(saved) && saved >= int.MinValue && saved <= int.MaxValue
                && VBColor.TryParse($"&H{unchecked((uint)(long)saved):X8}&", out var numberColour))
                return numberColour;
            throw Refused("Saved FillColor without a representable colour");
        }
        return component.GetPropertyOrDefault(VBProperties.FillColorProperty);
    }

    internal static void Initialize(Control owner, Canvas host, ComponentInstance component) =>
        _ = new RuntimeDrawing(owner, host, component);

    internal static RuntimeDrawing? Of(Control owner) => drawings.TryGetValue(owner, out var drawing) ? drawing : null;

    private RuntimeScale Scale => RuntimeScale.Of(owner)!;

    private float ReadPosition(int twips, bool vertical) => (float)(Scale.Origin(vertical) + twips / Scale.TwipsPerUnit(vertical));

    // Draw coordinates are whole twips first. Point deliberately skips that quantisation.
    private int Twips(Vb6Value value, bool vertical, bool step = false, int from = 0)
    {
        var units = Convert.ToSingle(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Single, null).Value);
        return Whole((units - (step ? 0 : Scale.Origin(vertical))) * Scale.TwipsPerUnit(vertical) + (step ? from : 0));
    }

    private static int Whole(double number)
    {
        var rounded = Math.Round(number, MidpointRounding.ToEven);
        if (!double.IsFinite(rounded) || rounded < int.MinValue || rounded > int.MaxValue)
            throw new VBRunTimeException(VBStandardError.Overflow);
        return (int)rounded;
    }

    internal static bool TryGet(Control owner, string name, out Vb6Value value)
    {
        value = default;
        if (Of(owner) is not { } drawing) return false;
        switch (name.ToUpperInvariant())
        {
            case "CURRENTX": value = new Vb6Value(drawing.ReadPosition(drawing.currentX, false)); return true;
            case "CURRENTY": value = new Vb6Value(drawing.ReadPosition(drawing.currentY, true)); return true;
            case "DRAWWIDTH": value = new Vb6Value(drawing.drawWidth); return true;
            case "DRAWSTYLE": value = new Vb6Value(drawing.drawStyle); return true;
            case "DRAWMODE": value = new Vb6Value(drawing.drawMode); return true;
            case "FILLSTYLE": value = new Vb6Value(drawing.fillStyle); return true;
            case "FILLCOLOR": value = new Vb6Value((long)drawing.fillColor.ToOle()); return true;
            case "AUTOREDRAW": value = new Vb6Value(drawing.autoRedraw); return true;
            case "FONTTRANSPARENT": value = new Vb6Value(drawing.fontTransparent); return true;
            default: return false;
        }
    }

    internal static bool TrySet(Control owner, string name, Vb6Value value)
    {
        if (Of(owner) is not { } drawing) return false;
        var member = name.ToUpperInvariant();
        switch (member)
        {
            case "CURRENTX": drawing.currentX = drawing.Twips(value, false); return true;
            case "CURRENTY": drawing.currentY = drawing.Twips(value, true); return true;
            case "FILLCOLOR": drawing.fillColor = VBColor.FromOle(AvaloniaInteroperability.OleColor(value)); return true;
            case "AUTOREDRAW": drawing.autoRedraw = (bool)VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Boolean, null).Value!; return true;
            case "FONTTRANSPARENT": throw Refused("Setting FontTransparent");
            case "DRAWWIDTH": case "DRAWSTYLE": case "DRAWMODE": case "FILLSTYLE":
                var number = Convert.ToInt32(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Integer, null).Value);
                var valid = member switch
                {
                    "DRAWWIDTH" => number >= 1,
                    "DRAWSTYLE" => number is >= 0 and <= 6,
                    "DRAWMODE" => number is >= 1 and <= 16,
                    _ => number is >= 0 and <= 7,
                };
                if (!valid) throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
                switch (member)
                {
                    case "DRAWWIDTH": drawing.drawWidth = number; break;
                    case "DRAWSTYLE": drawing.drawStyle = number; break;
                    case "DRAWMODE": drawing.drawMode = number; break;
                    default: drawing.fillStyle = number; break;
                }
                return true;
            default: return false;
        }
    }

    internal static bool IsMethod(string name) => name.Equals("Cls", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Point", StringComparison.OrdinalIgnoreCase);

    internal static Vb6Value Call(Control owner, string name, IReadOnlyList<Vb6Value> args)
    {
        var drawing = Of(owner) ?? throw Refused(name + " on this object");
        if (name.Equals("Cls", StringComparison.OrdinalIgnoreCase) && args.Count == 0)
        {
            drawing.EnsureSurface();
            Array.Fill(drawing.pixels, drawing.Background());
            drawing.currentX = drawing.currentY = 0;
            drawing.Changed();
            return Vb6Value.Nothing;
        }
        if (name.Equals("Point", StringComparison.OrdinalIgnoreCase) && args.Count == 2)
        {
            var x = drawing.PointPixel(args[0], false);
            var y = drawing.PointPixel(args[1], true);
            drawing.EnsureSurface();
            return new Vb6Value((long)(x < 0 || y < 0 || x >= drawing.width || y >= drawing.height
                ? -1 : ToOle(drawing.pixels[y * drawing.width + x] is 0 ? drawing.Background() : drawing.pixels[y * drawing.width + x])));
        }
        throw Refused(name + " with this argument count");
    }

    private int PointPixel(Vb6Value value, bool vertical)
    {
        var number = Convert.ToSingle(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Single, null).Value);
        var twips = (number - Scale.Origin(vertical)) * Scale.TwipsPerUnit(vertical);
        Whole(twips); // The measured overflow applies to Point too, without rounding it before conversion.
        return Whole(twips / VBScaleModeExtensions.PixelToTwips);
    }

    internal static NotImplementedException Refused(string what) => new(what + " is not implemented (#988)");

    private void CheckPen()
    {
        if (drawWidth != 1) throw Refused("Drawing with DrawWidth above 1");
        if (drawStyle != 0) throw Refused("Drawing with DrawStyle other than 0");
        if (drawMode != 13) throw Refused("Drawing with DrawMode other than 13");
        if (fillStyle is not (0 or 1)) throw Refused("Drawing with FillStyle other than 0 or 1");
    }

    internal static void Statement(Control owner, string method, IReadOnlyList<Vb6Value> args,
        bool firstStep, bool secondStep = false, bool shortLine = false, string? box = null)
    {
        var drawing = Of(owner) ?? throw Refused(method + " on this object");
        if (method == "Circle") throw Refused("Circle");
        drawing.CheckPen();
        var x1 = shortLine ? drawing.currentX : drawing.Twips(args[0], false, firstStep, drawing.currentX);
        var y1 = shortLine ? drawing.currentY : drawing.Twips(args[1], true, firstStep, drawing.currentY);
        var end = shortLine ? 0 : 2;
        var x2 = method == "PSet" ? x1 : drawing.Twips(args[end], false, secondStep, x1);
        var y2 = method == "PSet" ? y1 : drawing.Twips(args[end + 1], true, secondStep, y1);
        var colourIndex = method == "PSet" || shortLine ? 2 : 4;
        var colour = args.Count > colourIndex && !args[colourIndex].IsMissing
            ? MethodColor(args[colourIndex]) : drawing.Foreground();
        drawing.EnsureSurface();
        var px1 = Whole((double)x1 / VBScaleModeExtensions.PixelToTwips);
        var py1 = Whole((double)y1 / VBScaleModeExtensions.PixelToTwips);
        var px2 = Whole((double)x2 / VBScaleModeExtensions.PixelToTwips);
        var py2 = Whole((double)y2 / VBScaleModeExtensions.PixelToTwips);
        if (method == "PSet") drawing.Put(px1, py1, colour);
        else if (box is not null) drawing.Box(px1, py1, px2, py2, colour, box.Equals("BF", StringComparison.OrdinalIgnoreCase));
        else drawing.Line(px1, py1, px2, py2, colour);
        drawing.currentX = x2;
        drawing.currentY = y2;
        drawing.Changed();
    }

    private int Background() => PropertyColor(VBProperties.BackColorProperty);
    private int Foreground() => PropertyColor(VBProperties.ForeColorProperty);
    private int PropertyColor(PropertyClass property)
    {
        if (!AvaloniaInteroperability.TryGet(owner, property, out var value)) throw Refused(property.Name);
        return MethodColor(value);
    }

    private static int MethodColor(Vb6Value value)
    {
        var number = Convert.ToInt64(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Long, null).Value);
        var bits = unchecked((uint)number);
        if ((bits >> 24) == 0x80)
        {
            var colour = VBColor.FromOle(number);
            if (colour.ToBrush() is ISolidColorBrush brush)
                return unchecked((int)0xFF000000) | brush.Color.R << 16 | brush.Color.G << 8 | brush.Color.B;
            throw Refused("Drawing with this system colour");
        }
        return unchecked((int)0xFF000000) | (int)((bits & 255) << 16 | bits & 0xFF00 | (bits >> 16 & 255));
    }

    private static int ToOle(int argb) => (argb >> 16 & 255) | (argb & 0xFF00) | (argb & 255) << 16;

    private void Put(int x, int y, int colour)
    {
        if (x >= 0 && y >= 0 && x < width && y < height) pixels[y * width + x] = colour;
    }

    private void Line(int x1, int y1, int x2, int y2, int colour)
    {
        // An integer pen, with the terminal pixel excluded. Iterate the visible major axis only, even for offscreen ends.
        var dx = Math.Abs((long)x2 - x1);
        var dy = Math.Abs((long)y2 - y1);
        var horizontal = dx >= dy;
        var major = horizontal ? dx : dy;
        if (major == 0) return;
        var minor = horizontal ? dy : dx;
        var sx = Math.Sign(x2 - x1);
        var sy = Math.Sign(y2 - y1);
        var minorDirection = horizontal ? sy : sx;
        var bias = minorDirection < 0 ? major / 2 : (major - 1) / 2;
        var start = horizontal ? x1 : y1;
        var direction = horizontal ? sx : sy;
        var extent = horizontal ? width : height;
        var first = Math.Max(0L, direction > 0 ? -(long)start : (long)start - extent + 1);
        var last = Math.Min(major - 1, direction > 0 ? (long)extent - 1 - start : start);
        for (var i = first; i <= last; i++)
        {
            var offset = (i * minor + bias) / major;
            Put((int)(x1 + (horizontal ? i : offset) * sx), (int)(y1 + (horizontal ? offset : i) * sy), colour);
        }
    }

    private void Box(int x1, int y1, int x2, int y2, int colour, bool filled)
    {
        var left = Math.Min(x1, x2);
        var right = Math.Max(x1, x2);
        var top = Math.Min(y1, y2);
        var bottom = Math.Max(y1, y2);
        var fill = filled ? colour : MethodColor(new Vb6Value((long)fillColor.ToOle()));
        for (var y = Math.Max(0, top); y <= Math.Min(height - 1, bottom); y++)
        for (var x = Math.Max(0, left); x <= Math.Min(width - 1, right); x++)
            if (filled || x == left || x == right || y == top || y == bottom)
                Put(x, y, colour);
            else if (fillStyle == 0)
                Put(x, y, fill);
    }

    private void EnsureSurface()
    {
        var clientWidth = owner is VBPictureBox picture ? picture.Width - picture.ClientInset.Left - picture.ClientInset.Right
            : ControlPositions.ClientPixels((Window)owner, false);
        var clientHeight = owner is VBPictureBox box ? box.Height - box.ClientInset.Top - box.ClientInset.Bottom
            : ControlPositions.ClientPixels((Window)owner, true);
        // The oracle reads a drawn row just outside the client area. Its actual backing-store edge is unmeasured;
        // keep that extra row/column, without asserting a VB6 boundary for any other off-surface point.
        var w = Math.Max(1, (int)Math.Ceiling(clientWidth) + 1);
        var h = Math.Max(1, (int)Math.Ceiling(clientHeight) + 1);
        if (width == w && height == h) return;
        var resized = new int[checked(w * h)];
        for (var y = 0; y < Math.Min(height, h); y++)
            Array.Copy(pixels, y * width, resized, y * w, Math.Min(width, w));
        pixels = resized;
        width = w;
        height = h;
        bitmap?.Dispose();
        bitmap = null;
        dirty = true;
    }

    private void Changed() { dirty = true; Publish(); }

    private void Publish()
    {
        EnsureSurface();
        if (bitmap is null)
        {
            bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            dirty = true;
        }
        if (dirty)
        {
            using var buffer = bitmap.Lock();
            for (var y = 0; y < height; y++)
                Marshal.Copy(pixels, y * width, IntPtr.Add(buffer.Address, y * buffer.RowBytes), width);
            dirty = false;
        }
        host.Background = new ImageBrush(bitmap)
        {
            // One bitmap pixel covers one DIP, including the extra off-client row/column used by Point.
            DestinationRect = new RelativeRect(0, 0, width, height, RelativeUnit.Absolute),
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
        };
        RenderOptions.SetBitmapInterpolationMode(host, BitmapInterpolationMode.None);
        host.InvalidateVisual();
    }
}
