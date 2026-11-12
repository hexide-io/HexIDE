using HexIDE.Forms.ViewModels;
using HexIDE.Language;
using HexIDE.Runtime.Serialization;
using HexIDE.Utils;

namespace HexIDE.IDE;

/// <summary>
/// Reading and replacing a form's, module's or class's content as one text: the rule automation's
/// <c>get_file_content</c> and <c>set_file_content</c> share (hexide-io/HexIDE#273 task 3.18).
/// </summary>
/// <remarks>
/// <para>
/// <b>The text is the whole file, and it is the text every line number counts in.</b> An open code window's
/// buffer, or, with no window open, the model's composition of the same file, so line 1 is the file's first
/// line whether or not the document happens to be open, exactly as the mark tools count it. The write accepts
/// what the read returns, through <see cref="ContentReplacementRules"/>, which decides what a replacement
/// may touch. A format may also accept code alone and keep the header; the IDE's fallback refuses code
/// alone when the prefix holds a read-only region.
/// </para>
/// <para>
/// The automation tools are compiled into an executable nothing else references, so the rule lives here,
/// where a test can reach it, and the tools only relay its answer, as they do <see cref="MarkLineRules"/>.
/// </para>
/// </remarks>
public static class DocumentContent
{
    /// <summary>
    /// The document's whole file: <paramref name="editor"/>'s buffer when a code window is open on it, and
    /// otherwise the model's composition of the file.
    /// </summary>
    /// <param name="document">The form, module or class.</param>
    /// <param name="editor">The code window open on <paramref name="document"/>, or null when there is none.</param>
    public static string Read(DocumentIdentity document, CodeEditorViewModel? editor,
        IProjectFileFormat format, IReadOnlyRegionReader reader) =>
        TextOf(document, editor, format, reader).Text;

    /// <summary>
    /// What <see cref="Read"/> returns, with what its line numbers need: the document as its code window
    /// numbers it, open or not.
    /// </summary>
    public static CodeWindowText TextOf(DocumentIdentity document, CodeEditorViewModel? editor,
        IProjectFileFormat format, IReadOnlyRegionReader reader)
    {
        if (editor is not null) return editor.CodeWindowText;
        var prefix = document.Module is { } module ? format.CodePrefix(module) : format.CodePrefix(document.Form!);
        var text = prefix + (document.Module?.Code ?? document.Form!.Code);
        return new CodeWindowText(text, prefix.Length, reader.Regions(text, prefix.Length));
    }

    /// <summary>
    /// True when <paramref name="editor"/> holds code its document's model has not been given yet.
    /// </summary>
    /// <remarks>
    /// <b>Compares the code section, never the text <see cref="Read"/> returns.</b> The buffer is the whole
    /// file and the model's <c>Code</c> is the code section, so the two are never equal and comparing them
    /// would call every open document edited. Nor is the model's composition the thing to compare against:
    /// after a rename the buffer's prefix and the model's can differ in length until both are refreshed
    /// together. So the buffer's body is compared with <c>Code</c>, split exactly as the flush splits it, which
    /// is the rule the dirty detector applies too.
    /// </remarks>
    public static bool CodeAheadOfModel(DocumentIdentity document, CodeEditorViewModel? editor) =>
        editor is not null
        && !string.Equals(editor.BufferBody, document.Module?.Code ?? document.Form!.Code, StringComparison.Ordinal);

    /// <summary>
    /// Replaces the document's content with <paramref name="incoming"/> through the format's replacement rule,
    /// or the IDE's when there are no rules or Replace returns null. Keeping the header is the format's
    /// behaviour; the IDE's rule refuses code alone when the prefix holds a region. Nothing changes on refusal.
    /// </summary>
    /// <remarks>
    /// With a code window open the buffer takes the text and the model is given it at the next flush, as it is
    /// given anything typed. With none, the model's code takes it directly: the rule is the same, applied to
    /// the model's composition of the file, and the body is what is written back to it.
    /// </remarks>
    public static ContentReplacement Write(DocumentIdentity document, CodeEditorViewModel? editor, string incoming,
        IReadOnlyRegionRules? rules, IProjectFileFormat format, IReadOnlyRegionReader reader)
    {
        if (editor is not null)
            return editor.ReplaceContent(incoming);

        var prefix = document.Module is { } module ? format.CodePrefix(module) : format.CodePrefix(document.Form!);
        var whole = prefix + (document.Module?.Code ?? document.Form!.Code);
        Action<string> update = document.Module is { } target ? target.UpdateCode : document.Form!.UpdateCode;
        var result = ContentReplacementRules.Replace(whole, prefix,
            DocumentNewLines.Normalize(incoming, whole), reader, rules);
        if (result.NewText is { } text)
            update(text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : text);
        return result;
    }

    /// <summary>
    /// The model's text and leading region when incoming text read without a prefix has no leading region,
    /// the document's leading region ends past its prefix, and the document is not a module for which
    /// <c>PrefixCarriesName</c> is true. The prefix may be empty, as a new form's is. Otherwise null.
    /// </summary>
    public static (CodeWindowText Text, TextRegion Region)? LeftOutLeadingRegion(DocumentIdentity document, string incoming,
        IProjectFileFormat format, IReadOnlyRegionReader reader)
    {
        var text = TextOf(document, null, format, reader);
        return new CodeWindowText(incoming, 0, reader.Regions(incoming, 0)).LeadingRegion is null
            && text.LeadingRegion is { } leading && leading.End > text.PrefixLength
            && !(document.Module is { } module && format.PrefixCarriesName(module))
            ? (text, leading) : null;
    }
}
