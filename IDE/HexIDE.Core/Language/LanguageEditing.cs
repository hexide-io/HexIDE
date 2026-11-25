namespace HexIDE.Language;

/// <summary>
/// The editing helpers one owner supplies for the languages it names (#5403): which lines an <c>#If</c> leaves out, where the
/// parentheses are, and what a module declares. Each is optional, and a language with none registered gets none of them, as
/// a plain-text file does.
/// </summary>
/// <param name="LanguageIds">
/// The language ids it serves, such as <c>vb6</c>, compared in any case. An id another owner already serves is refused.
/// </param>
public sealed record LanguageEditing(IReadOnlyList<string> LanguageIds)
{
    /// <summary>What greys the lines an <c>#If</c> leaves out, or null for none.</summary>
    public IConditionalCodeReader? ConditionalCode { get; init; }

    /// <summary>What finds the parentheses the code window colours by depth, or null for none.</summary>
    public IBracketReader? Brackets { get; init; }

    /// <summary>What lists a module's declarations when no language server does, or null for none.</summary>
    public IDeclarationReader? Declarations { get; init; }
}
