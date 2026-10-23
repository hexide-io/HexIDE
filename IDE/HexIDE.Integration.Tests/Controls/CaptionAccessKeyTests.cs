using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HexIDE.Runtime.BuiltinControls;
using static HexIDE.Integration.Tests.Views.ContainerRuntimeHarness;

namespace HexIDE.Integration.Tests.Controls;

/// <summary>
/// A caption's access key, drawn as vb6.exe draws it (#920). HexIDE turned every <c>&amp;</c> into Avalonia's <c>_</c>, so
/// <c>Tom &amp;&amp; Jerry</c> drew <c>Tom _ Jerry</c> and <c>File_Name</c> drew <c>FileName</c> with N its key.
/// </summary>
/// <remarks>
/// Every expectation here is a row of the oracle's "A caption's access key", measured on a p-code build in the guest's
/// console session: what each control drew, and which control each Alt+letter reached. Where Avalonia cannot draw what VB6
/// does, the test says so by name.
/// </remarks>
public class CaptionAccessKeyTests
{
    // ── The reading ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Tom && Jerry", "Tom & Jerry", -1)]
    [InlineData("File_Name", "File_Name", -1)]
    [InlineData("Trail&", "Trail", -1)]
    [InlineData("A&&&Bc", "A&Bc", 2)]
    [InlineData("Hex_&Kit", "Hex_Kit", 4)]
    [InlineData("&Mute", "Mute", 0)]
    [InlineData("&One &Wide", "One Wide", 4)]
    public void A_control_reads_its_caption_as_vb6_does(string caption, string shown, int key)
    {
        var parsed = VBCaption.Parse(caption);

        parsed.Shown.Should().Be(shown);
        parsed.AccessKey.Should().Be(key < 0 ? null : key, "a control underlines the last character a caption marks");
    }

    [Fact]
    public void A_menu_takes_the_first_marked_character_as_its_key()
    {
        // vb6.exe underlines the U of "&Go &Upward" and answers only to Alt+G.
        VBCaption.Parse("&Go &Upward", VBCaption.KeyChoice.First).Should().Be(new VBCaption.Parsed("Go Upward", 0));
    }

    [Theory]
    [InlineData("A&&&Bc", "A&_Bc")]
    [InlineData("&Hex_Kit", "_Hex__Kit")]
    [InlineData("&One &Wide", "One _Wide")]
    public void A_key_before_every_underscore_is_written_in_AccessTexts_spelling(string caption, string spelled)
    {
        var parsed = VBCaption.Parse(caption);

        parsed.FitsAccessText.Should().BeTrue();
        parsed.ForAccessText(symbolGlyphs: false).Should().Be(spelled);
    }

    [AvaloniaFact]
    public void AccessText_still_takes_its_key_from_a_doubled_underscore()
    {
        // Why a caption with an underscore before its key is drawn as plain text, with no key. If this fails, Avalonia has
        // stopped doing it, and those captions can be AccessTexts with the key VB6 gives them.
        var text = new AccessText { Text = "Hex___Kit" };
        var window = new Window { Content = text };
        window.Show();
        try { text.AccessKey.Should().Be("_"); }
        finally { window.Close(); }
    }

    // ── What each control draws ─────────────────────────────────────────────────────────────────

    private static string Frm(string control, string caption, string extra = "") => $$"""
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           Begin {{control}} Cell
              Caption         =   "{{caption}}"
              Height          =   600
              Left            =   120
              TabIndex        =   0
              Top             =   120
              Width           =   2200{{extra}}
           End
        End
        Attribute VB_Name = "Form1"
        """;

    // The caption's own text element: the button's, check box's and option button's from their template, the label's from
    // its border, the frame's from its header.
    private static TextBlock Caption(Control control) => control is VBLabel
        ? control.GetVisualDescendants().OfType<TextBlock>().Single()
        : control.GetVisualDescendants().OfType<TextBlock>().Single(t => t is VBAccessText or VBCaptionText);

    private static void ShouldBePlain(TextBlock drawn, string text)
    {
        drawn.Should().NotBeAssignableTo<AccessText>("a caption with no key AccessText can carry has none");
        drawn.Text.Should().Be(text);
    }

    private static void ShouldHaveKey(TextBlock drawn, char key)
    {
        drawn.Should().BeAssignableTo<AccessText>();
        ((AccessText)drawn).AccessKey.Should().Be(key.ToString());
    }

