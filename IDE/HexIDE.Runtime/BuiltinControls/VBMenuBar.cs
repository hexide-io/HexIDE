using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using HexIDE.Runtime.AvaloniaInterop;

namespace HexIDE.Runtime.BuiltinControls;

internal sealed class VBMenuBar : Menu
{
    protected override Type StyleKeyOverride => typeof(Menu);
    private double clientWidth;

    internal VBMenuBar()
    {
        if (OperatingSystem.IsWindows() && ToolbarCaptionMetrics.MenuFont() is { } font)
        {
            FontFamily = font.Typeface.FontFamily;
            FontStyle = font.Typeface.Style;
            FontWeight = font.Typeface.Weight;
            FontSize = font.Size;
        }
        // Keep measurement and drawing in the same font even before a theme has attached at Form_Load.
        SetValue(FontFamilyProperty, FontFamily);
        SetValue(FontSizeProperty, FontSize);
        SetValue(FontStyleProperty, FontStyle);
        SetValue(FontWeightProperty, FontWeight);
        UseLayoutRounding = false;
        Padding = new Thickness(0);
        ItemsPanel = new FuncTemplate<Panel?>(() => new RowsPanel(this));
    }

    internal double ItemWidth(Control item, string? caption = null)
    {
        var text = VBCaption.Parse(caption ?? (item as MenuItem)?.Header as string ?? "").Shown;
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight);
        var width = MenuBarLayout.MeasureCaption is { } measure ? measure(text)
            : OperatingSystem.IsWindows() ? ToolbarCaptionMetrics.Measure(text, typeface, FontSize)
            : new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, FontSize, Brushes.Black).WidthIncludingTrailingWhitespace;
        return width + 14;
    }

    internal int RowCount(Control? changed = null, bool? visible = null, string? caption = null,
        Control? added = null, int position = -1)
    {
        var items = Items.OfType<Control>().ToList();
        if (added is not null) items.Insert(position < 0 ? items.Count : position, added);
        var widths = items.Where(item => ReferenceEquals(item, changed) ? visible ?? item.IsVisible : item.IsVisible)
            .Select(item => ItemWidth(item, ReferenceEquals(item, changed) ? caption : null));
        return MenuBarLayout.Rows(widths, clientWidth).Length;
    }

    internal void PrepareItem(Control item)
    {
        item.Width = ItemWidth(item);
        item.Height = 19;
        item.MinHeight = 0;
        item.UseLayoutRounding = false;
        if (item is MenuItem menu)
        {
            // The caption starts 7 px in, as VB6's does. The 7 px on its right stay inside the item for the text itself:
            // Skia draws a caption a pixel or two wider than GDI measures it, and at 150% a right padding clipped the
            // last letter of every caption (seen live on #4978's review).
            menu.Padding = new Thickness(7, 0, 0, 0);
            menu.FontFamily = FontFamily;
            menu.FontSize = FontSize;
            menu.FontStyle = FontStyle;
            menu.FontWeight = FontWeight;
        }
    }

    internal void Refresh() => Rewrap(clientWidth);

    internal void Rewrap(double width)
    {
        clientWidth = width;
        foreach (var item in Items.OfType<Control>()) PrepareItem(item);
        Height = Math.Max(20, MenuBarLayout.Height(RowCount()));
        InvalidateMeasure();
    }

    internal static void Initialize(Control host, Menu menu)
    {
        if (menu is not VBMenuBar bar) return;
        bar.Rewrap(host.Width);
        var extra = bar.IsVisible ? bar.Height - VBLoader.MenuBarHeight : 0;
        if (host is Window window) FormFrame.ChangeMenuHeight(window, extra);
        host.Height += extra;
        // The window's size is HexIDE's, and stays so through Windows' rounding of it (#4995).
        if (host is Window sized) FormFrame.RememberWindowSize(sized, true, host.Height);
        host.PropertyChanged += (_, change) =>
        {
            if (change.Property != WidthProperty) return;
            var before = bar.Height;
            bar.Rewrap(host.Width);
            if (host is Window form && bar.IsVisible)
                FormFrame.ChangeMenuHeight(form, bar.Height - before);
        };
    }

    private sealed class RowsPanel(VBMenuBar bar) : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var child in Children) child.Measure(new Size(child.Width, 19));
            return new Size(bar.clientWidth, bar.Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double x = 0, y = 0;
            var count = 0;
            foreach (var child in Children.Where(child => child.IsVisible))
            {
                if (count > 0 && x + child.Width > bar.clientWidth - 7)
                {
                    x = 0;
                    y += 19;
                    count = 0;
                }
                child.Arrange(new Rect(x, y, child.Width, 19));
                x += child.Width;
                count++;
            }
            return finalSize;
        }
    }
}
