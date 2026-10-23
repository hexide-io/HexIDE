using Avalonia.Controls.Templates;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// Draws a string caption as VB6 draws it, with its access key where Avalonia can carry one (<see cref="VBCaption"/>).
/// Not recycled: the child's type depends on the caption, so a new caption builds a new child.
/// </summary>
public class AccessTextDataTemplate
{
    /// <summary>For a control: a caption that marks several characters takes the last as its key, the one VB6 underlines.</summary>
    public static readonly FuncDataTemplate Access = For(VBCaption.KeyChoice.Last);

    /// <summary>For a menu, which answers only to the first marked character (#920).</summary>
    public static readonly FuncDataTemplate MenuAccess = For(VBCaption.KeyChoice.First);

    private static FuncDataTemplate For(VBCaption.KeyChoice choice) =>
        new FuncDataTemplate<object>((data, _) => data is string caption ? VBCaption.Present(caption, choice) : null, false);
}
