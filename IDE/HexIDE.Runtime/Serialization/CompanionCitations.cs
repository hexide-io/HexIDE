using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// The citations a designer file makes of its companion, written as placeholders and resolved once the text is
/// finished, so the companion is laid out in the order the text cites it (#60). That is how VB6 lays one out.
/// </summary>
/// <remarks>
/// <para>
/// A citation can come from a modelled property, from a property HexIDE carries without modelling, or from a line
/// of an unmodelled control's preserved block. The writer emits each as a placeholder wherever it goes, sorted
/// property blocks included, and <see cref="Resolve"/> reads the finished text top to bottom. Each record lands in
/// the order of its first citation, and every citation becomes <c>"name.frx":HHHH</c> for wherever its record
/// landed. So the layout follows the text by construction.
/// </para>
/// <para>
/// The records used to be collected before the text was written, in an order that had to repeat the writer's.
/// That got ODBC Log In's two ComboBox records the wrong way round, and it could not have placed a carried
/// record at all: those sort among the modelled properties, and a preserved block's sit between its siblings.
/// </para>
/// <para>
/// A carried citation is rewritten whole rather than kept as it was read. Kept, it would pin its record to an
/// offset that a modelled record before it may have outgrown, and then it would address that record's bytes.
/// </para>
/// </remarks>
internal sealed class CompanionCitations
{
    // Never in a designer file VB6 wrote, so a placeholder cannot be mistaken for any of its text. A damaged file
    // can still hold one, and a carried line keeps it: Resolve counts the marks against the placeholders and refuses.
    private const char Mark = (char)0;

    private readonly List<byte[]> records = [];
    private readonly Dictionary<byte[], int> indexOf = new(ReferenceEqualityComparer.Instance);
    private int placeholders;

    /// <summary>
    /// A placeholder citing <paramref name="record"/>. By reference, as <see cref="FrxDeserializer"/> hands one
    /// array to every property citing an offset, so one record cited twice is written once.
    /// </summary>
    public string Cite(byte[] record)
    {
        if (!indexOf.TryGetValue(record, out var index))
        {
            index = records.Count;
            records.Add(record);
            indexOf[record] = index;
        }
        placeholders++;
        return $"{Mark}{index}{Mark}";
    }

    /// <summary>
    /// <paramref name="line"/> with its citation, if it has one, replaced by a placeholder citing the record it
    /// cited when it was read. <paramref name="cited"/> holds those records by the offsets the lines cite.
    /// </summary>
    /// <exception cref="InvalidOperationException">The line cites a record the form does not hold.</exception>
    public string Cite(string line, IReadOnlyDictionary<int, byte[]>? cited)
    {
        if (FrxDeserializer.Citation(line) is not { } citation)
            return line;
        if (cited is null || !cited.TryGetValue(citation.Offset, out var record))
            throw new InvalidOperationException(
                $"'{line.Trim()}' cites a companion record this form does not hold. Refusing to write a citation "
              + "that would address some other record's bytes.");
        return line[..citation.Start] + Cite(record) + line[(citation.Start + citation.Length)..];
    }

    /// <summary>
    /// <paramref name="text"/> with every placeholder resolved to <paramref name="companionName"/> and an offset,
    /// and the companion those offsets address, or null when the text cites nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">The text holds a NUL that is not a placeholder's.</exception>
    public (string Text, byte[]? Companion) Resolve(string text, string companionName)
    {
        if (records.Count == 0)
            return (text, null);

        if (text.Count(c => c == Mark) != 2 * placeholders)
            throw new InvalidOperationException(
                "The form's designer text holds a NUL character, so the citations of its companion can't be placed. "
              + "VB6 never writes one there; it came in with the file as it was read.");

        var order = new List<int>();
        var placed = new HashSet<int>();
        foreach (var (_, _, index) in Placeholders(text))
        {
            if (placed.Add(index))
                order.Add(index);
        }

        var (companion, offsets) = FrxSerializer.Write(order.Select(i => records[i]));
        var resolved = new StringBuilder(text.Length);
        var at = 0;
        foreach (var (start, length, index) in Placeholders(text))
        {
            resolved.Append(text, at, start - at)
                .Append(CultureInfo.InvariantCulture, $"\"{companionName}\":{offsets[records[index]]:X4}");
            at = start + length;
        }
        resolved.Append(text, at, text.Length - at);
        return (resolved.ToString(), companion);
    }

    private static IEnumerable<(int Start, int Length, int Index)> Placeholders(string text)
    {
        for (var open = text.IndexOf(Mark); open >= 0; open = text.IndexOf(Mark, open))
        {
            var close = text.IndexOf(Mark, open + 1);
            yield return (open, close + 1 - open,
                int.Parse(text.AsSpan(open + 1, close - open - 1), CultureInfo.InvariantCulture));
            open = close + 1;
        }
    }
}
