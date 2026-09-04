using System;
using System.Collections.Generic;
using System.Xml;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace HexIDE.Themes;

/// <summary>
/// Owns the single shared VB6 <see cref="IHighlightingDefinition"/> and keeps its named colours
/// legible under the active theme.
///
/// <para><b>Why this exists.</b> <c>VB6.xshd.xml</c> hardcoded a light palette with no dark
/// counterpart, so on the Dark and Abyss packs the keyword colour <c>#0000B0</c> sat on editor
/// background <c>#252526</c> at roughly 1.1:1 — every keyword in the code window was invisible.</para>
///
/// <para><b>One definition, two palettes.</b> The xshd stays the single source of truth for the light
/// palette; its Foreground brushes are snapshotted at load. On a dark theme variant the named colours
/// are overwritten in place from <see cref="DarkPalette"/>, and switching back restores the snapshot.
/// Nothing is duplicated, so a new rule added to the xshd cannot silently drift — it simply keeps its
/// light colour until a dark entry is added here.</para>
///
/// <para><b>Keyed to the theme variant, not the pack.</b> Dark and Abyss both declare
/// <c>themeVariant: "Dark"</c>, so <c>ThemeService</c> leaves <c>RequestedThemeVariant</c> unchanged
/// when switching between them and no notification is raised. One dark palette that clears AA on both
/// packs' backgrounds therefore needs no per-pack hook. A bespoke per-pack scheme would require
/// <c>IThemeService</c> to grow a real theme-changed event first.</para>
///
/// <para><b>Mutation is global and does not repaint.</b> Every rule holds the same
/// <see cref="HighlightingColor"/> instance that <c>HighlightingManager</c> hands to every editor and
/// every minimap, so one write retints the whole IDE. But AvaloniaEdit bakes the brush into each
/// visual line's run properties when the line is built, so already-rendered text keeps the old colour
/// until <c>TextView.Redraw()</c> clears that cache. Views must do that from
/// <see cref="PaletteChanged"/> — the same reason <c>LspTextMarkerService.SetMarkers</c> calls Redraw
/// rather than InvalidateVisual.</para>
/// </summary>
public static class SyntaxHighlightingTheme
{
    public const string DefinitionName = "VB6";

    private static readonly string[] Extensions = [".vb6", ".bas", ".frm", ".cls"];
    private static readonly object Gate = new();

    // VB6 hues preserved — keywords blue, comments green, strings red, functions tan, metadata
    // magenta — lightened until every entry clears WCAG AA 4.5:1 against BOTH dark editor
    // backgrounds. Ratios below are (vs Dark #252526 / vs Abyss #000C18); #252526 is the binding
    // constraint, so anything passing there passes on Abyss too.
    private static readonly (string Name, string Hex)[] DarkPalette =
    [
        ("Comment",         "#6AAE6A"), // 5.74 /  7.39 — green,  as light #008000
        ("String",          "#E08A8A"), // 5.96 /  7.67 — red,    as light #A31515
        ("Number",          "#E08A8A"), // 5.96 /  7.67
        ("Constant",        "#E08A8A"), // 5.96 /  7.67
        ("Keyword",         "#7AA6F0"), // 6.23 /  8.01 — blue,   as light #0000B0
        ("IOKeyword",       "#7AA6F0"), // 6.23 /  8.01
        ("StorageModifier", "#7AA6F0"), // 6.23 /  8.01
        ("Type",            "#9CC4F5"), // 8.48 / 10.91 — lighter blue (light nudges #0000B0 to #0000B1)
        ("Function",        "#D3B07C"), // 7.49 /  9.64 — tan,    as light #74531F
        ("MetadataKeyword", "#E58FE5"), // 6.85 /  8.81 — magenta, as light #FF00FF
    ];

    private static readonly Dictionary<string, HighlightingBrush?> LightPalette = new(StringComparer.Ordinal);

