using System.Globalization;

namespace HexIDE.Runtime.RichText;

internal sealed record RtfParagraphFormat(
    string Alignment = "ql", int LeftIndent = 0, int RightIndent = 0, int FirstLineIndent = 0)
{
    public IReadOnlyList<int> TabStops { get; init; } = Array.Empty<int>();
    public int? BulletFont { get; init; }
    // Keep the accepted words in order; no unmeasured rich-edit normalisation is inferred.
    public string Controls { get; init; } = "";

    public RtfParagraphFormat WithControl(string word, int? number)
    {
        var next = word switch
        {
            "ql" or "qc" or "qr" => this with { Alignment = word },
            "li" when number.HasValue => this with { LeftIndent = number.Value },
            "ri" when number.HasValue => this with { RightIndent = number.Value },
            "fi" when number.HasValue => this with { FirstLineIndent = number.Value },
            "tx" when number.HasValue => this with { TabStops = Array.AsReadOnly(TabStops.Append(number.Value).ToArray()) },
            _ => throw new NotImplementedException($"RichTextBox RTF: paragraph word '{word}' is not implemented (#1100)")
        };
        return next with { Controls = Controls + "\\" + word + number?.ToString(CultureInfo.InvariantCulture) };
    }
}
