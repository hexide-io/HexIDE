using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace HexIDE.Utils;

/// <summary>
/// Builds theme-tinted icons for C# call sites that need an <see cref="IImage"/> (dynamic
/// loaders, tab/tree/toolbox icons) rather than an in-tree <c>Path</c>. A single shared
/// <see cref="SolidColorBrush"/> tracks the active theme, so every <see cref="DrawingImage"/>
/// built here re-tints live on a theme switch — no restart. Geometries are the same
/// <c>Geo.*</c> resources used by the toolbar/menu Paths (see Themes/IconGeometry.axaml).
/// </summary>
public static class IconFactory
{
    // Shared, theme-tracking neutral ink. Mutating its Color re-renders every DrawingImage built
    // from it. Kept in sync with the IconInk ThemeDictionaries values used by the XAML Paths.
    private static readonly SolidColorBrush Ink = new(Color.Parse("#3A3D42"));
    private static bool _wired;

    private static Color ResolveInk(Application app)
        => app.ActualThemeVariant == ThemeVariant.Dark
            ? Color.Parse("#D7DEEC")   // light ink on dark themes
            : Color.Parse("#3A3D42");  // dark ink on light themes

    private static void EnsureWired()
    {
        // ActualThemeVariant, Resources, and the ActualThemeVariantChanged subscription are all
        // thread-affine (they VerifyAccess against the UI thread). Off that thread — e.g. a parallel
        // xUnit worker building an icon — leave the default ink and skip wiring rather than throw a
        // cross-thread exception. At runtime IconFactory is only ever called on the UI thread.
        if (Application.Current is not { } app || !Dispatcher.UIThread.CheckAccess())
            return;

        Ink.Color = ResolveInk(app);
        if (_wired)
            return;

        _wired = true;
        app.ActualThemeVariantChanged += (_, _) =>
        {
            if (Application.Current is { } current)
                Ink.Color = ResolveInk(current);
        };
    }

    /// <summary>Themed (neutral-ink) icon for a <c>Geo.*</c> geometry resource key.</summary>
    public static IImage Themed(string geometryKey)
    {
        EnsureWired();
        return Build(geometryKey, Ink);
    }

    /// <summary>Icon with a fixed (non-theme) brush — for semantic colours.</summary>
    public static IImage Colored(string geometryKey, IBrush brush) => Build(geometryKey, brush);

    private static IImage Build(string geometryKey, IBrush brush)
    {
        // The resource lookup reads ActualThemeVariant (thread-affine); guard it the same way so an
        // off-UI-thread caller gets an un-themed (null-geometry) icon instead of a VerifyAccess throw.
        Geometry? geometry = null;
        if (Application.Current is { } app && Dispatcher.UIThread.CheckAccess() &&
            app.Resources.TryGetResource(geometryKey, app.ActualThemeVariant, out var resource))
        {
            geometry = resource as Geometry;
        }

        return new DrawingImage
        {
            Drawing = new GeometryDrawing { Geometry = geometry, Brush = brush },
        };
    }
}
