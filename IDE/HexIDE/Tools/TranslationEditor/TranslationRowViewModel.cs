using System.Windows.Input;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools.TranslationEditor;

/// <summary>
/// One row of the local translation editor grid: a single <c>Str.*</c> key with its reference (From) value
/// and its editable target (To) value. The row is deliberately <b>dumb</b> — it holds no services and runs
/// no commit logic. The parent <see cref="TranslationEditorViewModel"/> subscribes to
/// <see cref="ToValue"/> changes (via <c>PropertyChanged</c>) and performs all override write/clear/validate
/// work, so the row never needs to know about <c>IUserTranslationsService</c> or the active locale ids.
/// </summary>
public partial class TranslationRowViewModel
{
    /// <summary>The localization key (e.g. <c>Str.Menu.File.Open</c>).</summary>
    public string Key { get; }

    /// <summary>The raw grouping bucket: the first segment after <c>Str.</c> (e.g. <c>Menu</c>), or
    /// <c>Other</c> when the key has no such segment. Maps to a localized <see cref="AreaLabel"/>.</summary>
    public string Area { get; }

    /// <summary>The localized group-header label for <see cref="Area"/> (e.g. <c>Menu</c> → "Menus"),
    /// assigned and refreshed by the parent VM. The grid groups on this so headers read in the IDE language.</summary>
    [Notify] private string areaLabel = string.Empty;

    /// <summary>The read-only reference value from the From locale.</summary>
    [Notify] private string fromValue = string.Empty;

    /// <summary>The editable target value. The grid commits edits here; the parent VM listens for changes.</summary>
    [Notify] private string toValue = string.Empty;

    /// <summary>True when the To locale carries a user override for this key — drives the bold To cell.</summary>
    [Notify] private bool isOverridden;

    /// <summary>True when the From reference is secretly English (From ≠ en and the key is not in From's own
    /// shipped chain) — drives the red highlight.</summary>
    [Notify] private bool isRedFallback;

    /// <summary>Multi-line inheritance-chain text shown on the row's hover tooltip.</summary>
    [Notify] private string chainTooltip = string.Empty;

    /// <summary>True when the last edit was rejected (e.g. broke the canonical placeholder set).</summary>
    [Notify] private bool hasValidationError;

    /// <summary>Per-row "reset this string" command, assigned by the parent VM (reverts this key's override).
    /// Held here so the To-cell context menu can bind it directly against the row's own DataContext.</summary>
    public ICommand? ResetCommand { get; set; }

    public TranslationRowViewModel(string key)
    {
        Key = key;
        Area = AreaOf(key);
    }

    private static string AreaOf(string key)
    {
        // "Str.Menu.File.Open" → "Menu"; anything without a Str.<area> shape → "Other".
        var parts = key.Split('.');
        if (parts.Length >= 2 && parts[0] == "Str")
            return parts[1];
        return "Other";
    }
}
