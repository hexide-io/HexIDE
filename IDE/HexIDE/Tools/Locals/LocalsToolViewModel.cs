using Dock.Model.Mvvm.Controls;
using HexIDE.Localization;

namespace HexIDE.Tools;

public class LocalsToolViewModel : Tool
{
    public LocalsToolViewModel(ILocalizationService localization)
    {
        localization.BindTitle(this, "Str.Tool.Locals.Title");
        CanPin = false;
        CanClose = true;
    }
}