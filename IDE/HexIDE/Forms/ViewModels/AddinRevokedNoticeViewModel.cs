using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Forms.ViewModels;

/// <summary>
/// Says that HexIDE has revoked add-ins whose code is loaded, and offers to close HexIDE, which finishes removing them
/// (#5577). Close goes through the normal close, with its prompts to save; Later, Escape and the close box leave HexIDE
/// open. The add-ins' menus, commands and windows are already gone by the time this shows.
/// </summary>
public sealed partial class AddinRevokedNoticeViewModel(IReadOnlyList<string> addinTitles, ILocalizationService localization) : IDialog
{
    /// <summary>The most characters of one add-in's title the notice shows.</summary>
    internal const int MaxTitleLength = 80;

    public string Title => localization.GetString("Str.Dialog.AddinRevoked.Title");
    public bool CanResize => false;
    public event Action<bool>? CloseRequested;

    /// <summary>The titles as shown. Each comes from a revoked add-in's own manifest, the add-in most likely to be hostile.</summary>
    public IReadOnlyList<string> AddinTitles { get; } = [.. addinTitles.Select(Shown)];

    public string Message => string.Format(localization.GetString("Str.Dialog.AddinRevoked.Body"),
        string.Join(", ", AddinTitles));

    [RelayCommand] private void CloseIde() => CloseRequested?.Invoke(true);
    [RelayCommand] private void Later() => CloseRequested?.Invoke(false);

    /// <summary>
    /// A title cut to <see cref="MaxTitleLength"/> characters, with an ellipsis when cut. A control, format (such as a
    /// right-to-left override), line- or paragraph-separator character, or broken UTF-16, shows as <c>?</c>, so a title
    /// can't break the notice's lines or reorder its text. The joiners U+200C and U+200D stay, as add-in titles allow them.
    /// </summary>
    internal static string Shown(string title)
    {
        var text = new StringBuilder();
        var count = 0;
        foreach (var rune in title.EnumerateRunes())
        {
            if (count == MaxTitleLength)
                return text.Append('…').ToString();
            text.Append(Refused(rune) ? "?" : rune.ToString());
            count++;
        }
        return text.ToString();
    }

    private static bool Refused(Rune rune) => rune == Rune.ReplacementChar || Rune.IsControl(rune)
        || (rune.Value is not 0x200c and not 0x200d && Rune.GetUnicodeCategory(rune) is UnicodeCategory.Format
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);
}
