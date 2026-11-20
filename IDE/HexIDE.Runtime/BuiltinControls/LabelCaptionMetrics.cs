using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.BuiltinControls;

internal static partial class LabelCaptionMetrics
{
    internal static Size Measure(VBLabel label)
    {
        var caption = label.Text ?? "";
        if (label.RecognizesAccessKey) caption = VBCaption.Parse(caption).Drawn(false);
        if (caption.Length > 0 && caption.All(c => c is '\r' or '\n') && caption != "\r\n")
            throw new NotImplementedException("AutoSize for a Label caption containing only these line breaks is not measured (#922)");
        var border = label.BorderStyle == VBBorder.None ? 0 : label.Appearance == VBAppearance.Flat ? 2 : 4;
        var font = AttachedProperties.GetFont(label) ?? VBFont.Default;
        if (OperatingSystem.IsWindows()) return Windows(caption, font, label.WordWrap, Math.Round(label.Width), border);
        var face = new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch);
        var glyphs = face.GlyphTypeface;
        var scale = label.FontSize / glyphs.Metrics.DesignEmHeight;
        double Width(string text)
        {
            double width = 0;
            foreach (var rune in text.EnumerateRunes())
            {
                glyphs.TryGetHorizontalGlyphAdvance(glyphs.CharacterToGlyphMap.GetGlyph(rune.Value), out var advance);
                width += Math.Round(advance * scale);
            }
            return width;
        }
        var height = Math.Ceiling((glyphs.Metrics.Descent - glyphs.Metrics.Ascent + glyphs.Metrics.LineGap) * scale);
        return Layout(caption, Width, height, 0, label.WordWrap, Math.Round(label.Width), border);
    }

    private static Size Layout(string caption, Func<string, double> width, double lineHeight, int overhang,
        bool wrap, double oldWidth, int border)
    {
        var lines = caption.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var natural = caption.Length == 0 ? width(" ") : Math.Max(1, lines.Max(width));
        var content = natural + overhang + border;
        var count = lines.Length;
        if (wrap)
        {
            var widestWord = lines.SelectMany(line => line.Split(' ')).Max(width);
            content = Math.Max(oldWidth, widestWord + overhang + border);
            count = 0;
            foreach (var line in lines)
            {
                double used = 0;
                count++;
                foreach (var word in line.Split(' '))
                {
                    var extent = width(word);
                    var gap = used == 0 ? 0 : width(" ");
                    if (used > 0 && used + gap + extent > oldWidth - border)
                    {
                        count++;
                        used = extent;
                    }
                    else used += gap + extent;
                }
            }
        }
        return new Size(content, count * lineHeight + border);
    }

    private static Size Windows(string caption, VBFont font, bool wrap, double oldWidth, int border)
    {
        var dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) throw new Win32Exception();
        IntPtr handle = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            handle = CreateFontW(-(int)Math.Round(font.ToFontSize()), 0, 0, 0, font.Weight,
                font.Italic ? 1u : 0u, font.Underline ? 1u : 0u, font.Strikethrough ? 1u : 0u,
                (uint)font.Charset, 0, 0, 0, 0, font.FontFamilyName);
            if (handle == IntPtr.Zero) throw new Win32Exception();
            previous = SelectObject(dc, handle);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1)) throw new Win32Exception();
            if (!GetTextMetricsW(dc, out var metrics)) throw new Win32Exception();
            double Width(string text)
            {
                if (!GetTextExtentPoint32W(dc, text, text.Length, out var extent)) throw new Win32Exception();
                // The extent leaves out tmOverhang. Synthetic bold widens each character instead, so MS Sans
                // Serif 8.25 bold measures Hello at 29 px with an overhang of 1, and italic at 24 px with 6.
                // VB6 sized both to 30 px, so Layout adds the overhang once, to the widest line or word.
                return text.Length == 0 ? 0 : extent.Width;
            }
            return Layout(caption, Width, metrics.Height, metrics.Overhang, wrap, oldWidth, border);
        }
        finally
        {
            if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(dc, previous);
            if (handle != IntPtr.Zero) DeleteObject(handle);
            DeleteDC(dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Extent { public int Width; public int Height; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Metrics
    {
        public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AverageWidth, MaximumWidth, Weight, Overhang, AspectX, AspectY;
        public ushort First, Last, Default, Break;
        public byte Italic, Underlined, StruckOut, PitchAndFamily, Charset;
    }
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr CreateCompatibleDC(IntPtr dc);
    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charset, uint outputPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string face);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial IntPtr SelectObject(IntPtr dc, IntPtr value);
    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTextExtentPoint32W(IntPtr dc, string text, int count, out Extent extent);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTextMetricsW(IntPtr dc, out Metrics metrics);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr value);
    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr dc);
}
