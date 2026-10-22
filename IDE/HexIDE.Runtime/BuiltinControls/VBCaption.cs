using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// A VB6 caption's access key, read as VB6 reads it and drawn as Avalonia can draw it (#920, #954, #1128).
/// </summary>
/// <remarks>
/// <para>
/// <b>What VB6 does, measured</b> (the oracle's "A caption's access key"): <c>&amp;&amp;</c> shows one <c>&amp;</c>; a single
/// <c>&amp;</c> is hidden and marks the character after it; a trailing <c>&amp;</c>, with nothing to mark, is not shown; and
/// <c>_</c> is an ordinary character. Where a caption marks more than one character, every control underlines the last and
/// answers to all of them, while a menu underlines the last and answers only to the first.
/// </para>
/// <para>
/// <b>What Avalonia can draw.</b> AccessText marks its key with <c>_</c> and draws <c>__</c> as one underscore, but takes
/// its key from the first underscore in its text whether or not it is doubled. So <c>File__Name</c> draws right and answers
/// to Alt+_, and <c>Hex___Kit</c> answers to Alt+_ instead of Alt+K (Avalonia 12.0.4, measured headlessly). Only a caption
/// whose key comes before every underscore it shows can be an AccessText; any other is drawn as plain text, with no key. That
/// loses the key VB6 has for the second kind, which the AccessText would lose as well, and keeps it from answering to a key
/// VB6 does not have.
/// </para>
/// <para>
/// AccessText registers one key. A caption that marks several is given the one VB6 underlines on a control and the one it
/// answers to on a menu, so a control's other marked letters do nothing here where VB6 answers to them.
/// </para>
/// </remarks>
public static class VBCaption
{
    /// <summary>Which marked character is the access key, where a caption marks more than one.</summary>
    public enum KeyChoice
    {
        /// <summary>The last, which a control underlines. VB6 answers to every marked character on a control.</summary>
        Last,

        /// <summary>The first, the only one a menu answers to. VB6 still underlines the last.</summary>
        First,
    }

    /// <summary>A caption as shown, and the index in <see cref="Shown"/> of its access key, if it has one.</summary>
    public readonly record struct Parsed(string Shown, int? AccessKey)
    {
        /// <summary>Whether AccessText can draw it: it has a key, and no underscore is shown before the key.</summary>
        public bool FitsAccessText => AccessKey is { } key && Shown.IndexOf('_', 0, key) < 0;

        /// <summary>The text a plain text block draws: what is shown, in a symbol font's glyphs where it is one.</summary>
        public string Drawn(bool symbolGlyphs) => symbolGlyphs ? SymbolFontText.ToSymbolGlyphs(Shown) : Shown;

        /// <summary>
        /// AccessText's spelling of it: every underscore doubled, and one in front of the key. Where it does not
        /// <see cref="FitsAccessText"/>, the doubled underscores alone, which draw right and answer to Alt+_.
        /// </summary>
        public string ForAccessText(bool symbolGlyphs)
        {
            // Mapped before the marker goes in. The mapping is one character for one, so the key's index holds, and an
            // underscore in a symbol font becomes its glyph rather than a second marker.
            var drawn = Drawn(symbolGlyphs).Replace("_", "__");
            if (!FitsAccessText)
                return drawn;
            var key = AccessKey!.Value;
            return drawn[..key] + "_" + drawn[key..];
        }
    }

    /// <summary>
    /// <paramref name="caption"/> read as VB6 reads it: <c>E&amp;xit</c> shows <c>Exit</c> with x its key,
    /// <c>Tom &amp;&amp; Jerry</c> shows <c>Tom &amp; Jerry</c> with no key, and <c>Trail&amp;</c> shows <c>Trail</c>.
    /// </summary>
    public static Parsed Parse(string caption, KeyChoice choice = KeyChoice.Last)
    {
        if (!caption.Contains('&'))
            return new Parsed(caption, null);

        var shown = new StringBuilder(caption.Length);
        int? key = null;
        for (var i = 0; i < caption.Length; i++)
        {
            if (caption[i] != '&')
            {
                shown.Append(caption[i]);
            }
            else if (i + 1 < caption.Length && caption[i + 1] == '&')
            {
                shown.Append('&');
                i++;
            }
            else if (i + 1 < caption.Length && (choice == KeyChoice.Last || key is null))
            {
                // The marked character is appended on the next pass.
                key = shown.Length;
            }
            // A trailing &, with nothing to mark, is not shown. A Label draws an underline where its character would be,
            // which a button and a menu do not; that is not reproduced.
        }
        return new Parsed(shown.ToString(), key);
    }

    /// <summary>
    /// <paramref name="caption"/> as shown: <c>E&amp;xit</c> is <c>Exit</c>, <c>Tom &amp;&amp; Jerry</c> is <c>Tom &amp; Jerry</c>,
    /// <c>Trail&amp;</c> is <c>Trail</c>.
    /// </summary>
    public static string AsShown(string caption) => Parse(caption).Shown;

    /// <summary>
    /// What a control draws its caption with: an AccessText where the caption <see cref="Parsed.FitsAccessText"/>, plain
    /// text where it does not. Both follow the control's font into a symbol font's glyphs (#1133).
    /// </summary>
    public static Control Present(string caption, KeyChoice choice) =>
        Parse(caption, choice).FitsAccessText
            ? new VBAccessText { Choice = choice, Source = caption }
            : new VBCaptionText { Source = caption };

    /// <summary>
    /// Names every <typeparamref name="T"/> for automation by its <paramref name="caption"/> as shown, and keeps the name
    /// in step as the caption changes. A local value, so it outranks the style that names the IDE's own controls, whose
    /// access keys are marked with <c>_</c>. A caption that is not text, or shows nothing, leaves the default name.
    /// </summary>
    public static void NameByCaption<T>(AvaloniaProperty caption) where T : Control =>
        caption.Changed.AddClassHandler<T>((control, e) =>
        {
            if (e.NewValue is string text && AsShown(text) is { Length: > 0 } shown)
                control.SetValue(AutomationProperties.NameProperty, shown);
            else
                control.ClearValue(AutomationProperties.NameProperty);
        });
}
