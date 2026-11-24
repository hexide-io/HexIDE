using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.BuiltinControls;

internal static class FontDecorationDrawing
{
    private static readonly TextDecorationCollection Both =
        [TextDecorations.Underline[0], TextDecorations.Strikethrough[0]];

    internal static TextDecorationCollection? For(VBFont? font) => font switch
    {
        { Underline: true, Strikethrough: true } => Both,
        { Underline: true } => TextDecorations.Underline,
        { Strikethrough: true } => TextDecorations.Strikethrough,
        _ => null,
    };

    internal static void AttachToTextPresenters()
    {
        // The editor's template places its presenter in a plain Panel inside the scroll viewport.
        // Keep the lines there too so they follow scrolling, clipping and the control's stacking order.
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<TextBox>((box, e) =>
        {
            if (box is not VBTextBox && box.TemplatedParent is not VBComboBox && box.Parent is not VBListView)
                return;

            if (e.NameScope.Find<TextPresenter>("PART_TextPresenter") is { } presenter
                && presenter.GetVisualParent() is Panel panel)
                panel.Children.Add(new EditLines(presenter) { IsHitTestVisible = false });
        });
    }

    private sealed class EditLines(TextPresenter presenter) : Control
    {
        static EditLines() => AffectsRender<EditLines>(Inline.TextDecorationsProperty);

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            presenter.LayoutUpdated += PresenterLayoutUpdated;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            presenter.LayoutUpdated -= PresenterLayoutUpdated;
            base.OnDetachedFromVisualTree(e);
        }

        private void PresenterLayoutUpdated(object? sender, EventArgs e)
        {
            if (Inline.GetTextDecorations(this) is not null)
                InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            if (Inline.GetTextDecorations(this) is not { Count: > 0 } lines
                || !presenter.IsVisible || presenter.TranslatePoint(default, this) is not { } offset)
                return;

            var layout = presenter.TextLayout;
            var difference = Math.Min(0, presenter.Bounds.Height - layout.Height);
            var y = presenter.VerticalAlignment switch
            {
                VerticalAlignment.Center => difference / 2,
                VerticalAlignment.Bottom => difference,
                _ => 0,
            };
            y += offset.Y;
            foreach (var line in layout.TextLines)
            {
                var x = offset.X + line.Start;
                foreach (var run in line.TextRuns.OfType<DrawableTextRun>())
                {
                    if (run is ShapedTextRun shaped && shaped.Properties.ForegroundBrush is { } brush)
                        DrawLines(context, shaped, brush, lines, new Point(x, y + line.Baseline - shaped.Baseline));
                    x += run.Size.Width;
                }
                y += line.Height;
            }
        }

        private static void DrawLines(DrawingContext context, ShapedTextRun run, IBrush brush,
            TextDecorationCollection lines, Point origin)
        {
            // Give Avalonia the same shaped glyphs with a transparent foreground. The decoration
            // uses the original brush (including a selection's foreground) and the font's metrics.
            var source = run.ShapedBuffer;
            var glyphs = new ShapedBuffer(source.Text, source.Length, source.GlyphTypeface,
                source.FontRenderingEmSize, source.BidiLevel);
            for (var i = 0; i < source.Length; i++)
                glyphs[i] = source[i];
            var decorations = new TextDecorationCollection();
            foreach (var line in lines)
            {
                decorations.Add(new TextDecoration
                {
                    Location = line.Location,
                    StrokeThicknessUnit = line.StrokeThicknessUnit,
                    StrokeOffsetUnit = line.StrokeOffsetUnit,
                    Stroke = brush,
                });
            }

            using var overlay = new ShapedTextRun(glyphs, new GenericTextRunProperties(
                run.Properties.Typeface, source.FontRenderingEmSize, decorations, Brushes.Transparent));
            overlay.Draw(context, origin);
        }
    }
}
