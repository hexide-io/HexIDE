using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HexIDE.IDE;

namespace HexIDE.Controls;

internal static class DiagnosticContentView
{
    internal static Control Render(LspMarker marker, IEditorService editors)
    {
        var panel = new StackPanel { Spacing = 4, MaxWidth = 700 };
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(marker.Source) ? marker.Message : $"{marker.Source}: {marker.Message}",
            TextWrapping = TextWrapping.Wrap
        });
        foreach (var related in marker.RelatedInformation ?? [])
        {
            var location = related.Location;
            var line = location.Range.Start.Line + 1;
            var link = new HyperlinkButton
            {
                Content = $"{editors.DocumentNameForUri(location.Uri) ?? location.Uri}:{line}",
                Padding = new Thickness(0)
            };
            link.Click += (_, _) => editors.NavigateTo(location.Uri, line, location.Range.Start.Character + 1);
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(link);
            row.Children.Add(new TextBlock { Text = related.Message, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(row);
        }
        return panel;
    }
}
