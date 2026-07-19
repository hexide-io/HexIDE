using Dock.Model.Mvvm.Controls;
using HexIDE.Localization;

namespace HexIDE.Tools;

public class WatchesToolViewModel : Tool
{
    public WatchesToolViewModel(ILocalizationService localization)
    {
        localization.BindTitle(this, "Str.Tool.Watches.Title");
        CanPin = false;
        CanClose = true;
    }
}