using System;
using System.Collections.Generic;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.Addins;
using HexIDE.IDE;

namespace HexIDE.Forms.ViewModels;

/// <summary>One rendered row of the trust chain. <see cref="Fingerprint"/> is the copyable ground truth.</summary>
public sealed record TrustRow(string Heading, string Primary, string? Id, string? Fingerprint,
                              bool ShowConnector, bool FirstPartyBadge);

/// <summary>
/// Read-only view of an add-in's verified signing chain (publisher → marketplace → HexIDE root) with key
/// fingerprints a power user can compare against an out-of-band published value. Works <b>embedded</b> (the
/// Options "Trust" expander) or as a <b>modal</b> (the consent dialog's "Trust details…"). It renders only
/// IDE-computed facts — never publisher-asserted decoration — which is what makes it safe at the consent
/// moment, the deliberate inverse of the publisher logo.
/// </summary>
public partial class TrustChainViewModel : ObservableObject, IDialog
{
    public string Title => "Add-in trust chain";
    public bool CanResize => false;
    public event Action<bool>? CloseRequested;

    public TrustChainViewModel(TrustChain? chain, string? unavailableReason, bool isRevoked, bool asDialog)
    {
        ShowCloseButton = asDialog;
        IsRevoked = isRevoked;
        UnavailableReason = unavailableReason;

        if (chain is not null)
        {
            HasChain = true;
            IsFirstParty = chain.IsFirstParty;
            Rows =
            [
                new TrustRow("Build", $"{Dash(chain.BuildVersion)}", null, Group(chain.BuildHash), false, false),
                new TrustRow("Publisher", Dash(chain.Publisher.Label), NullIfEmpty(chain.Publisher.Id),
                             chain.Publisher.KeyFingerprint, true, chain.IsFirstParty),
                new TrustRow("Marketplace", Dash(chain.Intermediate.Label), NullIfEmpty(chain.Intermediate.Id),
                             chain.Intermediate.KeyFingerprint, true, false),
                new TrustRow("HexIDE root", "Baked-in trust anchor", null, chain.Root.KeyFingerprint, true, false),
            ];
        }
    }

    public bool ShowCloseButton { get; }
    public bool HasChain { get; }
    public bool IsFirstParty { get; }
    public bool IsRevoked { get; }
    public string? UnavailableReason { get; }
    public bool HasUnavailableReason => !string.IsNullOrEmpty(UnavailableReason);

    /// <summary>The compare-it-yourself hint shown beneath the chain.</summary>
    public string CompareHint =>
        "Compare the HexIDE root and publisher fingerprints against the values published in HexIDE's docs " +
        "(TRUST.md) and by the publisher. HexIDE verifies the chain; only you can confirm a key is who it claims.";

    public IReadOnlyList<TrustRow> Rows { get; } = [];

    [RelayCommand] private void Close() => CloseRequested?.Invoke(true);

    private static string Dash(string s) => string.IsNullOrWhiteSpace(s) ? "—" : s;
    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    // Group plain lower-case hex (the manifest hash) the same way as a key fingerprint for readability.
    private static string Group(string hex)
    {
        var sb = new StringBuilder(hex.Length + hex.Length / 4);
        for (var i = 0; i < hex.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(hex[i]);
        }
        return sb.ToString();
    }
}
