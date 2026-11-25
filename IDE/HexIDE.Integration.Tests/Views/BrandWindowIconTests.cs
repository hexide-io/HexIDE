using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using HexIDE.IDE;

namespace HexIDE.Integration.Tests.Views;

// #4170: a window with no Icon shows the one Avalonia embeds from the stock executable's ApplicationIcon, which a repack
// can't reach. So under a brand every IDE window takes the brand's logo as its icon, a running program's boxes keep
// their own rule, and stock sets nothing at all.
public class BrandWindowIconTests
{
    // Any PNG serves as the brand's: stock sets no Icon, so one that isn't null can only have come from the brand.
    private static Stream? BrandLogo() => AssetLoader.Open(new Uri("avares://HexIDE/Icons/AppIcon/hexide-logo-256.png"));

    private static T With<T>(Func<Stream?> logo, Func<T> make)
    {
        var previous = BrandAssets.OpenLogo;
        BrandAssets.OpenLogo = logo;
        try { return make(); }
        finally { BrandAssets.OpenLogo = previous; }
    }

    private static WindowManager Manager() => new(Substitute.For<HexIDE.Localization.ILocalizationService>());

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_IDE_dialog_takes_the_brands_icon_and_stock_sets_none(bool branded)
    {
        var window = With(branded ? BrandLogo : () => null, () => new DialogWindow());
        try { (window.Icon != null).Should().Be(branded); }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_IDE_message_box_takes_the_brands_icon_and_stock_sets_none(bool branded)
    {
        var window = With(branded ? BrandLogo : () => null,
            () => Manager().MakeDialogWindow("Title", new HexIDE.Dialogs.MessageBox()));
        try { (window.Icon != null).Should().Be(branded); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_running_programs_box_is_not_given_the_brands_icon()
    {
        var window = With(BrandLogo,
            // Made as the engine makes it, through the window-maker the IDE gives it (#5616).
            () => (Window)((HexIDE.IDE.IProgramDialogWindows)Manager())
                .MakeWindow("Title", new HexIDE.Runtime.Dialogs.MessageBox(), HexIDE.IDE.ProgramBox.Message));
        try { window.Icon.Should().BeNull("a program's box follows the program's rule, not the IDE's brand"); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_window_with_an_icon_of_its_own_keeps_it()
    {
        var own = new WindowIcon(BrandLogo()!);
        var window = new Window { Icon = own };
        try
        {
            With(BrandLogo, () => { BrandAssets.ApplyWindowIcon(window); return 0; });
            window.Icon.Should().BeSameAs(own);
        }
        finally { window.Close(); }
    }
}
