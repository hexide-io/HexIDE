using System;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.Addins;
using HexIDE.Localization;
using HexIDE.Runtime.Serialization;
using Serilog;

namespace HexIDE.Forms.ViewModels.Options;

/// <summary>
/// Add-Ins &#8594; {add-in}: a fixed details header (title / description / version / author / status)
/// plus an Activate/Deactivate toggle. Unlike the first-party settings pages, enable/disable is
/// persisted immediately via <see cref="IAddinRegistry"/> (independent of the dialog's OK/Cancel)
/// and takes effect on the next IDE restart.
/// </summary>
public partial class AddinOptionsPageViewModel : ObservableObject, IDisposable
{
    private readonly ILocalizationService _localization;
    private readonly AddinInfo _info;
    private readonly AddinStatus _trustStatus;

    private readonly IAddinRegistry _registry;
    private readonly string _assemblyPath;
    private readonly string _manifestHash;
    private readonly string _addinId;
    private readonly Func<string, Task<bool>>? _confirmWithdrawal;

    /// <param name="confirmWithdrawal">
    /// Asked before the add-in is turned off or its consent withdrawn, with its id: false keeps it as it is. It's how forms the
    /// add-in writes are saved, or let go, while it can still write them.
    /// </param>
    public AddinOptionsPageViewModel(IAddinRegistry registry, AddinInfo info, Func<object>? contributedFactory = null,
        Func<string, Task<bool>>? confirmWithdrawal = null, ILocalizationService? localization = null)
    {
        _registry = registry;
        _info = info;
        _localization = localization ?? new LocalizationService();
        _addinId = info.Id;
        _confirmWithdrawal = confirmWithdrawal;
        _assemblyPath = info.AssemblyPath;
        _manifestHash = info.ManifestHash;
        Version = string.IsNullOrWhiteSpace(info.Version) ? "—" : info.Version;
        Author = string.IsNullOrWhiteSpace(info.Author) ? "—" : info.Author;
        _status = _trustStatus = registry.GetStatus(_assemblyPath);
        ContributedContent = BuildContributedContent(contributedFactory);
        Logo = DecodeLogo(info);
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public void Dispose() => _localization.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Publisher));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ToggleLabel));
        OnPropertyChanged(nameof(ConsentText));
        OnPropertyChanged(nameof(TrustChain));
    }

    public string Title => string.IsNullOrWhiteSpace(_info.Title) ? Text("Unnamed") : _info.Title;
    public string Description => string.IsNullOrWhiteSpace(_info.Description) ? Text("NoDescription") : _info.Description;
    public string Version { get; }
    public string Author { get; }
    public string Publisher => string.IsNullOrWhiteSpace(_info.Publisher)
        ? Text("PublisherUnsigned")
        : string.Format(Text("PublisherVerified"), _info.Publisher);

    private string Text(string name) => _localization.GetString("Str.Options.Addin." + name);
    private string Format(AddinMessage message) => string.Format(_localization.GetString(message.Key), message.Arguments);

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
    public TrustChainViewModel TrustChain => BuildTrustChain(_info, _trustStatus);

    [ObservableProperty] private AddinStatus _status;
    [ObservableProperty] private bool _restartNoteVisible;
    [ObservableProperty] private bool _consentRevoked;

    /// <summary>An add-in is "enabled" unless it has been explicitly disabled.</summary>
    public bool IsAddinEnabled => Status != AddinStatus.Disabled;
    public string StatusText => Status switch
    {
        AddinStatus.Incompatible => _info.CompatibilityMessage is { } message ? Format(message) : _info.CompatibilityError ?? Text("Status.Incompatible"),
        AddinStatus.Error => _info.LoadError is { } message ? Format(message) : Text("Status.Error"),
        _ => Text("Status." + Status),
    };
    public string ToggleLabel => Text(IsAddinEnabled ? "Deactivate" : "Activate");

    /// <summary>The first-load consent state, derived from the load status.</summary>
    public string ConsentText => Status switch
    {
        AddinStatus.Loaded or AddinStatus.DeveloperUnsigned => Text("ConsentAllowed"),
        AddinStatus.ConsentDenied => Text("ConsentBlocked"),
        AddinStatus.AwaitingConsent => Text("ConsentAwaiting"),
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
    private async Task ToggleEnabled()
    {
        if (IsAddinEnabled && !await MayWithdraw())
            return;
        _registry.SetEnabled(_assemblyPath, !IsAddinEnabled);
        Status = _registry.GetStatus(_assemblyPath);
        RestartNoteVisible = true;
    }

    /// <summary>Drops the recorded consent so the add-in is re-prompted next launch.</summary>
    [RelayCommand]
    private async Task RevokeConsent()
    {
        if (!await MayWithdraw())
            return;
        _registry.RevokeConsent(_manifestHash);
        ConsentRevoked = true;   // hide the button; in-process add-in code unloads on restart
        RestartNoteVisible = true;
    }

    // Asked before the add-in is taken out, so what it writes can be saved while it still can.
    private Task<bool> MayWithdraw() => _confirmWithdrawal is null ? Task.FromResult(true) : _confirmWithdrawal(_addinId);

    // The trust chain for the "Trust" expander. A verified add-in carries its chain; an unsigned/untrusted
    // one carries none, so we show why; a revoked one keeps its (still-valid) chain plus a revoked banner.
    private TrustChainViewModel BuildTrustChain(AddinInfo info, AddinStatus status)
    {
        var unavailable = info.Chain is null
            ? status switch
            {
                AddinStatus.DeveloperUnsigned => Text("TrustUnsigned"),
                AddinStatus.Untrusted => Text("TrustUnverified"),
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
