using System;
using System.Globalization;

namespace HexIDE.Runtime.BuiltinTypes;

public readonly struct VBColor : IEquatable<VBColor>
{
    public readonly ColorType Type;
    public readonly byte R;
    public readonly byte G;
    public readonly byte B;

    public VbSystemColor SystemColor => (VbSystemColor)R;

    public bool Equals(VBColor other) => Type == other.Type && R == other.R && G == other.G && B == other.B;

    public override bool Equals(object? obj) => obj is VBColor other && Equals(other);

    public override int GetHashCode() => HashCode.Combine((int)Type, R, G, B);

    public static bool operator ==(VBColor left, VBColor right) => left.Equals(right);

    public static bool operator !=(VBColor left, VBColor right) => !left.Equals(right);

    private VBColor(ColorType type, byte r, byte g, byte b)
    {
        Type = type;
        R = r;
        G = g;
        B = b;
    }

    public override string ToString()
    {
        return $"&H{(int)Type:X2}{B:X2}{G:X2}{R:X2}&";
    }

    public static bool TryParse(string str, out VBColor result)
    {
        result = default;
        if (!str.StartsWith("&h", StringComparison.OrdinalIgnoreCase))
            return false;

        // The fixed slices below read up to index 10 (`&H` + 8 hex digits). A shorter `&H…` value would make the
        // range slice throw ArgumentOutOfRangeException — and a TryParse must never throw (it aborts form load).
        if (str.Length < 10)
            return false;

        if (!int.TryParse(str[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var type) ||
            !Enum.IsDefined(typeof(ColorType), type) ||
            !byte.TryParse(str[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue) ||
            !byte.TryParse(str[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(str[8..10], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red))
            return false;

        result = new VBColor((ColorType)type, red, green, blue);
        return true;
    }

    public static VBColor FromColor(byte r, byte g, byte b)
    {
        return new VBColor(ColorType.Raw, r, g, b);
    }

    public static VBColor FromSystemColor(VbSystemColor systemColor)
    {
        return new VBColor(ColorType.SystemColor, (byte)systemColor, 0, 0);
    }

    /// <summary>Convert a numeric VB6 OLE_COLOR to a <see cref="VBColor"/>: high byte 0x80 selects a system
    /// colour (low byte = index), otherwise it is a raw 0x00BBGGRR value. This is the property-boundary
    /// conversion for <c>ctrl.BackColor = &amp;HFF0000</c>, now that &amp;H literals evaluate as numbers.</summary>
    public static VBColor FromOle(long ole)
    {
        uint u = (uint)ole;
        if ((u >> 24) == 0x80)
            return FromSystemColor((VbSystemColor)(byte)(u & 0xFF));
        return FromColor((byte)(u & 0xFF), (byte)((u >> 8) & 0xFF), (byte)((u >> 16) & 0xFF));   // 0x00BBGGRR
    }

    public enum ColorType
    {
        Raw = 0,
        SystemColor = 0x80
    }

    public static VBColor Black => FromColor(0, 0, 0);
}

public enum VbSystemColor
{
    Scrollbar = 0,
    Desktop = 1,
    Activecaption = 2,
    Inactivecaption = 3,
    Menu = 4,
    Window = 5,
    Windowframe = 6,
    Menutext = 7,
    Windowtext = 8,
    Captiontext = 9,
    Activeborder = 10,
    Inactiveborder = 11,
    Appworkspace = 12,
    Highlight = 13,
    Highlighttext = 14,
    Btnface = 15,
    Btnshadow = 16,
    Graytext = 17,
    Btntext = 18,
    Inactivecaptiontext = 19,
    Btnhighlight = 20,
    _3DDKSHADOW = 21,
    _3DLIGHT = 22,
    Infotext = 23,
    Infobk = 24,
}
