using System;
using Avalonia.Media.Imaging;
using HexIDE.Addins;
using HexIDE.IDE;

namespace HexIDE.Forms.ViewModels;

public class AboutDialogViewModel
{
    public string Title { get; }
    public string SubTitle { get; }
    public string Copyright { get; }
    public Bitmap? Icon { get; }

    /// <summary>The canonical add-in trust-anchor fingerprints, echoed here so a user can confirm out of
    /// band that this binary trusts the genuine HexIDE root and first-party key.</summary>
    public string? RootFingerprint { get; } = TrustAnchors.RootFingerprint;
    public string? FirstPartyFingerprint { get; } = TrustAnchors.FirstPartyFingerprint;
    public bool HasTrustAnchors => RootFingerprint is not null;

    public event Action? CloseRequested;

    public AboutDialogViewModel(AboutDialogOptions options)
    {
        Title = options.Title;
        SubTitle = options.SubTitle;
        Copyright = options.Copyright;
        Icon = options.Icon;
    }

    public void Close() => CloseRequested?.Invoke();
}
