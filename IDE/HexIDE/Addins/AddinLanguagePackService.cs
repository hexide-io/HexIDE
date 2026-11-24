using System.Reflection;
using HexIDE.Localization;
using Serilog;

namespace HexIDE.Addins;

/// <summary>
/// Admits an owner's language packs to the IDE's text, as an area of keys it alone holds (#5394).
/// <see cref="IHexIdeHost.RegisterLanguagePacks"/> is its only way in.
/// </summary>
/// <remarks>
/// It decides who, and from where. Only an owner a <see cref="RegistrationGrant"/> names registers anything, and only
/// from its own assembly. <see cref="LanguagePackAreas"/> then checks every key against the grant's families and
/// against what the IDE and other owners already hold, and refuses the whole area for any one.
/// </remarks>
internal sealed class AddinLanguagePackService
{
    /// <summary>Who may register what. HexIDE's own table; a test gives its own owners grants here.</summary>
    internal IReadOnlyList<RegistrationGrant> Grants { get; init; } = RegistrationGrants.All;

    /// <summary>The areas the IDE reads; the running IDE's unless a test gives its own.</summary>
    internal LanguagePackAreas Areas { get; init; } = LanguagePackAreas.Current;

    /// <summary>Adds the packs in <paramref name="folder"/> for <paramref name="origin"/>, whose code in <paramref name="caller"/> asked.</summary>
    /// <exception cref="ArgumentException">
    /// Not an <c>avares://</c> address in <paramref name="caller"/>, or the folder holds no readable <c>en.json</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The owner holds no grant, or a key is refused.</exception>
    internal IDisposable Register(Uri folder, AddinOrigin origin, Assembly caller)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(caller);

        if (RegistrationGrants.For(origin, Grants) is not { } grant || grant.LanguagePackKeys.Count == 0)
        {
            Log.Warning("Language packs: refused {Folder} from {Addin} ({AddinId}): it holds no grant to register language packs",
                folder, origin.Title, origin.Id);
            throw new InvalidOperationException($"{origin.Title} isn't one HexIDE lets register language packs.");
        }

        // A Uri's host is lower case, so the assembly's name is compared without case.
        var assembly = caller.GetName().Name;
        if (!folder.IsAbsoluteUri || folder.Scheme != "avares"
            || !string.Equals(folder.Host, assembly, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"{origin.Title} can register language packs only from its own assembly, as avares://{assembly}/…; it named {folder}.",
                nameof(folder));

        return Areas.Register(origin.Id, origin.Title, folder, grant.CoversLanguagePackKey);
    }
}
