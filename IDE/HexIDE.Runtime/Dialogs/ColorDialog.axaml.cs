using System;
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace HexIDE.Runtime.Dialogs;

public partial class ColorDialog : UserControl
{
    public event Action<int?>? ColorRequest;
    private int selected;

    public ColorDialog() : this(0) { }

    public ColorDialog(int initial)
    {
        InitializeComponent();
        PART_Rgb.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                UpdateRgb();
        };
        foreach (var rgb in new[] { 0x000000, 0xFFFFFF, 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0xFF00FF, 0x00FFFF })
        {
            var text = rgb.ToString("X6", CultureInfo.InvariantCulture);
            var swatch = new Button { Width = 32, Height = 32, Margin = new Avalonia.Thickness(2), Background = Brush(rgb) };
            AutomationProperties.SetName(swatch, "#" + text);
            swatch.Click += (_, _) => PART_Rgb.Text = text;
            PART_Palette.Children.Add(swatch);
        }
        PART_Rgb.Text = ((initial & 255) << 16 | (initial & 0xFF00) | ((initial >> 16) & 255))
            .ToString("X6", CultureInfo.InvariantCulture);
    }

    private static SolidColorBrush Brush(int rgb) => new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    private void UpdateRgb()
    {
        if (PART_Ok is null || PART_Preview is null)
            return;
        var valid = PART_Rgb.Text is { Length: 6 } text
                    && int.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _);
        PART_Ok.IsEnabled = valid;
        if (!valid)
            return;
        var rgb = int.Parse(PART_Rgb.Text!, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        selected = (rgb & 255) << 16 | (rgb & 0xFF00) | ((rgb >> 16) & 255);
        PART_Preview.Background = Brush(rgb);
    }

    private void OkClick(object? sender, RoutedEventArgs e) => ColorRequest?.Invoke(selected);
    private void CancelClick(object? sender, RoutedEventArgs e) => ColorRequest?.Invoke(null);
}
