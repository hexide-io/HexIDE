using System.IO;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Serilog;

namespace HexIDE.IDE;

/// <summary>The images that carry the product's identity, from its descriptor or else HexIDE's own (#2042).</summary>
public static class BrandAssets
{
    private const string StockLogo = "avares://HexIDE/Icons/AppIcon/hexide-logo-64.png";

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

    // The descriptor's reader bounds the file and checks it is a PNG of a sensible size; only decoding can show the
    // rest of it is sound, and that needs the UI toolkit, which is not up when the descriptor is read. A logo that
    // fails here shows HexIDE's: nothing is at stake but a picture, and the identity has already been settled.
    private static T? TryDecode<T>(Func<Stream, T> decode) where T : class
    {
        using var stream = Distribution.Current.OpenLogo();
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
