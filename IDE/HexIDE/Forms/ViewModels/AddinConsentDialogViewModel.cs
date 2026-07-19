using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Forms.ViewModels;

/// <summary>
/// First-load consent prompt for one add-in. The user Allows or Blocks; closing/Escape resolves to
/// Block (the safe default). A verified add-in shows its publisher; an unsigned (developer-mode) add-in
/// shows a distinct warning instead. A subtle "Trust details…" affordance opens the read-only trust-chain
/// inspector (key fingerprints) — the ClickOnce "examine the certificate" gesture, at the decision moment.
/// </summary>
public partial class AddinConsentDialogViewModel : ObservableObject, IDialog
{
    public string Title => _localization.GetString("Str.Dialog.AddinConsent.Vm.Title");
    public bool CanResize => false;
    public event Action<bool>? CloseRequested;

    private readonly TrustChain? _chain;
    private readonly IWindowManager? _windowManager;
    private readonly ILocalizationService _localization;

    public AddinConsentDialogViewModel(string addinTitle, string version, string author, string? verifiedPublisher,
                                       ILocalizationService localization,
                                       TrustChain? chain = null, IWindowManager? windowManager = null)
    {
        _localization = localization;
        AddinTitle = string.IsNullOrEmpty(addinTitle) ? localization.GetString("Str.Dialog.AddinConsent.Vm.UnnamedAddin") : addinTitle;
        Version = version;
        Author = author;
        VerifiedPublisher = verifiedPublisher ?? "";
        IsVerified = !string.IsNullOrEmpty(verifiedPublisher);
        _chain = chain;
        _windowManager = windowManager;
    }

    /// <summary>The "Trust details…" affordance is offered only when a window manager is available to host
    /// the inspector modal (i.e. in the running IDE, not in isolated VM unit tests).</summary>
    public bool CanViewTrustDetails => _windowManager is not null;

    [RelayCommand]
    private async Task ViewTrustDetails()
    {
        if (_windowManager is null)
            return;
        var unavailable = _chain is null ? _localization.GetString("Str.Dialog.AddinConsent.Vm.NoVerifiableIdentity") : null;
        await _windowManager.ShowDialog(new TrustChainViewModel(_chain, unavailable, isRevoked: false, asDialog: true));
    }

    public string AddinTitle { get; }
    public string Version { get; }
    public string Author { get; }
    public string VerifiedPublisher { get; }
    public bool IsVerified { get; }
    public bool IsUnsigned => !IsVerified;

    public string PublisherLine => IsVerified
        ? string.Format(_localization.GetString("Str.Dialog.AddinConsent.Vm.VerifiedPublisher"), VerifiedPublisher)
        : _localization.GetString("Str.Dialog.AddinConsent.Vm.Unsigned");

    [RelayCommand] private void Allow() => CloseRequested?.Invoke(true);
    [RelayCommand] private void Block() => CloseRequested?.Invoke(false);
}
