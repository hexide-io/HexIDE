using System.Collections.Generic;
using Avalonia.Media.Imaging;

namespace HexIDE.IDE;

public class AboutDialogOptions
{
    public string Title { get; set; } = "";
    public string SubTitle { get; set; } = "";
    public string Copyright { get; set; } = "";
    /// <summary>Brand credits appended after HexIDE’s own credits; supplied in the brand’s UI language.</summary>
    public IReadOnlyList<string> AdditionalCredits { get; set; } = [];
    public Bitmap? Icon { get; set; }
}
