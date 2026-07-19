using System;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.Addins;
using HexIDE.Runtime.Serialization;
using Serilog;

namespace HexIDE.Forms.ViewModels.Options;

/// <summary>
/// Add-Ins &#8594; {add-in}: a fixed details header (title / description / version / author / status)
/// plus an Activate/Deactivate toggle. Unlike the first-party settings pages, enable/disable is
/// persisted immediately via <see cref="IAddinRegistry"/> (independent of the dialog's OK/Cancel)
/// and takes effect on the next IDE restart.
/// </summary>
public partial class AddinOptionsPageViewModel : ObservableObject
{
    private readonly IAddinRegistry _registry;
    private readonly string _assemblyPath;
    private readonly string _manifestHash;

    public AddinOptionsPageViewModel(IAddinRegistry registry, AddinInfo info, Func<object>? contributedFactory = null)
    {
        _registry = registry;
        _assemblyPath = info.AssemblyPath;
        _manifestHash = info.ManifestHash;
        Title = string.IsNullOrWhiteSpace(info.Title) ? "(unnamed add-in)" : info.Title;
        Description = string.IsNullOrWhiteSpace(info.Description) ? "(no description provided)" : info.Description;
        Version = string.IsNullOrWhiteSpace(info.Version) ? "—" : info.Version;
        Author = string.IsNullOrWhiteSpace(info.Author) ? "—" : info.Author;
        Publisher = string.IsNullOrWhiteSpace(info.Publisher)
            ? "Unsigned (developer build)"
            : $"Verified: {info.Publisher}";
        _status = registry.GetStatus(_assemblyPath);
        ContributedContent = BuildContributedContent(contributedFactory);
        Logo = DecodeLogo(info);
        TrustChain = BuildTrustChain(info, _status);
    }

    public string Title { get; }
    public string Description { get; }
    public string Version { get; }
    public string Author { get; }
    public string Publisher { get; }

    /// <summary>The publisher's logo, or null if the add-in shipped none / it failed the safety gate.
    /// Sourced from the verifier-sanitized <see cref="AddinInfo.LogoPath"/>, so the file is always a
    /// hash-verified, in-package PNG; <see cref="SafeImageDecoder"/> bounds the decode. Informational
    /// only — the logo is publisher-asserted and is deliberately NOT shown at the consent prompt.</summary>
    public Bitmap? Logo { get; }
    public bool HasLogo => Logo is not null;

    /// <summary>The add-in's own settings control, rendered below the header; null if none was contributed.</summary>
    public object? ContributedContent { get; }
    public bool HasContributedContent => ContributedContent is not null;

    /// <summary>The read-only trust-chain inspector for this add-in, embedded behind the "Trust" expander.</summary>
    public TrustChainViewModel TrustChain { get; }

    [ObservableProperty] private AddinStatus _status;
    [ObservableProperty] private bool _restartNoteVisible;
    [ObservableProperty] private bool _consentRevoked;

    /// <summary>An add-in is "enabled" unless it has been explicitly disabled.</summary>
    public bool IsAddinEnabled => Status != AddinStatus.Disabled;
    public string StatusText => Status.ToString();
    public string ToggleLabel => IsAddinEnabled ? "Deactivate" : "Activate";

    /// <summary>The first-load consent state, derived from the load status.</summary>
    public string ConsentText => Status switch
    {
        AddinStatus.Loaded or AddinStatus.DeveloperUnsigned => "Allowed",
        AddinStatus.ConsentDenied => "Blocked",
        AddinStatus.AwaitingConsent => "Awaiting first-load consent",
        _ => "—",
    };

    /// <summary>Revoke is offered only when a first-load decision exists to drop (and not yet revoked
    /// this session).</summary>
    public bool CanRevokeConsent => !ConsentRevoked && !string.IsNullOrEmpty(_manifestHash) && Status is
        AddinStatus.Loaded or AddinStatus.DeveloperUnsigned or AddinStatus.ConsentDenied;

    partial void OnConsentRevokedChanged(bool value) => OnPropertyChanged(nameof(CanRevokeConsent));

    partial void OnStatusChanged(AddinStatus value)
    {
        OnPropertyChanged(nameof(IsAddinEnabled));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ToggleLabel));
        OnPropertyChanged(nameof(ConsentText));
        OnPropertyChanged(nameof(CanRevokeConsent));
    }

    [RelayCommand]
    private void ToggleEnabled()
    {
        _registry.SetEnabled(_assemblyPath, !IsAddinEnabled);
        Status = _registry.GetStatus(_assemblyPath);
        RestartNoteVisible = true;
    }

    /// <summary>Drops the recorded consent so the add-in is re-prompted next launch.</summary>
    [RelayCommand]
    private void RevokeConsent()
    {
        _registry.RevokeConsent(_manifestHash);
        ConsentRevoked = true;   // hide the button; revoke takes effect on next launch
        RestartNoteVisible = true;
    }

    // The trust chain for the "Trust" expander. A verified add-in carries its chain; an unsigned/untrusted
    // one carries none, so we show why; a revoked one keeps its (still-valid) chain plus a revoked banner.
    private static TrustChainViewModel BuildTrustChain(AddinInfo info, AddinStatus status)
    {
        var unavailable = info.Chain is null
            ? status switch
            {
                AddinStatus.DeveloperUnsigned => "No verifiable identity — unsigned developer build.",
                AddinStatus.Untrusted => "Not verified — signature missing or invalid.",
                _ => null,
            }
            : null;
        return new TrustChainViewModel(info.Chain, unavailable, status == AddinStatus.Revoked, asDialog: false);
    }

    // Decode the (verifier-sanitized) logo once. LogoPath is null unless the verifier confirmed it names
    // a listed, in-package, traversal-free file, so the combine cannot escape the package; SafeImageDecoder
    // applies the byte/dimension caps and returns null on anything malformed.
    private static Bitmap? DecodeLogo(AddinInfo info) =>
        string.IsNullOrEmpty(info.LogoPath)
            ? null
            : SafeImageDecoder.DecodeBoundedPngFile(Path.Combine(info.AssemblyPath, info.LogoPath));

    // Build the add-in's contributed control once. An add-in throwing in its factory must not
    // crash the Options dialog — isolate it like the loader isolates Initialize.
    private static object? BuildContributedContent(Func<object>? factory)
    {
        if (factory is null) return null;
        try
        {
            return factory();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AddinOptionsPage: contributed control factory threw — omitting it");
            return null;
        }
    }
}
