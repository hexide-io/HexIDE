namespace HexIDE.Runtime.BuiltinTypes;

/// <summary>
/// Framework-agnostic VB6 font descriptor. UI frameworks convert to their
/// native font types via extension methods (e.g. VBFontExtensions in Runtime).
///
/// Every field VB6 writes in a <c>BeginProperty Font</c> block is held here, because anything not held is
/// regenerated from a guess on save. Before 2026-08-19 this carried only family, size, bold and italic:
/// <see cref="Size"/> was an <c>int</c>, so VB6's <c>Size = 9.6</c> came back as <c>10</c>; and Charset,
/// Underline and Strikethrough were not read at all — the writer emitted the constants 2, False and False
/// regardless of what the file said. Microsoft's own <c>Mover ListBox.frm</c> declares
/// <c>Size = 9.6</c> with <c>Charset = 0</c>, and lost both on every save.
///
/// Those are wrong values rather than different bytes: a form saved that way reopens in VB6 with a
/// different font. Byte-identity is the gate that caught it, but correctness is why it matters.
/// </summary>
public readonly record struct VBFont
{
    public readonly string FontFamilyName;

    /// <summary>Point size. Fractional — VB6 writes 9.6 and 8.25 routinely.</summary>
    public readonly double Size;

    /// <summary>
    /// The raw weight VB6 recorded, 400 for normal and 700 for bold. Stored rather than derived from a
    /// bool, so a file carrying any other value round-trips it instead of being snapped to one of two.
    /// </summary>
    public readonly int Weight;

    public readonly bool Italic;
    public readonly bool Underline;
    public readonly bool Strikethrough;

    /// <summary>
    /// The Windows character set. 0 (ANSI) is what VB6's own templates use; the writer used to emit a
    /// hardcoded 2 (Symbol) for every font it wrote, which is the one value least likely to be right.
    /// </summary>
    public readonly int Charset;

    public VBFont(string fontFamilyName, double size,
        bool bold = false,
        bool italic = false,
        int charset = 0,
        bool underline = false,
        bool strikethrough = false,
        int? weight = null)
    {
        FontFamilyName = fontFamilyName;
        Size = size;
        Weight = weight ?? (bold ? BoldWeight : NormalWeight);
        Italic = italic;
        Charset = charset;
        Underline = underline;
        Strikethrough = strikethrough;
    }

    public const int NormalWeight = 400;
    public const int BoldWeight = 700;

    /// <summary>Derived, so every existing caller and the whole rendering path is unaffected.</summary>
    public bool Bold => Weight >= BoldWeight;

    public static VBFont Default { get; } = new("MS Sans Serif", 11);

    public override string ToString()
    {
        return FontFamilyName;
    }
}
