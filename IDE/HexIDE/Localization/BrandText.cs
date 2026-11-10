using System.Text.RegularExpressions;

namespace HexIDE.Localization;

/// <summary>
/// Puts the installation's product name into the IDE's own strings, in every language (#2042).
/// </summary>
/// <remarks>
/// <para>
/// <b>The literal product name in a pack is the template argument.</b> A brand author then needs no language packs
/// to rename the product, and a string added tomorrow is covered without anybody remembering to mark it up. Packs
/// keep reading as ordinary text, and the placeholder checks on translations see no new token.
/// </para>
/// <para>
/// This runs over the IDE's own strings only. Documents, program messages, protocol text and project files never
/// pass through here.
/// </para>
/// </remarks>
internal static partial class BrandText
{
    internal const string UpstreamCreditsKey = "Str.Dialog.About.Credits.Thanks";

    /// <summary>
    /// Strings that are statements about HexIDE itself, and stay true only if they keep saying so: who built it,
    /// whose signing keys these are, and which HexIDE version an add-in asks for.
    /// </summary>
    private static readonly HashSet<string> UpstreamKeys = new(StringComparer.Ordinal)
    {
        UpstreamCreditsKey,
        "Str.Dialog.About.Provenance",
        "Str.Dialog.About.SigningKeys.RootKey",
        "Str.Dialog.About.SigningKeys.FirstPartyKey",
        "Str.Addins.Incompatible",
        "Str.TranslationEditor.ProtectedCredits",
    };

    // Not when a dot and a letter follow: that is a file or assembly name, HexIDE.Standalone for one, and those keep
    // their names in every distribution. A dot that ends a sentence has no letter after it.
    [GeneratedRegex(@"HexIDE(?!\.\w)", RegexOptions.CultureInvariant)]
    private static partial Regex ProductName();

    public static void Apply(Dictionary<string, string> strings, string productName, bool hasRepository = false)
    {
        foreach (var key in new List<string>(strings.Keys))
        {
            if (UpstreamKeys.Contains(key) || (key == "Str.Menu.Help.ProductOnWeb" && !hasRepository))
                continue;
            strings[key] = ProductName().Replace(strings[key], _ => productName);
        }
    }
}
