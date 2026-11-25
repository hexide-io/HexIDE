using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Language;

/// <summary>
/// Length-based splits of a composed code buffer, and the file name a designer half renders against (#5637). They hold no
/// language's rules: a buffer is a prefix and its code, split by length. Public, because the Basic provider's own text
/// helpers forward to them and the provider uses none of Core's internals (#3411 task 2.12); not part of the add-in SDK.
/// </summary>
public static class CodeBufferText
{
    /// <summary>The number of line breaks in <paramref name="text"/>, counted by <c>'\n'</c> on every host.</summary>
    public static int LineBreaks(string text) => SourceLines.Count(text);

    /// <summary>
    /// The body of a composed buffer: everything after <paramref name="prefix"/>, split by the prefix's length; the whole
    /// buffer when the prefix is empty or longer than it.
    /// </summary>
    public static string BodyOf(string buffer, string prefix) =>
        prefix.Length > 0 && buffer.Length >= prefix.Length ? buffer[prefix.Length..] : buffer;

    /// <summary>
    /// The designer half of a rendered designer file: everything before <paramref name="code"/>, which the render appends
    /// last; the whole text when the code is empty or longer than it.
    /// </summary>
    public static string DesignerHalfOf(string rendered, string code) =>
        code.Length > 0 && rendered.Length >= code.Length ? rendered[..^code.Length] : rendered;

    /// <summary>
    /// The file name a form's designer half renders against: its file's name, or for a form with none yet the name its
    /// first save will use. Null for a form with no name.
    /// </summary>
    public static string? RenderFileNameFor(FormDefinition form)
    {
        var identity = DocumentIdentity.For(form);
        if (identity.AbsolutePath is { } path)
            return System.IO.Path.GetFileName(path);
        return identity.Name.Length == 0 ? null : identity.Name + identity.Extension;
    }
}
