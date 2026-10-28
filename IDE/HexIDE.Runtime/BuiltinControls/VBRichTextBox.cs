using Avalonia.Controls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.RichText;

namespace HexIDE.Runtime.BuiltinControls;

public sealed partial class VBRichTextBox : Control
{
    internal bool MultiLine { get; init; } = true;
    internal int ScrollBars { get; init; }
    internal RtfDocument Document { get; private set; } = RtfDocument.Empty;
    internal int Start { get; private set; }
    internal int Length { get; private set; }
    private RtfCharacterFormat? insertionFormat;

    internal RtfCharacterFormat CaretFormat => insertionFormat ?? Document.Slice(Start, 1).FirstOrDefault()?.Format
        ?? Document.Runs.LastOrDefault()?.Format ?? new RtfCharacterFormat();
    internal IEnumerable<RtfRun> SelectedRuns => Length == 0
        ? [new RtfRun("", CaretFormat) { Paragraph = CaretParagraph }]
        : Document.Slice(Start, Math.Min(Length, Document.Text.Length - Start));
    private RtfParagraphFormat CaretParagraph => Document.Slice(Start, 1).FirstOrDefault()?.Paragraph
        ?? Document.Runs.LastOrDefault()?.Paragraph ?? new RtfParagraphFormat();

    internal string WriteRtf()
    {
        if (insertionFormat != null)
            throw new NotImplementedException("RichTextBox RTF insertion-format serialization is not implemented (#1100)");
        return Document.Write();
    }

    internal void Select(int start, int length, bool forceEvent = false)
    {
        selectionAnchor = null;
        navigationLine = null;
        var changed = Start != start || Length != length;
        Start = start;
        Length = length;
        insertionFormat = null;
        KeepCaretVisible();
        InvalidateVisual();
        if (changed || forceEvent) this.ExecuteSub(RichTextBoxComponentClass.SelChangeEvent);
    }

    internal void SetDocument(RtfDocument document, bool textAssignment)
    {
        if (!textAssignment) RefuseTextLimit();
        if (!MultiLine && document.Text.IndexOfAny(['\r', '\n']) >= 0)
            throw new NotImplementedException("RichTextBox single-line TextRTF assignment is not implemented (#1100)");
        Document = document;
        Select(0, 0, textAssignment);
        Changed();
    }

    internal void SetText(string text)
    {
        if (InputMaxLength < 0) RefuseTextLimit();
        if (InputMaxLength > 0 && text.Length > InputMaxLength) text = text[..InputMaxLength];
        var format = CaretFormat;
        if (!MultiLine) text = text.Split(['\r', '\n'])[0];
        var plain = RtfDocument.FromText(text);
        SetDocument(format == new RtfCharacterFormat() ? plain
            : Document.WithRuns([new RtfRun(plain.Text, format)]), true);
    }

    internal void ReplaceSelection(RtfDocument replacement, bool plain, bool fromInput = false)
    {
        if (!plain) RefuseTextLimit();
        else if (!fromInput && InputMaxLength != 0)
        {
            if (InputMaxLength > 0 && Length == 0 && Start == Document.Text.Length && Start >= InputMaxLength) return;
            throw new NotImplementedException("RichTextBox code selection replacement with MaxLength is not implemented (#1100)");
        }
        if (!MultiLine && replacement.Text.IndexOfAny(['\r', '\n']) >= 0)
            throw new NotImplementedException("RichTextBox single-line selection paragraph insertion is not implemented (#1100)");
        var inserted = plain ? new[] { new RtfRun(replacement.Text, CaretFormat) { Paragraph = CaretParagraph } }
            : replacement.Runs;
        Document = Document.WithRuns(Document.Slice(0, Start).Concat(inserted)
            .Concat(Document.Slice(Math.Min(Start + Length, Document.Text.Length), Math.Max(0, Document.Text.Length - Start - Length))));
        Select(Start + replacement.Text.Length, 0, true);
        Changed();
    }

    internal void FormatSelection(Func<RtfCharacterFormat, RtfCharacterFormat> apply)
    {
        if (Length == 0) insertionFormat = apply(CaretFormat);
        else Document = Document.WithRuns(Document.Slice(0, Start)
            .Concat(SelectedRuns.Select(r => r with { Format = apply(r.Format) }))
            .Concat(Document.Slice(Math.Min(Start + Length, Document.Text.Length), Math.Max(0, Document.Text.Length - Start - Length))));
        Changed();
    }

    internal void Align(string alignment)
    {
        var text = Document.Text;
        var from = Start == 0 ? 0 : text.LastIndexOf('\n', Start - 1, Start) + 1;
        var end = text.IndexOf('\n', Math.Min(Start + Math.Max(Length - 1, 0), text.Length));
        if (end < 0) end = text.Length;
        else end++;
        if (text.Length == 0)
        {
            Document = Document.WithRuns([new RtfRun("", CaretFormat)
            {
                Paragraph = CaretParagraph.WithControl(alignment, null)
            }]);
            Changed();
            return;
        }
        Document = Document.WithRuns(Document.Slice(0, from)
            .Concat(Document.Slice(from, end - from).Select(r => r with
            {
                Paragraph = r.Paragraph with
                {
                    Alignment = alignment,
                    Controls = System.Text.RegularExpressions.Regex.Replace(r.Paragraph.Controls, @"\\q[clr]", "") + "\\" + alignment
                }
            }))
            .Concat(Document.Slice(end, text.Length - end)));
        Changed();
    }

    private void RefuseTextLimit()
    {
        if (InputMaxLength != 0)
            throw new NotImplementedException("RichTextBox RTF editing with MaxLength is not implemented (#1100)");
    }

    private void Changed()
    {
        InvalidateVisual();
        this.ExecuteSub(TextBoxComponentClass.ChangeEvent);
    }
}
