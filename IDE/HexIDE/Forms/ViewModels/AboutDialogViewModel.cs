using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Forms.ViewModels;

public class AboutDialogViewModel
{
    public string Title { get; }
    public string SubTitle { get; }
    public string Copyright { get; }
    public string Provenance { get; }
    public bool HasProvenance => Provenance.Length > 0;
    public IReadOnlyList<string> Credits { get; }
    public Bitmap? Icon { get; }

    /// <summary>The canonical add-in trust-anchor fingerprints, echoed here so a user can confirm out of
    /// band that this binary trusts the genuine HexIDE root and first-party key.</summary>
    public string? RootFingerprint { get; } = TrustAnchors.RootFingerprint;
    public string? FirstPartyFingerprint { get; } = TrustAnchors.FirstPartyFingerprint;
    public bool HasTrustAnchors => RootFingerprint is not null;

    public event Action? CloseRequested;

    public AboutDialogViewModel(AboutDialogOptions options, ILocalizationService localization)
    {
        Title = options.Title;
        SubTitle = options.SubTitle;
        Copyright = options.Copyright;
        Provenance = options.Provenance;
        Icon = options.Icon;
        // Append-only by design. Jen (#2746): "a whitelabelled IDE should _not_ get an API to remove
        // HexIDE credits, only to add to them. They can obviously strip that by forking, but we
        // shouldn't make it a trivial decision."
        var credits = new List<string> { localization.GetString("Str.Dialog.About.Credits.Thanks") };
        credits.AddRange(options.AdditionalCredits);
        Credits = credits.AsReadOnly();
    }

    public void Close() => CloseRequested?.Invoke();
}