    public static TheoryData<string> Controls => ["VB.CommandButton", "VB.CheckBox", "VB.OptionButton", "VB.Label", "VB.Frame"];

    [AvaloniaTheory]
    [MemberData(nameof(Controls))]
    public void A_doubled_ampersand_is_drawn_as_one_with_no_key(string control)
    {
        var (canvas, window, _, _) = Laid(Frm(control, "Tom && Jerry"));
        try { ShouldBePlain(Caption(Child(canvas, "Cell")), "Tom & Jerry"); }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Controls))]
    public void An_underscore_is_drawn_and_is_no_key(string control)
    {
        var (canvas, window, _, _) = Laid(Frm(control, "File_Name"));
        try { ShouldBePlain(Caption(Child(canvas, "Cell")), "File_Name"); }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Controls))]
    public void A_trailing_ampersand_is_not_drawn(string control)
    {
        // A Label in vb6.exe draws an underline where the marked character would be. That is not reproduced.
        var (canvas, window, _, _) = Laid(Frm(control, "Trail&"));
        try { ShouldBePlain(Caption(Child(canvas, "Cell")), "Trail"); }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Controls))]
    public void A_marked_character_after_a_doubled_ampersand_is_the_key(string control)
    {
        var (canvas, window, _, _) = Laid(Frm(control, "A&&&Bc"));
        try { ShouldHaveKey(Caption(Child(canvas, "Cell")), 'B'); }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Controls))]
    public void Of_two_marked_characters_the_last_is_the_key(string control)
    {
        // vb6.exe underlines the W and answers to Alt+O and Alt+W alike. AccessText has one key, so Alt+O does nothing here.
        var (canvas, window, _, _) = Laid(Frm(control, "&One &Wide"));
        try { ShouldHaveKey(Caption(Child(canvas, "Cell")), 'W'); }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Controls))]
    public void An_underscore_before_the_key_leaves_the_caption_with_no_key(string control)
    {
        // The known loss: vb6.exe answers to Alt+K. AccessText would take its key from the underscore instead (the sentinel
        // above), so the caption is drawn as plain text, right but with no key.
        var (canvas, window, _, _) = Laid(Frm(control, "Hex_&Kit"));
        try { ShouldBePlain(Caption(Child(canvas, "Cell")), "Hex_Kit"); }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Lab && Rat")]
    [InlineData("&Mute")]
    public void A_label_with_UseMnemonic_off_draws_every_ampersand(string caption)
    {
        var (canvas, window, _, _) = Laid(Frm("VB.Label", caption, "\n      UseMnemonic     =   0   'False"));
        try { ShouldBePlain(Caption(Child(canvas, "Cell")), caption); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_caption_set_at_run_time_is_drawn_by_the_same_rule()
    {
        var (canvas, window, ctx, env) = Laid(Frm("VB.CommandButton", "&OK"));
        try
        {
            var button = Child(canvas, "Cell");
            ShouldHaveKey(Caption(button), 'O');

            await Run(ctx, env, "Cell.Caption = \"File_Name\"\r\n");

            ShouldBePlain(Caption(button), "File_Name");
        }
        finally { window.Close(); }
    }

    // ── Menus ───────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void A_menu_draws_its_caption_as_vb6_does_and_takes_the_first_marked_character()
    {
        var (root, _, _, _) = Spawn("""
            VERSION 5.00
            Begin VB.Form Form1
               Caption         =   "Form1"
               Begin VB.Menu mnuA
                  Caption         =   "Menu && More"
               End
               Begin VB.Menu mnuB
                  Caption         =   "Menu_Bar"
               End
               Begin VB.Menu mnuC
                  Caption         =   "&Go &Upward"
               End
               Begin VB.Menu mnuD
                  Caption         =   "Dot_&Edge"
               End
            End
            Attribute VB_Name = "Form1"
            """);
        var window = new Window { Content = root };
        window.Show();
        try
        {
            TextBlock Header(string name) => root.GetVisualDescendants().OfType<VBMenu>()
                .Single(m => NameOf(m) == name).GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t is VBAccessText or VBCaptionText);

            ShouldBePlain(Header("mnuA"), "Menu & More");
            ShouldBePlain(Header("mnuB"), "Menu_Bar");
            ShouldHaveKey(Header("mnuC"), 'G');
            ShouldBePlain(Header("mnuD"), "Dot_Edge");
        }
        finally { window.Close(); }
    }
}
