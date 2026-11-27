using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using HexIDE.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit.Highlighting;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Themes;

namespace HexIDE.Integration.Tests.Controls;

/// <summary>
/// <para>The IDE's half of the render guard: the form designer's control bitmaps and the code window's
/// syntax palette, rendered for real under Skia. Controls as a running program draws them, including the
/// option button that killed the process and the check box that was always ticked, are guarded by
/// <c>HexIDE.Runtime.Integration.Tests</c>' <c>ClassicRenderTests</c>.</para>
///
/// <para>These are a class of bug — a precompiled-against-Avalonia-11 assembly failing only at render
/// time — that compiles cleanly and cannot be caught by unit tests of view models. Rendering for real
/// under Skia is the only thing that finds them.</para>
/// </summary>
public class ClassicRenderTests
{
    private static Color Pixel(Bitmap bitmap, int x, int y)
    {
        var pixel = new byte[4];
        var handle = GCHandle.Alloc(pixel, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), handle.AddrOfPinnedObject(), 4, 4);
            // In the bitmap's own order: a RenderTargetBitmap is RGBA on macOS and BGRA elsewhere.
            return bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888
                ? Color.FromArgb(pixel[3], pixel[0], pixel[1], pixel[2])
                : Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
        }
        finally { handle.Free(); }
    }

    /// <summary>
    /// A window carrying the VB6 control themes plus the SystemColors brushes they resolve. This project's
    /// TestApp loads the IDE's colours and the running controls' templates as well (<c>TestApp.cs:83-93</c>),
    /// and the window still adds its own, as <c>HexIDE.Runtime.Integration.Tests</c>' copy does.
    /// </summary>
    private static Window BuildHost(Control content)
    {
        var window = new Window { Width = 240, Height = 120, Background = Brushes.White };

        window.Resources.MergedDictionaries.Add(
            new ResourceInclude(new Uri("avares://HexIDE.Integration.Tests/"))
            {
                Source = new Uri("avares://HexIDE.Runtime/BuiltinControls/Resources.axaml"),
            });

        window.Resources[Classic.CommonControls.SystemColors.WindowBrushKey] = new SolidColorBrush(Colors.White);
        window.Resources[Classic.CommonControls.SystemColors.WindowTextBrushKey] = new SolidColorBrush(Colors.Black);
        window.Resources[Classic.CommonControls.SystemColors.GrayTextBrushKey] = new SolidColorBrush(Color.Parse("#808080"));

        window.Content = content;
        return window;
    }

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private const string AccessKeyForm = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.CommandButton Command1
              Caption = "&OK"
              Height = 480
              Width = 2400
           End
           Begin VB.Label Label1
              Caption = "Centred &label"
              Alignment = 2
              Top = 600
              Height = 480
              Width = 2400
           End
        End
        Attribute VB_Name = "Form1"
        """;

    [AvaloniaFact]
    public void DesignerCommandCaption_PaintsTheAccessKeyUnderlineAtRest()
    {
        var form = Deserialize(AccessKeyForm);
        var designer = new FormEditViewModel(null!, new EventBus(), null!, null!, null!, null!, Substitute.For<ILocalizationService>())
            .Initialize(form);
        var renderer = new ControlRenderer { Width = 160, Height = 32 };
        var window = BuildHost(renderer);
        try
        {
            var component = FindComponent(form, "Command1");
            renderer.Component = new ComponentInstanceViewModel(designer, component);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            using var marked = (Bitmap)((Image)renderer.Child!).Source!;

            component.SetProperty(VBProperties.CaptionProperty, "OK");
            Dispatcher.UIThread.RunJobs();
            using var plain = (Bitmap)((Image)renderer.Child!).Source!;
            plain.Should().NotBeSameAs(marked);

            // Both captions lay out the same glyphs. Look for a horizontal run of added ink just below the O.
            var region = new PixelRect(65, 16, 15, 9);
            var markedPixels = ReadPixels(marked, region);
            var plainPixels = ReadPixels(plain, region);
            Enumerable.Range(0, region.Height).Should().Contain(y =>
                Enumerable.Range(0, region.Width - 2).Any(x =>
                    IsDarker(markedPixels, plainPixels, y * region.Width + x) &&
                    IsDarker(markedPixels, plainPixels, y * region.Width + x + 1) &&
                    IsDarker(markedPixels, plainPixels, y * region.Width + x + 2)));
        }
        finally
        {
            window.Close();
        }
    }

    private static bool IsDarker(int[] marked, int[] plain, int index) =>
        (marked[index] & 0xff) < (plain[index] & 0xff);

    private static FormDefinition Deserialize(string source) =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source.ReplaceLineEndings("\r\n"), new NullSink())!;

    private static ComponentInstance FindComponent(FormDefinition form, string name) =>
        form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == name);

    [AvaloniaFact]
    public void DesignerCommandCaption_HasAntialiasedPixels()
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), """
            VERSION 5.00
            Begin VB.Form Form1
               Begin VB.CommandButton Command0
                  Caption = "Command0"
                  Height = 480
                  Width = 2400
               End
            End
            Attribute VB_Name = "Form1"

            """.ReplaceLineEndings("\r\n"), new NullSink())!;
        var designer = new FormEditViewModel(null!, new EventBus(), null!, null!, null!, null!, Substitute.For<ILocalizationService>())
            .Initialize(form);
        var renderer = new ControlRenderer { Width = 160, Height = 32 };
        var window = BuildHost(renderer);
        try
        {
            window.Show();
            renderer.Component = new ComponentInstanceViewModel(designer, form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Command0"));
            Dispatcher.UIThread.RunJobs();
            var bitmap = (Bitmap)((Image)renderer.Child!).Source!;
            CaptionColours(bitmap, new PixelRect(10, 8, 140, 16)).Should().BeGreaterThan(2,
                "the designer bitmap must antialias its caption, excluding the button border");
        }
        finally
        {
            window.Close();
        }
    }

    // #913: the runtime paint (#752) and designer paint (oracle PR #5217) agree.
    private static FormDefinition ButtonStyleForm(int style) => new FormDeserializer().Deserialize(
        new ProjectDefinition(VBProjectType.EXE, "P"), $$"""
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.CommandButton Command1
              BackColor = &H000000FF&
              Style = {{style}}
              Height = 480
              Width = 2400
           End
        End
        Attribute VB_Name = "Form1"

        """.ReplaceLineEndings("\r\n"), new NullSink())!;

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void CommandButton_Style_selects_the_designer_face(int style)
    {
        var form = ButtonStyleForm(style);
        var designer = new FormEditViewModel(null!, new EventBus(), null!, null!, null!, null!, Substitute.For<ILocalizationService>())
            .Initialize(form);
        var renderer = new ControlRenderer { Width = 160, Height = 32 };
        var window = BuildHost(renderer);
        try
        {
            window.Show();
            renderer.Component = new ComponentInstanceViewModel(designer, form.Components.Single(c => c.BaseClass is CommandButtonComponentClass));
            Dispatcher.UIThread.RunJobs();
            var bitmap = (Bitmap)((Image)renderer.Child!).Source!;
            var expected = style == 1 ? Colors.Red
                : ((ISolidColorBrush)VBColor.FromSystemColor(VbSystemColor.Btnface).ToBrush()).Color;
            Pixel(bitmap, 10, 10).Should().Be(expected);
        }
        finally { window.Close(); }
    }

    private static int CaptionColours(Bitmap bitmap, PixelRect region) => new HashSet<int>(ReadPixels(bitmap, region)).Count;

    private static int[] ReadPixels(Bitmap bitmap, PixelRect region)
    {
        var pixels = new int[region.Width * region.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(region, handle.AddrOfPinnedObject(), pixels.Length * 4, region.Width * 4);
            return pixels;
        }
        finally
        {
            handle.Free();
        }
    }

    // ── The dark palette ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One test, not several: the highlighting definition is process-wide static, so parallel facts
    /// mutating the variant would race each other.
    /// </summary>
    [AvaloniaFact]
    public void SyntaxPalette_SwapsWithThemeVariant_AndSwapsBack()
    {
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        var definition = SyntaxHighlightingTheme.Definition;

        static Color ForegroundOf(AvaloniaEdit.Highlighting.IHighlightingDefinition d, string name)
        {
            var brush = d.GetNamedColor(name)!.Foreground!.GetBrush(null!);
            return ((ISolidColorBrush)brush).Color;
        }

        try
        {
            app.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            ForegroundOf(definition, "Keyword").Should().Be(Color.Parse("#0000B0"),
                "the light palette is the xshd's own and must survive a round trip");

            app.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();

            // #0000B0 on the Dark pack's editor background (#252526) is ~1.1:1 — invisible.
            ForegroundOf(definition, "Keyword").Should().Be(Color.Parse("#7AA6F0"));
            ForegroundOf(definition, "Comment").Should().Be(Color.Parse("#6AAE6A"));
            ForegroundOf(definition, "String").Should().Be(Color.Parse("#E08A8A"));

            app.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            ForegroundOf(definition, "Keyword").Should().Be(Color.Parse("#0000B0"));
        }
        finally
        {
            app.RequestedThemeVariant = original;
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void LightPalette_ClearsContrastAA_OnClassic()
    {
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        var definition = SyntaxHighlightingTheme.Definition;
        var classic = new ResourceInclude(new Uri("avares://HexIDE/"))
        {
            Source = new Uri("avares://HexIDE/Themes/Classic.axaml"),
        };
        var background = classic.Loaded[Classic.CommonControls.SystemColors.WindowBrushKey]
            .Should().BeAssignableTo<ISolidColorBrush>().Subject.Color;

        try
        {
            app.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();

            foreach (var color in definition.NamedHighlightingColors)
            {
                var solid = (color.Foreground?.GetBrush(null!)).Should()
                    .BeAssignableTo<ISolidColorBrush>($"'{color.Name}' must have a solid foreground").Subject;
                ContrastRatio(solid.Color, background).Should().BeGreaterThanOrEqualTo(4.5,
                    $"'{color.Name}' must be legible on Classic's editor background");
            }
        }
        finally
        {
            app.RequestedThemeVariant = original;
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// The guard that would have caught the original bug before it shipped: every named colour must
    /// clear WCAG AA against the editor background of both dark packs (Dark #252526, Abyss #000C18).
    /// The editor background is SystemColors.WindowBrushKey — see CodeEditorView.axaml.
    /// </summary>
    [AvaloniaFact]
    public void DarkPalette_ClearsContrastAA_OnBothDarkPacks()
    {
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        var definition = SyntaxHighlightingTheme.Definition;

        var names = new[]
        {
            "Comment", "String", "Number", "Constant",
            "Keyword", "IOKeyword", "StorageModifier", "Type", "Function", "MetadataKeyword",
        };

        try
        {
            app.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();

            foreach (var background in new[] { Color.Parse("#252526"), Color.Parse("#000C18") })
            {
                foreach (var name in names)
                {
                    var solid = (definition.GetNamedColor(name)?.Foreground?.GetBrush(null!)).Should()
                        .BeAssignableTo<ISolidColorBrush>($"'{name}' must have a solid foreground").Subject;

                    ContrastRatio(solid.Color, background).Should().BeGreaterThanOrEqualTo(4.5,
                        $"'{name}' must be legible on {background}");
                }
            }
        }
        finally
        {
            app.RequestedThemeVariant = original;
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void AnAdoptedBundledDefinitionIsLegibleOnADarkBackground()
    {
        // The VB6 definition gets a hand-tuned dark palette. A definition resolved by file extension from
        // AvaloniaEdit's bundle cannot: the set is open-ended, so its dark colours are DERIVED. This is the
        // parallel guarantee — a derived palette must clear the same bar the hand-tuned one was built to.
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        var background = Color.Parse("#252526");

        var definition = HighlightingManager.Instance.GetDefinitionByExtension(".md");
        definition.Should().NotBeNull("AvaloniaEdit is expected to bundle a Markdown definition");

        // THE CONTROL, and it has to come first. Every assertion below is "contrast is sufficient", which
        // would pass trivially against a definition that happened to be light already — proving nothing
        // about the derivation. So: establish that the STOCK palette genuinely fails on a dark background
        // before adopting it. If this ever stops failing, the test below has stopped testing anything.
        app.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        var stockFailures = definition!.NamedHighlightingColors
            .Select(c => c.Foreground?.GetBrush(null!) as ISolidColorBrush)
            .Where(b => b is not null)
            .Count(b => ContrastRatio(b!.Color, background) < 4.5);
        stockFailures.Should().BeGreaterThan(0,
            "the stock bundled palette is authored for a light background — if it were already legible "
          + "on dark, this test would prove nothing about the derived palette");

        try
        {
            SyntaxHighlightingTheme.Adopt(definition);
            app.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();

            foreach (var color in definition.NamedHighlightingColors)
            {
                if (color.Foreground?.GetBrush(null!) is not ISolidColorBrush solid)
                    continue;

                ContrastRatio(solid.Color, background).Should().BeGreaterThanOrEqualTo(4.5,
                    $"'{color.Name}' must be legible on {background} after adoption");
            }
        }
        finally
        {
            app.RequestedThemeVariant = original;
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void AdoptionPreservesHueSoColoursStillMeanWhatTheyMeant()
    {
        // Lightness is lifted; hue is not touched. That matters because hue carries the meaning a reader
        // has learned — blue is a link, green is a comment — and a re-tint that scrambles it would trade
        // one legibility problem for a worse one.
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        var definition = HighlightingManager.Instance.GetDefinitionByExtension(".md")!;

        try
        {
            app.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            var lightHues = definition.NamedHighlightingColors
                .Where(c => c.Name is not null && c.Foreground?.GetBrush(null!) is ISolidColorBrush)
                .ToDictionary(
                    c => c.Name!,
                    c => ((ISolidColorBrush)c.Foreground!.GetBrush(null!)!).Color.ToHsl());

            SyntaxHighlightingTheme.Adopt(definition);
            app.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();

            foreach (var color in definition.NamedHighlightingColors)
            {
                if (color.Name is null || !lightHues.TryGetValue(color.Name, out var light)) continue;
                if (color.Foreground?.GetBrush(null!) is not ISolidColorBrush solid) continue;
                // Greys have no meaningful hue, so exclude them rather than assert nonsense about them.
                if (light.S < 0.05) continue;

                solid.Color.ToHsl().H.Should().BeApproximately(light.H, 1.0,
                    $"'{color.Name}' should keep its hue — only its lightness is lifted");
            }
        }
        finally
        {
            app.RequestedThemeVariant = original;
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>WCAG 2.x relative-luminance contrast ratio.</summary>
    private static double ContrastRatio(Color a, Color b)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color c) =>
            0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

        var (l1, l2) = (Luminance(a), Luminance(b));
        var (hi, lo) = l1 >= l2 ? (l1, l2) : (l2, l1);
        return (hi + 0.05) / (lo + 0.05);
    }
}