    /// <summary>
    /// Definitions HexIDE did not author — AvaloniaEdit's bundled set, resolved by file extension for
    /// non-VB6 documents — together with their original light palettes.
    ///
    /// <para>
    /// These cannot get the hand-tuned treatment above, because the point of resolving by extension is
    /// that the set is open-ended: we do not know in advance which definitions exist, nor what their
    /// colours are named. So their dark palette is DERIVED (see <see cref="DeriveForDarkBackground"/>)
    /// rather than authored, targeting the same 4.5:1 contrast bar the VB6 palette was tuned to.
    /// </para>
    /// </summary>
    private static readonly List<(IHighlightingDefinition Definition, Dictionary<string, HighlightingBrush?> Light)>
        AdoptedDefinitions = [];

    /// <summary>The darker of the two dark editor backgrounds, and therefore the binding constraint.</summary>
    private static readonly Color DarkEditorBackground = Color.Parse("#252526");

    private static IHighlightingDefinition? _definition;
    private static bool? _appliedDark;

    // Which Application we subscribed to, not merely whether we subscribed. The definition is
    // process-wide static but an Application is not necessarily: Avalonia.Headless.XUnit builds a fresh
    // one per test, so a plain bool latched on the first Application and left every later one
    // unsubscribed — the palette silently stopped following the theme. Production has a single
    // Application, so this only ever mattered under test, which is precisely where it went unnoticed.
    private static Application? _wiredTo;

    /// <summary>
    /// Raised after the shared definition's named colours have been swapped for the active theme
    /// variant. Handlers must repaint: <c>TextEditor.TextArea.TextView.Redraw()</c>.
    /// </summary>
    public static event Action? PaletteChanged;

    /// <summary>The shared VB6 highlighting definition, registered and theme-corrected on first use.</summary>
    public static IHighlightingDefinition Definition
    {
        get
        {
            EnsureRegistered();
            return _definition!;
        }
    }

    /// <summary>
    /// Loads and registers the VB6 xshd (once), applies the palette for the active theme variant, and
    /// subscribes to theme changes. Safe to call repeatedly.
    /// </summary>
    public static void EnsureRegistered()
    {
        lock (Gate)
        {
            if (_definition is null)
            {
                var uri = new Uri("avares://HexIDE/Resources/TextHighlighting/VB6.xshd.xml");
                var xshdContent = AssetLoader.Open(uri);

                if (xshdContent == null)
                    throw new InvalidOperationException("VB6 XSHD resource not found");

                using var reader = new XmlTextReader(xshdContent);
                var xshd = HighlightingLoader.LoadXshd(reader);
                var definition = HighlightingLoader.Load(xshd, HighlightingManager.Instance);
                HighlightingManager.Instance.RegisterHighlighting(DefinitionName, Extensions, definition);

                // Snapshot the light palette off the xshd so it remains the single source of truth.
                foreach (var color in definition.NamedHighlightingColors)
                {
                    if (color.Name is not null)
                        LightPalette[color.Name] = color.Foreground;
                }

                _definition = definition;
            }
        }

        EnsureWired();
    }

    private static void EnsureWired()
    {
        // ActualThemeVariant and the event subscription are thread-affine. Off the UI thread — a
        // parallel xUnit worker, say — leave the light palette in place and skip wiring rather than
        // throw.
        if (Application.Current is not { } app || !Dispatcher.UIThread.CheckAccess())
            return;

        if (!ReferenceEquals(_wiredTo, app))
        {
            _wiredTo = app;
            // A different Application means the palette state we recorded describes someone else's
            // theme; force the next ApplyPalette to actually do the work.
            _appliedDark = null;

            app.ActualThemeVariantChanged += (_, _) =>
            {
                if (Application.Current is { } current)
                    ApplyPalette(current);
            };
        }

        ApplyPalette(app);
    }

