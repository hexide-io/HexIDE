using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Avalonia.Media;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinTypes;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Forms.ViewModels;

public partial class FontDialogViewModel
{
    [Notify] private string _selectedFamily;
    [Notify] private string _selectedStyle;
    [Notify] private string _familyText;
    [Notify] private string _styleText;
    [Notify] private double _selectedSize;
    [Notify] private double? _selectedSizeItem;
    [Notify] private string _sizeText;
    [Notify] private int _selectedScriptIndex;
    private readonly double _initialSize;
    private readonly int? _initialCharset;
    private bool _sizeEdited;
    private bool _synchronizingSize;
    [Notify] private bool _underline;
    [Notify] private bool _strikethrough;

    public TextDecorationCollection PreviewTextDecorations
    {
        get
        {
            var decorations = new TextDecorationCollection();
            if (Underline) decorations.AddRange(TextDecorations.Underline);
            if (Strikethrough) decorations.AddRange(TextDecorations.Strikethrough);
            return decorations;
        }
    }

    // The previews read the generated properties, never their fields: the generator raises a dependent property's
    // change only for a read of the property, so a preview reading `_selectedSize` was never redrawn.

    /// <summary>The sample's size in pixels: the dialog's sizes are points, 12 drawing at 16 (#910).</summary>
    public double PreviewFontSize => SelectedSize * 96.0 / 72.0;

    /// <summary>The sample's face. Bound straight to the name, the sample drew in the default face: the compiled binding
    /// does not make a FontFamily of a string (#1353).</summary>
    public FontFamily PreviewFontFamily => new(SelectedFamily);

    // Runtime CommonDialog's choice contract does not yet carry effects (#1084).
    public bool CanEditEffects { get; }
    public bool CanEditCharset => _initialCharset != null;

    public IReadOnlyList<string> FontFamilies { get; }
    public IReadOnlyList<string> FontStyles { get; } = ["Regular", "Bold", "Italic", "Bold Italic"];
    private static readonly double[] StandardSizes = [8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];

    /// <summary>The sizes offered, with the font's own among them when it is not one of the usual, as 8.25 or 9.6 (#910).</summary>
    public IReadOnlyList<double> FontSizes { get; }

    public FontStyle PreviewFontStyle =>
        SelectedStyle.Contains("Italic") ? FontStyle.Italic : FontStyle.Normal;

    public FontWeight PreviewFontWeight =>
        SelectedStyle.Contains("Bold") ? FontWeight.Bold : FontWeight.Normal;

    public event Action<FontDialogResult?>? AcceptRequested;
    public event Action? CancelRequested;

    public FontDialogViewModel(FontDialogResult? initial)
    {
        CanEditEffects = initial?.Underline != null && initial.Strikethrough != null;
        _underline = initial?.Underline ?? false;
        _strikethrough = initial?.Strikethrough ?? false;
        _selectedFamily = initial?.Family.Name ?? "Arial";
        var families = FontManager.Current.SystemFonts
            .Select(f => f.Name)
            .ToList();
        if (!families.Contains(_selectedFamily))
            families.Add(_selectedFamily);
        FontFamilies = families.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        _selectedSize   = initial?.Size ?? 12;
        _initialSize = _selectedSize;
        _selectedSizeItem = _selectedSize;
        _sizeText = Math.Round(_selectedSize, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
        _initialCharset = initial?.Charset;
        _selectedScriptIndex = _initialCharset is null or 0 ? 0 : -1;
        FontSizes = StandardSizes.Contains(_selectedSize) ? StandardSizes : [.. StandardSizes.Append(_selectedSize).Order()];
        _selectedStyle  = initial != null ? ToStyleName(initial.Style, initial.Weight) : "Regular";
        _familyText = _selectedFamily;
        _styleText = _selectedStyle;
    }

    private void OnFamilyTextChanged()
    {
        var match = FontFamilies.FirstOrDefault(f => string.Equals(f, FamilyText, StringComparison.OrdinalIgnoreCase));
        if (match != null) SelectedFamily = match;
    }

    private void OnStyleTextChanged()
    {
        var match = FontStyles.FirstOrDefault(s => string.Equals(s, StyleText, StringComparison.OrdinalIgnoreCase));
        if (match != null) SelectedStyle = match;
    }

    private void OnSelectedFamilyChanged() => FamilyText = SelectedFamily;
    private void OnSelectedStyleChanged() => StyleText = SelectedStyle;

    private void OnSizeTextChanged()
    {
        if (_synchronizingSize) return;
        _sizeEdited = true;
        _synchronizingSize = true;
        try
        {
            SelectedSizeItem = FontSizes.Select(size => (double?)size).FirstOrDefault(size =>
                size!.Value.ToString(CultureInfo.InvariantCulture) == SizeText);
            if (BigInteger.TryParse(SizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var typed)
                && typed >= 1 && typed <= 2236)
                SelectedSize = VBFontSizing.SnapDialogSize((double)typed);
        }
        finally { _synchronizingSize = false; }
    }

    private void OnSelectedSizeItemChanged()
    {
        if (_synchronizingSize || SelectedSizeItem is not { } size) return;
        _synchronizingSize = true;
        try
        {
            SelectedSize = size;
            SizeText = size.ToString(CultureInfo.InvariantCulture);
            _sizeEdited = false;
        }
        finally { _synchronizingSize = false; }
    }

    // The dialog's 96-dpi measurements are in #4153. An untouched fractional font must survive OK.
    public string? Accept()
    {
        if (SelectedScriptIndex is < -1 or > 0)
            throw new NotImplementedException("Font dialog scripts other than Western are not measured (#4153).");
        var size = SelectedSize;
        if (_sizeEdited)
        {
            if (string.IsNullOrEmpty(SizeText)) size = _initialSize;
            else
            {
                if (!BigInteger.TryParse(SizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var typed))
                    return "Str.Dialog.Font.SizeNotNumber";
                if (typed == 0) size = _initialSize;
                else if (typed < 1 || typed > 2236) return "Str.Dialog.Font.SizeOutOfRange";
                else size = VBFontSizing.SnapDialogSize((double)typed);
            }
        }
        var result = new FontDialogResult(
            new FontFamily(_selectedFamily),
            _selectedStyle.Contains("Italic") ? FontStyle.Italic : FontStyle.Normal,
            _selectedStyle.Contains("Bold")   ? FontWeight.Bold   : FontWeight.Normal,
            size, _underline, _strikethrough,
            CanEditCharset && SelectedScriptIndex == 0 ? 0 : null);
        AcceptRequested?.Invoke(result);
        return null;
    }

    public void Cancel() => CancelRequested?.Invoke();

    private static string ToStyleName(FontStyle style, FontWeight weight) =>
        (weight >= FontWeight.Bold, style == FontStyle.Italic) switch
        {
            (true,  true)  => "Bold Italic",
            (true,  false) => "Bold",
            (false, true)  => "Italic",
            _              => "Regular"
        };
}
