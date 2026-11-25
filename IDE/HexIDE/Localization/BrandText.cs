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
    internal static IEnumerable<string> LanguageChain(ILocalizationService localization) =>
        [localization.ResolvedLanguage, LocalizationService.NeutralLanguageOf(localization.ResolvedLanguage), "en"];

    internal const string UpstreamCreditsKey = "Str.Dialog.About.Credits.Thanks";

    /// <summary>
    /// The lines that say whose signing keys these are: About's provenance and fingerprint labels, and the trust
    /// inspector's root, trust-anchor and first-party rows. No override tier may relabel them, whether a profile (a
    /// brand's <c>defaultProfile</c> included), the user's translations or a later package tier. Otherwise a
    /// product built on HexIDE could present HexIDE's keys as its own beside the real fingerprints (#4060).
    /// </summary>
    internal static readonly IReadOnlySet<string> TrustLabelKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "Str.Dialog.About.Provenance",
        "Str.Dialog.About.SigningKeys.Header",
        "Str.Dialog.About.SigningKeys.RootKey",
        "Str.Dialog.About.SigningKeys.FirstPartyKey",
        "Str.Dialog.TrustChain.Root",
        "Str.Dialog.TrustChain.TrustAnchor",
        "Str.Dialog.TrustChain.FirstPartyBadge",
        "Str.Dialog.TrustChain.CompareHint",
    };

    /// <summary>
    /// What tells a person whether to trust an add-in: the consent dialog's verdicts, warnings and its Allow and Block
    /// buttons, the trust inspector's revocation and signing lines, the unsigned add-ins notice, and Options → Add-Ins'
    /// publisher, trust, consent and status verdicts. A profile may not change them, so a brand's <c>defaultProfile</c>
    /// or one a package installs can't make an unsigned add-in read as verified (#4121). The user's own translations
    /// still apply, because they change only that person's IDE. A package tier will refuse a third party's and accept
    /// HexIDE's own first-party translations when it exists.
    /// </summary>
    internal static readonly IReadOnlySet<string> TrustVerdictKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "Str.Dialog.AddinConsent.Title",
        "Str.Dialog.AddinConsent.Vm.Title",
        "Str.Dialog.AddinConsent.UnsignedWarning",
        "Str.Dialog.AddinConsent.AccessWarning",
        "Str.Dialog.AddinConsent.Allow",
        "Str.Dialog.AddinConsent.Block",
        "Str.Dialog.AddinConsent.Vm.NoVerifiableIdentity",
        "Str.Dialog.AddinConsent.Vm.VerifiedPublisher",
        "Str.Dialog.AddinConsent.Vm.Unsigned",
        "Str.Dialog.AddinConsent.Vm.UnrecognisedRoot",
        "Str.Dialog.TrustChain.RevokedBanner",
        "Str.Dialog.TrustChain.SignedBy",
        "Str.MainWindow.UnsignedAddinsNotice",
        "Str.Options.Addin.PublisherUnsigned",
        "Str.Options.Addin.PublisherVerified",
        "Str.Options.Addin.TrustUnsigned",
        "Str.Options.Addin.TrustUnverified",
        "Str.Options.Addin.TrustUnrecognisedRoot",
        "Str.Options.Addin.ConsentAllowed",
        "Str.Options.Addin.ConsentBlocked",
        "Str.Options.Addin.ConsentAwaiting",
        "Str.Options.Addin.Status.Untrusted",
        "Str.Options.Addin.Status.DeveloperUnsigned",
        "Str.Options.Addin.Status.AwaitingConsent",
        "Str.Options.Addin.Status.ConsentDenied",
        "Str.Options.Addin.Status.Revoked",
        "Str.Options.Addin.RevokeConsent",
    };

    /// <summary>Every key that always shows the shipped pack's text: HexIDE's credits (#2746) and its trust labels.</summary>
    internal static readonly IReadOnlySet<string> ProtectedKeys =
        new HashSet<string>([UpstreamCreditsKey, .. TrustLabelKeys], StringComparer.Ordinal);

    /// <summary>
    /// Strings that are statements about HexIDE itself, and stay true only if they keep saying so: who built it,
    /// whose signing keys these are, which HexIDE version an add-in asks for, and what its engine implements.
    /// </summary>
    internal static readonly HashSet<string> UpstreamKeys = new(StringComparer.Ordinal)
    {
        UpstreamCreditsKey,
        "Str.Dialog.About.Provenance",
        "Str.Dialog.About.SigningKeys.RootKey",
        "Str.Dialog.TrustChain.Root",
        "Str.Dialog.TrustChain.CompareHint",
        "Str.Dialog.About.SigningKeys.FirstPartyKey",
        "Str.Addins.Incompatible",
        "Str.TranslationEditor.ProtectedCredits",
        "Str.MemberOrigin.HexIde",
        "Str.Dialog.Components.NotImplemented",
        "Str.Designer.Unmodelled.NotSupported",
        "Str.ProjectRunner.NotImplementedYet",
        "Str.Dialog.UnfaithfulSave.Body.One",
        "Str.Dialog.UnfaithfulSave.Body.Many",
        "Str.Designer.ReadOnly.Banner",
        "Str.CodeEditor.FileNotSaved.Banner",
        "Str.ProjectLoad.ActiveXDocuments",
        "Str.TranslationEditor.ProtectedTrust",
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
