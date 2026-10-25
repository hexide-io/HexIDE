using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using HexIDE.IDE;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Forms.ViewModels;

public partial class FontDialogViewModel
{
    [Notify] private string _selectedFamily;
    [Notify] private string _selectedStyle;
    [Notify] private double _selectedSize;

    // The previews read the generated properties, never their fields: the generator raises a dependent property's
    // change only for a read of the property, so a preview reading `_selectedSize` was never redrawn.

    /// <summary>The sample's size in pixels: the dialog's sizes are points, 12 drawing at 16 (#910).</summary>
    public double PreviewFontSize => SelectedSize * 96.0 / 72.0;

    /// <summary>The sample's face. Bound straight to the name, the sample drew in the default face: the compiled binding
    /// does not make a FontFamily of a string (#1353).</summary>
    public FontFamily PreviewFontFamily => new(SelectedFamily);

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
        _selectedFamily = initial?.Family.Name ?? "Arial";
        var families = FontManager.Current.SystemFonts
            .Select(f => f.Name)
            .ToList();
        if (!families.Contains(_selectedFamily))
            families.Add(_selectedFamily);
        FontFamilies = families.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        _selectedSize   = initial?.Size ?? 12;
        FontSizes = StandardSizes.Contains(_selectedSize) ? StandardSizes : [.. StandardSizes.Append(_selectedSize).Order()];
        _selectedStyle  = initial != null ? ToStyleName(initial.Style, initial.Weight) : "Regular";
    }

    public void Accept()
    {
        var result = new FontDialogResult(
            new FontFamily(_selectedFamily),
            _selectedStyle.Contains("Italic") ? FontStyle.Italic : FontStyle.Normal,
            _selectedStyle.Contains("Bold")   ? FontWeight.Bold   : FontWeight.Normal,
            _selectedSize);
        AcceptRequested?.Invoke(result);
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
