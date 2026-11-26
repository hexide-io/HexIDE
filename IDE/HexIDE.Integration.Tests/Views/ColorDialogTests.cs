using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using HexIDE.Dialogs;

namespace HexIDE.Integration.Tests.Views;

public class ColorDialogTests
{
    [AvaloniaFact]
    public void Managed_chooser_palette_selects_and_Cancel_answers_null()
    {
        var dialog = new ColorDialog(0);
        var palette = dialog.FindControl<WrapPanel>("PART_Palette")!;
        ((Button)palette.Children[2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        dialog.FindControl<TextBox>("PART_Rgb")!.Text.Should().Be("FF0000");
        int? answer = 255;
        dialog.ColorRequest += c => answer = c;
        dialog.FindControl<Button>("PART_Cancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        answer.Should().BeNull();
    }

    [AvaloniaFact]
    public void Managed_chooser_edits_RGB_previews_it_and_returns_OLE_RGB()
    {
        var dialog = new ColorDialog(255);
        var rgb = dialog.FindControl<TextBox>("PART_Rgb")!;
        var ok = dialog.FindControl<Button>("PART_Ok")!;
        rgb.Text.Should().Be("FF0000");
        rgb.Text = "123456";
        var preview = (SolidColorBrush)dialog.FindControl<Border>("PART_Preview")!.Background!;
        preview.Color.Should().Be(Color.FromRgb(0x12, 0x34, 0x56));
        int? chosen = null;
        dialog.ColorRequest += c => chosen = c;
        ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        chosen.Should().Be(0x563412);
        rgb.Text = "invalid";
        ok.IsEnabled.Should().BeFalse();
    }
}
