using System.IO;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Serilog;

namespace HexIDE.IDE;

/// <summary>The images that carry the product's identity, from its descriptor or else HexIDE's own (#2042).</summary>
public static class BrandAssets
{
    // A brand image, exempt from the icon system's vector rule (#3364). Rendered from hexide-logo.svg at 256 pixels, so it
    // stays sharp up to five times the 46 to 48 DIP About and New Project show it at; re-render it if the SVG changes.
    private const string StockLogo = "avares://HexIDE/Icons/AppIcon/hexide-logo-256.png";

    /// <summary>The logo shown in About and the New Project header.</summary>
    public static Bitmap Logo
    {
        get
        {
            if (TryDecode(stream => new Bitmap(stream)) is { } branded)
                return branded;
            return new Bitmap(AssetLoader.Open(new Uri(StockLogo)));
        }
    }

    /// <summary>The window icon a descriptor supplies, or null to leave the one the window already has.</summary>
    public static WindowIcon? WindowIcon => TryDecode(stream => new WindowIcon(stream));

    /// <summary>
    /// Gives one of the IDE's own windows the brand's icon, unless it has an icon of its own (#4170).
    /// </summary>
    /// <remarks>
    /// A window with no <c>Icon</c> shows the one Avalonia embeds from the stock executable's <c>ApplicationIcon</c>,
    /// which a repack can't reach, so every IDE window a brand should own is given its icon here: the main window, the
    /// IDE's dialogs and message boxes, a language server's prompts and the floating tool windows. A running program's
    /// windows are the program's, not the IDE's, and keep their own rule (<c>FormIcon</c>). With no brand nothing is
    /// set, and stock stays as it was.
    /// </remarks>
    public static void ApplyWindowIcon(Window window)
    {
        if (window.Icon is null && WindowIcon is { } icon)
            window.Icon = icon;
    }

    /// <summary>
    /// Where the logo is read from: the installation's descriptor. The distribution is fixed for the process, so a test
    /// that needs a brand's logo points this at one of its own, and puts it back.
    /// </summary>
    internal static Func<Stream?> OpenLogo { get; set; } = () => Distribution.Current.OpenLogo();

    // The descriptor's reader bounds the file and checks it is a PNG of a sensible size; only decoding can show the
    // rest of it is sound, and that needs the UI toolkit, which is not up when the descriptor is read. A logo that
    // fails here shows HexIDE's: nothing is at stake but a picture, and the identity has already been settled.
    private static T? TryDecode<T>(Func<Stream, T> decode) where T : class
    {
        using var stream = OpenLogo();
        if (stream == null)
            return null;

        try
        {
            return decode(stream);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "The logo named by {File} could not be decoded", DistributionDescriptor.FileName);
            return null;
        }
    }
}
