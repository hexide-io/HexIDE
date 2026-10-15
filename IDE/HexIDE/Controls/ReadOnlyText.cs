using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Reactive;
using AvaloniaEdit.Folding;

namespace HexIDE.Controls;

/// <summary>
/// The theme's colour for text the developer does not edit: a code window's read-only regions, and the label of
/// every folded section (hexide-io/HexIDE#273 task 3.15).
/// </summary>
/// <remarks>
/// <para>
/// <b>A theme key, followed rather than read once.</b> Every shipped pack carries <c>ReadOnlyText</c>, and a pack
/// switch between Dark and Abyss changes its value without changing the theme variant. So nothing that waits for
/// a variant change, as the syntax palette does, would ever repaint for it. The resource itself is watched.
/// </para>
/// <para>
/// <b>The fold label's colour is process-wide.</b> AvaloniaEdit draws every folded section's label with one static
/// brush, which defaults to a grey that misses the contrast bar on a light editor and on Dark. Every editor that
/// folds sets it from the theme, so a label is legible whichever editor last saw a theme change, and each
/// repaints itself when it does.
/// </para>
/// </remarks>
internal static class ReadOnlyText
{
    /// <summary>The resource key; a theme pack names it <c>ReadOnlyText</c>.</summary>
    public const string BrushKey = "ReadOnlyTextBrush";

    /// <summary>
    /// Calls <paramref name="apply"/> with the theme's brush now and whenever it changes, after pointing the fold
    /// labels at it. The brush is null where the key does not resolve, and the fold labels are then left as
    /// they were.
    /// </summary>
    /// <returns>The subscription, to dispose when the control detaches.</returns>
    public static IDisposable Follow(Control control, Action<IBrush?> apply) =>
        control.GetResourceObservable(BrushKey).Subscribe(new AnonymousObserver<object?>(value =>
        {
            var brush = value as IBrush;
            if (brush is not null)
                FoldingElementGenerator.TextBrush = brush;
            apply(brush);
        }));
}
