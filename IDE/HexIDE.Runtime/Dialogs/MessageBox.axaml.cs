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

    /// <summary>
    /// What closing a box with these buttons answers when no button was pressed, or null where it has no close.
    /// </summary>
    /// <remarks>
    /// Measured on vb6.exe, posting WM_CLOSE and SC_CLOSE to each kind: OK alone answers vbOK; OK/Cancel, Yes/No/Cancel
    /// and Retry/Cancel answer vbCancel; Yes/No and Abort/Retry/Ignore stay open, because they have no close. That is
    /// Windows' own message box, and the same holds for every box the IDE shows.
    /// </remarks>
    public static MessageBoxResult? AnswerOnClose(MessageBoxButtons buttons) => buttons switch
    {
        MessageBoxButtons.Ok => MessageBoxResult.Ok,
        MessageBoxButtons.OkCancel or MessageBoxButtons.YesNoCancel or MessageBoxButtons.RetryCancel
            => MessageBoxResult.Cancel,
        _ => null,
    };

    /// <summary>
    /// Whether the window hosting a box should refuse a close, as Windows refuses one for a box with no close.
    /// </summary>
    /// <remarks>
    /// Only a person's close is refused: the title-bar button or Alt+F4. A close HexIDE makes itself still goes
    /// through, which is how ending the run or shutting down reaches a box that is still open, and so does one
    /// forced by the owner closing or the session ending.
    /// </remarks>
    public static bool RefusesClose(MessageBoxButtons buttons, bool isProgrammatic, WindowCloseReason reason) =>
        AnswerOnClose(buttons) is null && !isProgrammatic && reason == WindowCloseReason.WindowClosing;

    /// <summary>Makes <paramref name="window"/>, which hosts this box, refuse a close the box does not have.</summary>
    public void GuardClose(Window window) =>
        window.Closing += (_, e) =>
        {
            if (RefusesClose(Buttons, e.IsProgrammatic, e.CloseReason))
                e.Cancel = true;
        };

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
