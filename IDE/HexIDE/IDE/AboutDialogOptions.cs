using Avalonia.Media.Imaging;

namespace HexIDE.IDE;

public class AboutDialogOptions
{
    public string Title { get; set; } = "";
    public string SubTitle { get; set; } = "";
    public string Copyright { get; set; } = "";
    public Bitmap? Icon { get; set; }
}
