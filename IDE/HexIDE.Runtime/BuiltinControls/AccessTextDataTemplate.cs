using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;

namespace HexIDE.Runtime.BuiltinControls;

public class AccessTextDataTemplate
{
    public static readonly FuncDataTemplate Access =
        new FuncDataTemplate<object>(
            (data, s) =>
            {
                if (data is string str)
                {
                    // Drawn as a symbol font's glyphs when the control's font is one (#1133).
                    return new VBAccessText { Source = str };
                }
                else
                {
                    return null;
                }
            },
            false);
}