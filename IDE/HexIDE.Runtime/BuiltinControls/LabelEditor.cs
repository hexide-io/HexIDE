using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace HexIDE.Runtime.BuiltinControls;

// Shared by in-box controls: the owner supplies events and the label's location, never a live Text binding.
internal sealed class LabelEditor : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);
    private readonly Control owner;
    private readonly string original;
    private readonly Func<string, Task<string?>> commit;
    private readonly Action close;
    private readonly Action<string> apply;
    private Rect label;
    private double leftOffset;
    private bool ending;
    private bool grown;

    internal LabelEditor(Control owner, string text, Func<string, Task<string?>> commit, Action<string> apply, Action close)
    {
        this.owner = owner;
        original = text;
        this.commit = commit;
        this.apply = apply;
        this.close = close;
        Canvas.SetLeft(this, 0);
        Canvas.SetTop(this, 0);
        Text = text;
        Width = Height = MinWidth = MinHeight = 0;
        Padding = new Thickness(1, 0);
        // A line high, as VB6's box is, which leaves a TextBox's scroll bars no room: shown, one covered the label.
        ScrollViewer.SetVerticalScrollBarVisibility(this, ScrollBarVisibility.Hidden);
        ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Hidden);
        BorderThickness = new Thickness(1);
        BorderBrush = Brushes.Black;
        Background = Brushes.White;
        Foreground = Brushes.Black;
        FontSize = 13;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty && grown) Place();
    }

    internal void Open(Rect bounds, double offset)
    {
        label = bounds;
        leftOffset = offset;
        Place();
        Focus();
        SelectAll();
    }

    private void Place()
    {
        var text = new FormattedText(Text ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight), FontSize, Brushes.Black);
        Canvas.SetLeft(this, label.X - leftOffset);
        Canvas.SetTop(this, label.Y - 1 - (grown ? 2 : 0));
        Width = text.Width + 14;
        Height = label.Height + 1 + (grown ? 4 : 0);
    }

    internal async Task FinishAsync(bool discard = false)
    {
        if (ending) return;
        ending = true;
        try
        {
            var text = Text ?? "";
            if (!discard && text != original && await commit(text) is { } accepted)
                apply(accepted);
        }
        finally { close(); }
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        if (!grown)
        {
            grown = true;
            Place();
        }
        if (e.Key is Key.Enter or Key.Escape or Key.Tab)
        {
            e.Handled = true;
            await FinishAsync(e.Key == Key.Escape);
            owner.Focus();
            if (e.Key == Key.Tab)
                TopLevel.GetTopLevel(owner)?.FocusManager?.TryMoveFocus(e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                    ? NavigationDirection.Previous : NavigationDirection.Next);
            return;
        }
        base.OnKeyDown(e);
    }

    protected override async void OnLostFocus(FocusChangedEventArgs e)
    {
        await FinishAsync();
        base.OnLostFocus(e);
    }
}
