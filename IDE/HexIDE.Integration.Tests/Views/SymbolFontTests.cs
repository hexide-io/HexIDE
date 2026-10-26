using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using static HexIDE.Integration.Tests.Views.ContainerRuntimeHarness;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A caption in a symbol font is drawn as the font's glyphs, as GDI draws it for VB6 (#1133): a Wingdings Label captioned
/// <c>PMJ</c> is a flag, a bomb and a smiling face in vb6.exe (the oracle's "The fonts a form names"). Where the font isn't
/// installed, as on Linux and macOS, the letters are drawn as before, so each test answers for whichever this machine is.
/// </summary>
public class SymbolFontTests : WindowTestFixture
{
    private static string Glyphs(params int[] codes) => new(codes.Select(c => (char)c).ToArray());

    private static readonly bool HasWingdings = VBFontExtensions.IsInstalled("Wingdings");

    private static string Frm(string control, string caption, string font, int charset) => $$"""
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           Begin {{control}} Cell
              Caption         =   "{{caption}}"
              BeginProperty Font
                 Name            =   "{{font}}"
                 Size            =   18
                 Charset         =   {{charset}}
                 Weight          =   400
                 Underline       =   0   'False
                 Italic          =   0   'False
                 Strikethrough   =   0   'False
              EndProperty
              Height          =   600
              Left            =   120
              TabIndex        =   0
              Top             =   120
              Width           =   2200
           End
        End
        Attribute VB_Name = "Form1"
        """;

    // What the control draws: the label's own text, or the text a button's template made for its caption.
    private static string? Drawn(Control control) =>
        control.GetVisualDescendants().OfType<TextBlock>().Single().Text;

    [AvaloniaFact]
    public void Each_byte_goes_to_its_glyph_in_the_private_use_range()
    {
        SymbolFontText.ToSymbolGlyphs("PMJ").Should().Be(Glyphs(0xF050, 0xF04D, 0xF04A));
        // A line break and a character already in the range are left as they are. An ampersand is mapped like any other:
        // the text is as shown, with the caption's access key already read (#920).
        SymbolFontText.ToSymbolGlyphs("&P" + "\r\n" + Glyphs(0xF04D)).Should().Be(Glyphs(0xF026, 0xF050) + "\r\n" + Glyphs(0xF04D));

        // Above 127, the byte is the ANSI code page's. On a Windows-1252 machine the euro is byte 128, and what HexIDE's
        // Chr$(&H81) gives today, U+0081, is byte 129, the circled one.
        if (Vb6TextEncoding.Ansi.CodePage == 1252)
            SymbolFontText.ToSymbolGlyphs(Glyphs(0x20AC, 0x81)).Should().Be(Glyphs(0xF080, 0xF081));
    }

    [AvaloniaFact]
    public async Task A_Wingdings_Label_draws_its_glyphs_where_the_font_is_installed_and_reads_back_its_letters()
    {
        var (canvas, _, ctx, env) = Laid(Frm("VB.Label", "PMJ", "Wingdings", 2));

        Drawn(Child(canvas, "Cell")).Should().Be(HasWingdings ? Glyphs(0xF050, 0xF04D, 0xF04A) : "PMJ");
        (await Run(ctx, env, "Debug.Print Cell.Caption\r\n")).Single().Value.Should().Be("PMJ");
    }

    [AvaloniaFact]
    public void A_Label_in_an_ordinary_font_draws_its_letters()
    {
        var (canvas, _, _, _) = Laid(Frm("VB.Label", "PMJ", "Arial", 0));

        Drawn(Child(canvas, "Cell")).Should().Be("PMJ");
    }

    [AvaloniaFact]
    public void A_symbol_font_named_with_an_ordinary_charset_draws_letters()
    {
        // The charset decides, not the name: vb6.exe draws letters for Wingdings with Charset 0, because GDI then picks
        // another face (measured: Font.Charset = 0 after FontName = "Wingdings" reads back Arial and draws PMJ).
        var (canvas, _, _, _) = Laid(Frm("VB.Label", "PMJ", "Wingdings", 0));

        Drawn(Child(canvas, "Cell")).Should().Be("PMJ");
    }

    [AvaloniaTheory]
    [InlineData("VB.CommandButton")]
    [InlineData("VB.CheckBox")]
    [InlineData("VB.OptionButton")]
    public void A_Wingdings_button_draws_its_caption_as_glyphs_with_its_access_key(string control)
    {
        var (canvas, _, _, _) = Laid(Frm(control, "&PM", "Wingdings", 2));

        Drawn(Child(canvas, "Cell")).Should().Be(HasWingdings ? "_" + Glyphs(0xF050, 0xF04D) : "_PM");
    }

    [AvaloniaFact]
    public void A_Wingdings_form_draws_its_menus_in_letters()
    {
        // VB6 draws menus in the system's menu font, whatever the form's font is.
        var (root, _, _, _) = Spawn("""
            VERSION 5.00
            Begin VB.Form Form1
               Caption         =   "Form1"
               BeginProperty Font
                  Name            =   "Wingdings"
                  Size            =   12
                  Charset         =   2
                  Weight          =   400
                  Underline       =   0   'False
                  Italic          =   0   'False
                  Strikethrough   =   0   'False
               EndProperty
               Begin VB.Menu mnuGame
                  Caption         =   "&Game"
               End
            End
            Attribute VB_Name = "Form1"
            """);
        // The form's font goes on its window, as FormComponentClass.InstantiateWindow puts it there.
        var window = new Window { Content = root, [AttachedProperties.FontProperty] = new VBFont("Wingdings", 12, charset: 2) };
        window.Show();

        var menu = ((DockPanel)root).Children.OfType<Menu>().Single();
        menu.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).Should().Contain("_Game");
    }

    [AvaloniaTheory]
    [InlineData("Wingdings")]
    [InlineData("Marlett")]
    public void An_installed_symbol_font_draws_its_own_glyphs(string family)
    {
        var fontFamily = new VBFont(family, 18, charset: SymbolFontText.SymbolCharset).ToFontFamily();
        if (!VBFontExtensions.IsInstalled(family))
        {
            // Not on this machine, as on Linux and macOS: the fallbacks follow it.
            fontFamily.FamilyNames.Should().Contain("Liberation Sans");
            return;
        }

        // Marlett was passed over for the platform's default face when named with the fallbacks after it, and drew boxes.
        var layout = new TextLayout(SymbolFontText.ToSymbolGlyphs("ab"), new Typeface(fontFamily), 18, Brushes.Black);
        var run = layout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().Single();
        run.GlyphRun.GlyphTypeface.FamilyName.Replace(((char)0).ToString(), "").Should().Be(family);
        run.GlyphRun.GlyphInfos.Select(glyph => glyph.GlyphIndex).Should().NotContain((ushort)0, "0 is the missing glyph, drawn as a box");
    }

    [AvaloniaFact]
    public void The_glyphs_follow_the_font_when_it_changes()
    {
        var (canvas, _, _, _) = Laid(Frm("VB.Label", "PMJ", "Arial", 0));
        var cell = Child(canvas, "Cell");

        AttachedProperties.SetFont(cell, new VBFont("Wingdings", 18, charset: 2));
        Drawn(cell).Should().Be(HasWingdings ? Glyphs(0xF050, 0xF04D, 0xF04A) : "PMJ");

        AttachedProperties.SetFont(cell, new VBFont("Arial", 18));
        Drawn(cell).Should().Be("PMJ");
    }
}
