using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using HexIDE.IDE;

namespace HexIDE.Runtime.Dialogs;

public partial class MessageBox : UserControl
{
    public string Text { get; set; } = "";
    public MessageBoxButtons Buttons { get; set; } = MessageBoxButtons.Ok;
    public MessageBoxIcon Icon { get; set; } = MessageBoxIcon.None;

    public event Action<MessageBoxResult>? AcceptRequest;

    public MessageBox()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (Icon != MessageBoxIcon.None)
            PART_Content.Children.Add(BuildIconElement());

        PART_Content.Children.Add(new TextBlock
        {
            Text = Text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 360,
            VerticalAlignment = VerticalAlignment.Center
        });

        foreach (var (label, result) in GetButtonDefs())
        {
            var captured = result;
            var btn = new Button { Content = label, MinWidth = 75 };
            btn.Click += (_, _) => AcceptRequest?.Invoke(captured);
            PART_Buttons.Children.Add(btn);
        }
    }

    private Control BuildIconElement()
    {
        var (symbol, bg) = Icon switch
        {
            MessageBoxIcon.Error       => ("✕", new SolidColorBrush(Colors.Crimson)),
            MessageBoxIcon.Warning     => ("!", new SolidColorBrush(Color.FromRgb(220, 120, 0))),
            MessageBoxIcon.Question    => ("?", new SolidColorBrush(Colors.RoyalBlue)),
            MessageBoxIcon.Information => ("i", new SolidColorBrush(Colors.SteelBlue)),
            _                          => ("", new SolidColorBrush(Colors.Gray))
        };
        return new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = bg,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = symbol,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 16,
                FontWeight = FontWeight.Bold
            }
        };
    }

    private IEnumerable<(string Label, MessageBoxResult Result)> GetButtonDefs() => Buttons switch
    {
        MessageBoxButtons.OkCancel =>
            [("OK", MessageBoxResult.Ok), ("Cancel", MessageBoxResult.Cancel)],
        MessageBoxButtons.AbortRetryIgnore =>
            [("Abort", MessageBoxResult.Abort), ("Retry", MessageBoxResult.Retry), ("Ignore", MessageBoxResult.Ignore)],
        MessageBoxButtons.YesNoCancel =>
            [("Yes", MessageBoxResult.Yes), ("No", MessageBoxResult.No), ("Cancel", MessageBoxResult.Cancel)],
        MessageBoxButtons.YesNo =>
            [("Yes", MessageBoxResult.Yes), ("No", MessageBoxResult.No)],
        MessageBoxButtons.RetryCancel =>
            [("Retry", MessageBoxResult.Retry), ("Cancel", MessageBoxResult.Cancel)],
        _ => [("OK", MessageBoxResult.Ok)]
    };
}
