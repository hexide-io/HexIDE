using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.BuiltinControls;

// Runtime only: the designer uses VBLabel too, but its AutoSize has not been measured (#922).
internal static class LabelAutoSize
{
    private sealed class State { internal bool Enabled; }
    private static readonly ConditionalWeakTable<VBLabel, State> states = new();

    internal static void Load(VBLabel label, bool enabled) => states.GetOrCreateValue(label).Enabled = enabled;
    internal static bool Get(VBLabel label) => states.TryGetValue(label, out var state) && state.Enabled;
    internal static void Set(VBLabel label, bool enabled)
    {
        var previous = Get(label);
        Load(label, enabled);
        if (enabled && !previous) Resize(label);
    }

    internal static void Caption(VBLabel label, string caption)
    {
        if (label.Text == caption) return;
        label.Text = caption;
        Resize(label);
    }

    internal static void Resize(VBLabel label)
    {
        if (!Get(label)) return;
        var size = LabelCaptionMetrics.Measure(label);
        var old = new Rect(Canvas.GetLeft(label), Canvas.GetTop(label), label.Width, label.Height);
        var next = Place(old, size, label.Alignment, label.WordWrap);
        if (next == old) return;
        Canvas.SetLeft(label, next.X);
        Canvas.SetTop(label, next.Y);
        label.Width = next.Width;
        label.Height = next.Height;
    }

    // #922 and oracle PR #4984: whole-pixel placement, retaining unsnapped values if nothing changes.
    internal static Rect Place(Rect old, Size content, VBTextAlignment alignment, bool wordWrap)
    {
        var rounded = new Rect(Math.Round(old.X), Math.Round(old.Y), Math.Round(old.Width), Math.Round(old.Height));
        var width = content.Width;
        var left = rounded.X;
        if (alignment == VBTextAlignment.RightJustify)
            left += rounded.Width - width;
        else if (alignment == VBTextAlignment.Center && !(wordWrap && width == rounded.Width))
        {
            left += Math.Floor(rounded.Width / 2) - Math.Ceiling(width / 2);
            width = 2 * Math.Ceiling(width / 2) + rounded.Width % 2;
        }
        var result = new Rect(left, rounded.Y, width, content.Height);
        return result == rounded ? old : result;
    }
}
