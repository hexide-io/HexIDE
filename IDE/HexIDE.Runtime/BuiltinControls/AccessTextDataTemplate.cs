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
                    var result = new AccessText()
                    {
                        Text = str.Replace("&", "_")
                    };
                    return result;
                }
                else
                {
                    return null;
                }
            },
            false);
}