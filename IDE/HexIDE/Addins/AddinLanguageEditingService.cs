using System.Threading;
using HexIDE.Language;
using HexIDE.Lsp.Messages;
using Serilog;

namespace HexIDE.Addins;

/// <summary>
/// The editing helpers each language's owner registered (#5403): which lines an <c>#If</c> leaves out, where the parentheses
/// are, and what a module declares. The code window, the archive view, the Object Browser and the Watch dialog ask here by
/// language id.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deny by default.</b> Only an owner whose grant says so may register (<see cref="RegistrationGrant.LanguageEditing"/>).
/// Today that's the Basic provider alone.
/// </para>
/// <para>
/// <b>It only adds.</b> A registration naming a language another owner serves is refused whole and logged, naming both
/// owners, so nothing replaces a language's helpers silently.
/// </para>
/// <para>
/// <b>No helper runs under a lock.</b> The registry's lock guards only its table, and <see cref="Changed"/> is raised after
/// it's released. The add-in registry withdraws a registration under its own lock, so <see cref="Changed"/>'s handlers only
/// post to the UI thread.
/// </para>
/// <para>
/// <b>A throwing helper is contained.</b> That call answers as though nothing were registered, and the registration stays: the
/// code window asks on every edit, and a bad helper mustn't take the editor down. The first throw from each helper is logged
/// as a warning and later ones at debug, so a helper that throws on every keystroke doesn't flood the log.
/// </para>
/// </remarks>
internal sealed class AddinLanguageEditingService : ILanguageEditing
{
    /// <summary>Who may register what. HexIDE's own table; a test gives its own owners grants here.</summary>
    internal IReadOnlyList<RegistrationGrant> Grants { get; init; } = RegistrationGrants.All;

    private readonly Lock gate = new();
    private readonly Dictionary<string, Registration> served = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    public IConditionalCodeReader ConditionalCodeFor(string? languageId) => new ConditionalCodeReader(this, languageId);

    public IBracketReader BracketsFor(string? languageId) => new BracketReader(this, languageId);

    public IDeclarationReader DeclarationsFor(string? languageId) => new DeclarationReader(this, languageId);

    internal IDisposable Register(LanguageEditing editing, AddinOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(editing);
        if (RegistrationGrants.For(origin, Grants) is not { LanguageEditing: true })
        {
            Log.Warning("Language editing: refused {Addin} ({AddinId}): it holds no grant to register a language's editing helpers",
                origin.Title, origin.Id);
            throw new InvalidOperationException($"{origin.Title} isn't one HexIDE lets register a language's editing helpers.");
        }

        // A copy, so a list the owner changes later can't change what it serves.
        string[] languages = [.. editing.LanguageIds ?? []];
        if (languages.Length == 0 || languages.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"{origin.Title}'s editing helpers name no language, or a blank one.", nameof(editing));

        Registration registration;
        lock (gate)
        {
            foreach (var language in languages)
                if (served.TryGetValue(language, out var held))
                {
                    Log.Warning("Language editing: refused {Addin} ({AddinId}) for {Language}: {Owner} ({OwnerId}) serves it already",
                        origin.Title, origin.Id, language, held.Owner.Title, held.Owner.Id);
                    throw new InvalidOperationException(
                        $"{origin.Title} can't register editing helpers for {language}: {held.Owner.Title} ({held.Owner.Id}) serves it already.");
                }
            registration = new Registration(this, origin, languages, editing.ConditionalCode, editing.Brackets, editing.Declarations);
            foreach (var language in languages)
                served[language] = registration;
        }
        Log.Information("Language editing: {Addin} ({AddinId}) serves {Languages}", origin.Title, origin.Id, languages);
        Changed?.Invoke();
        return registration;
    }

    private Registration? Find(string? languageId)
    {
        if (string.IsNullOrEmpty(languageId)) return null;
        lock (gate)
            return served.GetValueOrDefault(languageId);
    }

    private void Withdraw(Registration registration)
    {
        lock (gate)
        {
            // Only its own: a handle disposed late never takes out a registration made after it.
            foreach (var language in registration.Languages)
                if (served.TryGetValue(language, out var held) && ReferenceEquals(held, registration))
                    served.Remove(language);
        }
        Log.Information("Language editing: {Addin} ({AddinId}) no longer serves {Languages}",
            registration.Owner.Title, registration.Owner.Id, registration.Languages);
        Changed?.Invoke();
    }

    private sealed class Registration(AddinLanguageEditingService service, AddinOrigin owner, string[] languages,
        IConditionalCodeReader? conditionalCode, IBracketReader? brackets, IDeclarationReader? declarations) : IDisposable
    {
        private int disposed;
        private int conditionalCodeThrew;
        private int bracketsThrew;
        private int declarationsThrew;

        internal AddinOrigin Owner => owner;
        internal string[] Languages => languages;
        internal IConditionalCodeReader? ConditionalCode => conditionalCode;
        internal IBracketReader? Brackets => brackets;
        internal IDeclarationReader? Declarations => declarations;

        internal void ConditionalCodeThrew(Exception ex) => Report(ref conditionalCodeThrew, "which lines an #If leaves out", ex);
        internal void BracketsThrew(Exception ex) => Report(ref bracketsThrew, "where the parentheses are", ex);
        internal void DeclarationsThrew(Exception ex) => Report(ref declarationsThrew, "what a module declares", ex);

        private void Report(ref int threw, string question, Exception ex)
        {
            if (Interlocked.Exchange(ref threw, 1) == 0)
                Log.Warning(ex, "Language editing: {Addin} ({AddinId}) threw when asked {Question}; it's treated as answering nothing, "
                    + "and later throws are logged at debug", owner.Title, owner.Id, question);
            else
                Log.Debug(ex, "Language editing: {Addin} ({AddinId}) threw again when asked {Question}", owner.Title, owner.Id, question);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                service.Withdraw(this);
        }
    }

    // Each looks the language up on every call, so it follows registrations and withdrawals, and calls the helper outside the
    // registry's lock.
    private sealed class ConditionalCodeReader(AddinLanguageEditingService service, string? languageId) : IConditionalCodeReader
    {
        public IReadOnlyList<LineRange> InactiveLines(string source, ConditionalConstants constants)
        {
            if (service.Find(languageId) is not { ConditionalCode: { } reader } registration) return [];
            try
            {
                return reader.InactiveLines(source, constants) ?? [];
            }
            catch (Exception ex)
            {
                registration.ConditionalCodeThrew(ex);
                return [];
            }
        }
    }

    private sealed class BracketReader(AddinLanguageEditingService service, string? languageId) : IBracketReader
    {
        public IReadOnlyList<BracketMark> Brackets(string source)
        {
            if (service.Find(languageId) is not { Brackets: { } reader } registration) return [];
            try
            {
                return reader.Brackets(source) ?? [];
            }
            catch (Exception ex)
            {
                registration.BracketsThrew(ex);
                return [];
            }
        }
    }

    private sealed class DeclarationReader(AddinLanguageEditingService service, string? languageId) : IDeclarationReader
    {
        public IReadOnlyList<DocumentSymbol> Declarations(string source)
        {
            if (service.Find(languageId) is not { Declarations: { } reader } registration) return [];
            try
            {
                return reader.Declarations(source) ?? [];
            }
            catch (Exception ex)
            {
                registration.DeclarationsThrew(ex);
                return [];
            }
        }
    }
}
