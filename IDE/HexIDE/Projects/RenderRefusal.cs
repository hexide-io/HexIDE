namespace HexIDE.Projects;

/// <summary>
/// An add-in's format didn't render a form, or its writer didn't render a project file: it threw, it's no longer loaded, or
/// what it rendered can't be written where it would go. It ends a save before anything is written, as a character the code
/// page can't hold does.
/// </summary>
/// <param name="document">The form, UserControl or PropertyPage, or the project.</param>
/// <param name="addin">The add-in whose format or writer it is, for display.</param>
/// <param name="reason">Why, for the log.</param>
/// <param name="addinWords">What the add-in said, where it threw: shown as it gave it. Null where the IDE refused the render.</param>
internal sealed class RenderRefusal(string document, string addin, string reason, string? addinWords = null)
    : Exception($"{addin} didn't render {document}: {reason}")
{
    public string Document { get; } = document;

    public string Addin { get; } = addin;

    public string Reason { get; } = reason;

    public string? AddinWords { get; } = addinWords;
}
