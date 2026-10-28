namespace HexIDE.Runtime.RichText;

internal sealed record RtfCharacterFormat(
    string FontName = "MS Sans Serif", int HalfPoints = 17, int Color = 0,
    bool Bold = false, bool Italic = false, bool Underline = false,
    bool StrikeThrough = false, bool Protected = false, float PointSize = 8.25f);

internal sealed record RtfRun(string Text, RtfCharacterFormat Format)
{
    public RtfParagraphFormat Paragraph { get; init; } = new();
}
