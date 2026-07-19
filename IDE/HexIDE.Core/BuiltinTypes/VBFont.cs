namespace HexIDE.Runtime.BuiltinTypes;

/// <summary>
/// Framework-agnostic VB6 font descriptor. UI frameworks convert to their
/// native font types via extension methods (e.g. VBFontExtensions in Runtime).
/// </summary>
public readonly record struct VBFont
{
    public readonly string FontFamilyName;
    public readonly int Size;
    public readonly bool Bold;
    public readonly bool Italic;

    public VBFont(string fontFamilyName, int size,
        bool bold = false,
        bool italic = false)
    {
        FontFamilyName = fontFamilyName;
        Size = size;
        Bold = bold;
        Italic = italic;
    }

    public static VBFont Default { get; } = new("MS Sans Serif", 11);

    public override string ToString()
    {
        return FontFamilyName;
    }
}
