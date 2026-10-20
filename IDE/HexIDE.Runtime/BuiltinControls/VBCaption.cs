using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// A VB6 caption as it reads on screen, which is what a control on a running form is named for automation (#954, #1128).
/// An <c>&amp;</c> marks the access key and is not shown, and <c>&amp;&amp;</c> shows one <c>&amp;</c>: Windows' rule
/// for prefix characters, which VB6 draws through. Not measured on the oracle; #920 owes that measurement, and the
/// drawing side of the same rule.
/// </summary>
public static class VBCaption
{
    /// <summary>
    /// <paramref name="caption"/> as shown: <c>E&amp;xit</c> is <c>Exit</c>, <c>Tom &amp;&amp; Jerry</c> is <c>Tom &amp; Jerry</c>. A
    /// trailing <c>&amp;</c>, which Microsoft does not document, is kept.
    /// </summary>
    public static string AsShown(string caption)
    {
        if (!caption.Contains('&'))
            return caption;
        var shown = new StringBuilder(caption.Length);
        for (var i = 0; i < caption.Length; i++)
        {
            // An & marks the character after it, so a trailing one, with none to mark, is drawn.
            if (caption[i] != '&' || i + 1 == caption.Length)
                shown.Append(caption[i]);
            else if (caption[i + 1] == '&')
                shown.Append(caption[++i]);
        }
        return shown.ToString();
    }

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
