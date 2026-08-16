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

        PaletteChanged?.Invoke();
    }
}
