using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// A TextBox's selection at run time: SelStart, SelLength and SelText (#1150). Measured on vb6.exe, native and p-code
/// agreeing (the oracle's "A TextBox's selection"):
/// <list type="bullet">
/// <item>SelStart and SelLength are Longs and SelText a String. A CR LF counts as two characters, as it is two in Text.</item>
/// <item>Setting SelStart drops the selection, and setting Text puts the caret at 0. A SelStart past the end is the end,
/// and a SelLength past the end runs to it, both with no error. A negative one is 380, and Null is 94; the selection is
/// left as it was.</item>
/// <item>Setting SelText replaces the selection, or inserts at the caret, and leaves the caret after what it put in.
/// It is code's, so a Locked box takes it.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>VB6 keeps SelText to MaxLength (measured), which HexIDE's TextBox doesn't model yet, so nothing limits it here.</para>
/// <para>VB6 puts SelStart between the CR and the LF of a line break where code asks (measured: start 3 in
/// <c>"ab" &amp; vbCrLf &amp; "cd"</c> selects the LF and the c), and Avalonia moves it past the break. So the selection code
/// sets is remembered, with where Avalonia put it, and read back while Avalonia's is still there: once the user moves
/// it, Avalonia's is the selection.</para>
/// </remarks>
internal static class TextBoxMembers
{
    private sealed record Set(int Start, int End, int AvaloniaStart, int AvaloniaEnd);

    private static readonly ConditionalWeakTable<TextBox, Set> set = new();

    public static bool TryGet(Control control, string member, out Vb6Value value)
    {
        value = default;
        if (control is not VBTextBox box)
            return false;
        var (start, length) = Selection(box);
        switch (member.ToLowerInvariant())
        {
            case "selstart":
                value = new Vb6Value((long)start);
                return true;
            case "sellength":
                value = new Vb6Value((long)length);
                return true;
            case "seltext":
                value = new Vb6Value(TextOf(box).Substring(start, length));
                return true;
            default:
                return false;
        }
    }

    public static bool TrySet(Control control, string member, Vb6Value value)
    {
        if (control is not VBTextBox box)
            return false;
        switch (member.ToLowerInvariant())
        {
            case "selstart":
            {
                var start = (int)Math.Min(Position(value), TextOf(box).Length);
                Select(box, start, start);
                return true;
            }
            case "sellength":
            {
                var length = Position(value);
                var (start, _) = Selection(box);
                Select(box, start, (int)Math.Min(start + length, TextOf(box).Length));
                return true;
            }
            case "seltext":
            {
                if (value.IsNull)
                    throw new VBRunTimeException(VBStandardError.InvalidUseOfNull);
                var insert = (string)VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.String, null).Value!;
                var (start, length) = Selection(box);
                var text = TextOf(box);
                box.Text = text[..start] + insert + text[(start + length)..];
                Select(box, start + insert.Length, start + insert.Length);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>Puts the caret at the start, as setting Text from code does (measured).</summary>
    public static void TextSet(VBTextBox box) => Select(box, 0, 0);

    private static string TextOf(TextBox box) => box.Text ?? "";

    // Where the selection starts and how long it is. Avalonia keeps the end the user dragged to, which may be before the
    // start; VB6's SelStart is always the lower.
    private static (int Start, int Length) Selection(TextBox box)
    {
        var length = TextOf(box).Length;
        if (set.TryGetValue(box, out var remembered) && remembered.AvaloniaStart == box.SelectionStart
            && remembered.AvaloniaEnd == box.SelectionEnd && remembered.End <= length)
            return (remembered.Start, remembered.End - remembered.Start);
        var a = Math.Clamp(box.SelectionStart, 0, length);
        var b = Math.Clamp(box.SelectionEnd, 0, length);
        return (Math.Min(a, b), Math.Abs(b - a));
    }

    private static void Select(TextBox box, int start, int end)
    {
        box.SelectionStart = start;
        box.SelectionEnd = end;
        box.CaretIndex = end;
        // CaretIndex clears the selection in Avalonia, so it is put back after.
        box.SelectionStart = start;
        box.SelectionEnd = end;
        set.AddOrUpdate(box, new Set(start, end, box.SelectionStart, box.SelectionEnd));
    }

    // A SelStart or SelLength: a Long, rounded as an assignment rounds it, 94 for Null and 380 below 0.
    private static long Position(Vb6Value value)
    {
        if (value.IsNull)
            throw new VBRunTimeException(VBStandardError.InvalidUseOfNull);
        var position = Convert.ToInt64(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Long, null).Value, CultureInfo.InvariantCulture);
        return position < 0 ? throw new VBRunTimeException(VBStandardError.InvalidPropertyValue) : position;
    }
}
