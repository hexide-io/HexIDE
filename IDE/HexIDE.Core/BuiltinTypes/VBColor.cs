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
