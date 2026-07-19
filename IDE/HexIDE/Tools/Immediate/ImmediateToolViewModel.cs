using AvaloniaEdit.Document;
using HexIDE.Utils;
using HexIDE.Localization;
using Dock.Model.Mvvm.Controls;

namespace HexIDE.Tools;

public partial class ImmediateToolViewModel : EditorToolBase
{
    public TextDocument Document { get; } = new();

    public ImmediateToolViewModel(ILocalizationService localization)
    {
        localization.BindTitle(this, "Str.Tool.Immediate.Title");
        CanPin = false;
        CanClose = true;
    }
}