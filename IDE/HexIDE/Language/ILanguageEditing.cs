using System;

namespace HexIDE.Language;

/// <summary>
/// The editing helpers registered for each language (#5403), as the code window, the archive view, the Object Browser and the
/// Watch dialog ask for them. A language with none registered gets none: no coloured brackets, no greyed <c>#If</c> branches,
/// and no declarations, as a plain-text file has.
/// </summary>
/// <remarks>
/// Each reader it hands out looks the language up again on every call, so a reader taken before a registration arrives starts
/// answering once it does, and one taken before it's withdrawn stops. A throw from the registered helper answers as though
/// nothing were registered.
/// </remarks>
public interface ILanguageEditing
{
    /// <summary>Which lines an <c>#If</c> leaves out, for <paramref name="languageId"/>'s documents. None for a null id.</summary>
    IConditionalCodeReader ConditionalCodeFor(string? languageId);

    /// <summary>Where the parentheses are, for <paramref name="languageId"/>'s documents. None for a null id.</summary>
    IBracketReader BracketsFor(string? languageId);

    /// <summary>What a module declares, for <paramref name="languageId"/>'s documents. None for a null id.</summary>
    IDeclarationReader DeclarationsFor(string? languageId);

    /// <summary>
    /// Raised when a registration arrives or is withdrawn, on whatever thread that happens. The add-in registry withdraws
    /// registrations under its lock, so a handler only posts to the UI thread: it never invokes it or waits on it, and it
    /// reads nothing again until the posted job runs.
    /// </summary>
    event Action? Changed;
}
