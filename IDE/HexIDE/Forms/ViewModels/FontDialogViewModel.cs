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

    public IReadOnlyList<string> FontFamilies { get; }
    public IReadOnlyList<string> FontStyles { get; } = ["Regular", "Bold", "Italic", "Bold Italic"];
    public IReadOnlyList<double> FontSizes { get; } = [8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];

    public FontStyle PreviewFontStyle =>
        _selectedStyle.Contains("Italic") ? FontStyle.Italic : FontStyle.Normal;

    public FontWeight PreviewFontWeight =>
        _selectedStyle.Contains("Bold") ? FontWeight.Bold : FontWeight.Normal;

    public event Action<FontDialogResult?>? AcceptRequested;
    public event Action? CancelRequested;

    public FontDialogViewModel(FontDialogResult? initial)
    {
        FontFamilies = FontManager.Current.SystemFonts
            .Select(f => f.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _selectedFamily = initial?.Family.Name ?? "Arial";
        _selectedSize   = initial?.Size ?? 12;
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
