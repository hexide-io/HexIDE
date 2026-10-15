using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.Serialization;

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
/// what the read returns, the whole file with its header unchanged, or the code alone with the header kept,
/// through <see cref="GuardedContent"/>, which is the one place that decides what a replacement may touch.
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
    public static string Read(DocumentIdentity document, CodeEditorViewModel? editor) =>
        TextOf(document, editor).Text;

    /// <summary>
    /// What <see cref="Read"/> returns, with what its line numbers need: the document as its code window
    /// numbers it, open or not.
    /// </summary>
    public static CodeWindowText TextOf(DocumentIdentity document, CodeEditorViewModel? editor) =>
        editor?.CodeWindowText ?? CodeWindowText.Of(document);

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
    /// Replaces the document's content with <paramref name="incoming"/>, keeping its header, or refuses when
    /// the content would change the header or a member's attribute lines (<see cref="GuardedContent"/>). Nothing
    /// changes when it is refused.
    /// </summary>
    /// <remarks>
    /// With a code window open the buffer takes the text and the model is given it at the next flush, as it is
    /// given anything typed. With none, the model's code takes it directly: the rule is the same, applied to
    /// the model's composition of the file, and the body is what is written back to it.
    /// </remarks>
    public static ContentReplacement Write(DocumentIdentity document, CodeEditorViewModel? editor, string incoming)
    {
        if (editor is not null)
            return editor.ReplaceContent(incoming);

        var (whole, prefix, update) = document.Module is { } module
            ? (FormCodeText.WholeFile(module), FormCodeText.Prefix(module), (Action<string>)module.UpdateCode)
            : (FormCodeText.WholeFile(document.Form!), FormCodeText.Prefix(document.Form!), document.Form!.UpdateCode);
        var result = GuardedContent.Replace(whole, prefix.Length, incoming);
        if (result.NewText is { } text)
            update(text[prefix.Length..]);
        return result;
    }

    /// <summary>
    /// True when <paramref name="incoming"/> is a form's code with its leading <c>Attribute</c> lines left out,
    /// and the form has them: the case a write reports, because those lines carry the form's identity and a
    /// caller composing code rather than editing what it read leaves them out without knowing.
    /// </summary>
    /// <remarks>
    /// Asked of the caller's text, not of whether the guard put anything back: the guard always puts the
    /// designer block back in front of code sent alone, so its own answer is true for text that left out
    /// nothing a caller could have been expected to send.
    /// </remarks>
    public static bool LeftOutAttributeLines(DocumentIdentity document, string incoming) =>
        document.Form is { } form
        && ReadOnlyRegions.HeaderEnd(incoming, 0) == 0
        && FormCodeText.AttributeBlock(form.Code).Length > 0;
}
