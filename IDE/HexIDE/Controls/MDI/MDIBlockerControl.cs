using Avalonia.Controls;
using Avalonia.Media;

namespace HexIDE.Controls;

public class MDIBlockerControl : Panel
{
    static MDIBlockerControl()
    {
        BackgroundProperty.OverrideDefaultValue<MDIBlockerControl>(Brushes.Transparent);
    }
}