    /// <summary>
    /// Brings a definition HexIDE does not own under theme control, so its colours are legible on a dark
    /// background. Idempotent; safe to call every time an editor resolves a definition.
    /// </summary>
    public static void Adopt(IHighlightingDefinition? definition)
    {
        if (definition is null) return;

        // Guarantees the theme-change subscription exists. Without it, a session whose only open editor
        // is a non-VB6 document would never wire ActualThemeVariantChanged, and adopted definitions would
        // stay on whichever palette they were given first.
        EnsureRegistered();

        lock (Gate)
        {
            foreach (var (existing, _) in AdoptedDefinitions)
                if (ReferenceEquals(existing, definition)) return;

            var light = new Dictionary<string, HighlightingBrush?>(StringComparer.Ordinal);
            foreach (var color in definition.NamedHighlightingColors)
                if (color.Name is not null)
                    light[color.Name] = color.Foreground;

            AdoptedDefinitions.Add((definition, light));
        }

        // Adoption can happen long after the theme was last applied, so bring the newcomer up to date
        // rather than leaving it light until the next theme change.
        if (Application.Current is { } app && Dispatcher.UIThread.CheckAccess())
            ApplyToAdopted(app.ActualThemeVariant == ThemeVariant.Dark);
    }

    private static void ApplyToAdopted(bool dark)
    {
        lock (Gate)
        {
            foreach (var (definition, light) in AdoptedDefinitions)
            {
                foreach (var color in definition.NamedHighlightingColors)
                {
                    if (color.Name is null || !light.TryGetValue(color.Name, out var original)) continue;
                    color.Foreground = dark ? DeriveForDarkBackground(original) : original;
                }
            }
        }
    }

    /// <summary>
    /// Lifts a light-theme foreground until it is legible on a dark editor background, keeping its hue.
    ///
    /// <para>
    /// Hue is preserved because it carries meaning — a reader learns that blue is a link and green is a
    /// comment, and a re-tint that scrambles those teaches them nothing. Only lightness moves, and only
    /// upward, until the colour clears 4.5:1 against the darker of the two dark editor backgrounds. That
    /// is the same bar the hand-tuned VB6 palette was built to, so both halves of the editor agree.
    /// </para>
    /// </summary>
    private static HighlightingBrush? DeriveForDarkBackground(HighlightingBrush? original)
    {
        if (original is null) return null;
        if (original.GetColor(null!) is not { } color) return original;

        var hsl = color.ToHsl();
        var lightness = hsl.L;

        // Walk lightness up in small steps rather than solving directly: contrast is non-linear in HSL
        // lightness, and stepping keeps the colour as close to its original as the bar allows instead of
        // washing everything out to near-white.
        for (var i = 0; i < 100 && ContrastRatio(color, DarkEditorBackground) < 4.5; i++)
        {
            lightness = Math.Min(1.0, lightness + 0.01);
            color = new HslColor(hsl.A, hsl.H, hsl.S, lightness).ToRgb();
            if (lightness >= 1.0) break;
        }

        return new SimpleHighlightingBrush(color);
    }

    /// <summary>WCAG relative-luminance contrast ratio between two opaque colours.</summary>
    private static double ContrastRatio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var (lighter, darker) = la > lb ? (la, lb) : (lb, la);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static void ApplyPalette(Application app)
    {
        if (_definition is null)
            return;

        var dark = app.ActualThemeVariant == ThemeVariant.Dark;
        if (_appliedDark == dark)
            return;
        _appliedDark = dark;

        if (dark)
        {
            foreach (var (name, hex) in DarkPalette)
            {
                var color = _definition.GetNamedColor(name);
                if (color is null)
                    continue; // colour removed from the xshd — nothing to override
                color.Foreground = new SimpleHighlightingBrush(Color.Parse(hex));
            }
        }
        else
        {
            foreach (var color in _definition.NamedHighlightingColors)
            {
                if (color.Name is not null && LightPalette.TryGetValue(color.Name, out var brush))
                    color.Foreground = brush;
            }
        }

        ApplyToAdopted(dark);
        PaletteChanged?.Invoke();
    }
}
