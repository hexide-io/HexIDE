using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using HexIDE.Addins;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.Forms.Views;
using HexIDE.Forms.Views.Options;
using HexIDE.IDE;
using HexIDE.Keymaps;
using HexIDE.Localization;
using HexIDE.Themes;

namespace HexIDE.Integration.Tests.Views;

public class OptionsViewIntegrationTests
{
    private static readonly AddinInfo SampleAddin =
        new("/addins/Demo.dll", "Demo Add-in", "A demo add-in", "1.0", "Tester");

    private static (ISettingsService settings, IThemeService theme, IKeymapService keymap, ILanguageSwitchService languageSwitch, IAddinRegistry registry, AddinOptionsService addinOptions) Mocks()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.ActiveTheme.Returns("Classic");
        settings.ActiveKeymap.Returns("Default");
        settings.ActiveLanguage.Returns("en");
        settings.TabWidth.Returns(4);
        settings.GridWidth.Returns(8);
        settings.GridHeight.Returns(8);

        var theme = Substitute.For<IThemeService>();
        theme.ActiveTheme.Returns("Classic");
        theme.AvailableThemes.Returns(["Classic", "Dark"]);

        var keymap = Substitute.For<IKeymapService>();
        keymap.ActiveKeymap.Returns("Default");
        keymap.AvailableKeymaps.Returns(["Default", "VB6"]);

        var languageSwitch = Substitute.For<ILanguageSwitchService>();
        languageSwitch.ActiveLanguage.Returns("en");
        languageSwitch.AvailableLanguages.Returns([new LanguageInfo("en", "English", "ltr")]);
        languageSwitch.RegionsFor(Arg.Any<string>()).Returns([]);

        var registry = Substitute.For<IAddinRegistry>();
        registry.GetAll().Returns([SampleAddin]);
        registry.GetStatus(SampleAddin.AssemblyPath).Returns(AddinStatus.Loaded);
        return (settings, theme, keymap, languageSwitch, registry, new AddinOptionsService());
    }

    [AvaloniaFact]
    public void OptionsView_RendersTwoPane_WithoutErrors()
    {
        var (settings, theme, keymap, languageSwitch, registry, addinOptions) = Mocks();
        var view = new OptionsView { DataContext = new OptionsViewModel(settings, theme, keymap, languageSwitch, registry, addinOptions, Substitute.For<IDeveloperModeService>(), Substitute.For<ILocalizationService>(), Substitute.For<IEventBus>()) };

        view.Measure(new Size(720, 520));
        view.Arrange(new Rect(0, 0, 720, 520));

        view.IsMeasureValid.Should().BeTrue();
        view.IsArrangeValid.Should().BeTrue();
    }

    [AvaloniaFact]
    public void DeveloperPageView_RendersDragonsBanner_WithoutErrors()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.LoadUnsignedAddins.Returns(false);
        var view = new DeveloperPageView { DataContext = new DeveloperPageViewModel(settings) };

        view.Measure(new Size(460, 320));
        view.Arrange(new Rect(0, 0, 460, 320));

        view.IsMeasureValid.Should().BeTrue();
        view.IsArrangeValid.Should().BeTrue();
    }

    [AvaloniaFact]
    public void AddinConsentDialog_RendersVerified_WithoutErrors()
    {
        var view = new AddinConsentDialog
        {
            DataContext = new AddinConsentDialogViewModel("My Add-in", "1.2.0", "Example Ltd", "Example Ltd", Substitute.For<ILocalizationService>()),
        };
        view.Measure(new Size(480, 360));
        view.Arrange(new Rect(0, 0, 480, 360));

        view.IsMeasureValid.Should().BeTrue();
        view.IsArrangeValid.Should().BeTrue();
    }

    [AvaloniaFact]
    public void AddinConsentDialog_RendersUnsignedWarning_WithoutErrors()
    {
        var vm = new AddinConsentDialogViewModel("Dev Add-in", "0.1.0", "Me", verifiedPublisher: null, localization: Substitute.For<ILocalizationService>());
        vm.IsUnsigned.Should().BeTrue();

        var view = new AddinConsentDialog { DataContext = vm };
        view.Measure(new Size(480, 360));
        view.Arrange(new Rect(0, 0, 480, 360));

        view.IsMeasureValid.Should().BeTrue();
    }

    // Regression: the ViewLocator is an explicit registry, and a missing registration silently renders
    // the VM's type name instead of the view. The render tests above instantiate views directly, so
    // they do NOT catch a missing registration — this does (it would have caught the consent dialog and
    // Developer page both shipping unregistered).
    [AvaloniaFact]
    public void ViewLocator_Resolves_DialogsAndPages_AddedRecently()
    {
        var locator = new ViewLocator();
        var settings = Substitute.For<ISettingsService>();

        locator.Build(new AddinConsentDialogViewModel("X", "1.0", "A", "Pub", Substitute.For<ILocalizationService>())).Should().BeOfType<AddinConsentDialog>();
        locator.Build(new DeveloperPageViewModel(settings)).Should().BeOfType<DeveloperPageView>();
        locator.Build(new TrustChainViewModel(null, "x", false, true)).Should().BeOfType<TrustChainView>();

        var languageSwitch = Substitute.For<ILanguageSwitchService>();
        languageSwitch.AvailableLanguages.Returns([new LanguageInfo("en", "English", "ltr")]);
        languageSwitch.ActiveLanguage.Returns("en");
        languageSwitch.RegionsFor(Arg.Any<string>()).Returns([]);
        locator.Build(new LanguagePageViewModel(settings, languageSwitch, Substitute.For<ILocalizationService>(), () => { })).Should().BeOfType<LanguagePageView>();
    }

    [AvaloniaFact]
    public void SelectingNode_SwapsCurrentPage()
    {
        var (settings, theme, keymap, languageSwitch, registry, addinOptions) = Mocks();
        var vm = new OptionsViewModel(settings, theme, keymap, languageSwitch, registry, addinOptions, Substitute.For<IDeveloperModeService>(), Substitute.For<ILocalizationService>(), Substitute.For<IEventBus>());

        vm.CurrentPage.Should().BeOfType<EnvironmentGeneralPageViewModel>();

        var gridNode = vm.RootNodes
            .SelectMany(n => n.Children)
            .First(n => n.Page is FormDesignerGridPageViewModel);
        vm.SelectedNode = gridNode;

        vm.CurrentPage.Should().BeOfType<FormDesignerGridPageViewModel>();
    }

    [AvaloniaFact]
    public void AllPageViews_RenderWithoutErrors()
    {
        var (settings, theme, keymap, languageSwitch, registry, addinOptions) = Mocks();
        var views = new Control[]
        {
            new EnvironmentGeneralPageView { DataContext = new EnvironmentGeneralPageViewModel(settings) },
            new ThemePageView { DataContext = new ThemePageViewModel(settings, theme) },
            new LanguagePageView { DataContext = new LanguagePageViewModel(settings, languageSwitch, Substitute.For<ILocalizationService>(), () => { }) },
            new KeymapPageView { DataContext = new KeymapPageViewModel(settings, keymap) },
            new ToolbarsPageView { DataContext = new ToolbarsPageViewModel(settings) },
            new EditorGeneralPageView { DataContext = new EditorGeneralPageViewModel(settings) },
            new EditorFormattingPageView { DataContext = new EditorFormattingPageViewModel(settings) },
            new FormDesignerGridPageView { DataContext = new FormDesignerGridPageViewModel(settings) },
            new AdvancedLspPageView { DataContext = new AdvancedLspPageViewModel(settings) },
            new AddinOptionsPageView { DataContext = new AddinOptionsPageViewModel(registry, SampleAddin) },
        };

        foreach (var v in views)
        {
            v.Measure(new Size(420, 300));
            v.Arrange(new Rect(0, 0, 420, 300));
            v.IsMeasureValid.Should().BeTrue();
            v.IsArrangeValid.Should().BeTrue();
        }
    }

    // A recognizable 64×64 PNG (teal disc with an orange centre) — exercises the decode + binding and
    // gives the captured snapshot something visible to confirm.
    private const string SampleLogoPng =
        "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAOrSURBVHhe7ZnNS1RRGMb9c1rVqja1qk20qRbVoggKCoIWtSmCgqhdLgwDC2EWQ4ISlpHEhKQLS5hqopGJxhAnMLUms8ymGLQ48Qg3Zp4z3vN5p+DcB36bg577vs95z+d0bO7sFCHTwQ2hkRrADaGRGsANoZEawA2hkRrADaGRGsANoZEawA2h8c8MONLXJ44PDDTBf9MO2mYAEr5dKIj55WURp8VaTdwpFsWZoSGpjyRI3IBLuZwy6Y20Uq+L6+PjYmtXl9SvLxIz4NTgoKgsLXFOVkJVnBselr7hg0QMuDkxwTl4EaaG72rwasD27m4xOj3NcXvV5MLC+nf427Z4NSDp5CPBBF+V4M2ATD7PcSaqXLksxWCDFwOw4Jnq59yM+FZ69pcflTL/iVJXR0akWExxNgClaLLaf3iQFZOn94j8vk0Sr07uEu/7b4jfq3X+t5bCNrmzp0eKyQRnA66NjXFcLYURf33+kJR0K2DQ97dF7qKlcLjimExwMgCjjz1apU+j98Tzg1ukRFXM3+3lriTV19acqsDJAJ25j5G3ST4C64NKqEKOTRcnA3AwUUm37Dfi5bEd4ldthbtt0ovZWSk2XZwMUJU/FjxOyIaZ7gvcdZMwDWwPR9YG7O5Vz0+s6pyMDZhCqiqwvU5bG4DrbZywr3MiLnzJP+ZPNMn2smRtAO7rcfr85KGUhAuqHcF2IbQ2AKewOH181C8l4QIOSHHCDZRj1MHaAJRcnLD3cxIuqAzAwwnHqIO1AVh04oSTHCfhAqZUnPDyxDHqYG2AahfAeb5weJuUiC316hx/oklt3wVwDMb+G6d3ty5LidhQOrufu5aEAeEYdbA2AOBOHqfVr4vrJzlOyAScAVRX5alqVYpNFycDMO9Uwv7NSZmg2v4gPMZwbLo4GYDjp2oaQLZTYerKCe6qpWznP3AyAOA+riNsi7qLIspeZ+Shp5WKFJMJzgbgLq5TBRDWhDcXj0oJN2LyGAIdyGalmExwNgDovgpFwpaGkyIONxG4OeLtwES4jnMspngxAKAU2yms/D6exr0ZgAXR5HHURXiHsN33GW8GgL2ZjPUPobrCS7DrvG/EqwEAlYAnqiSEsvc18hHeDYjA4UR3d9DR/VLJy5xnEjMAYLRcfy9ENfkseSZRAyKwNqAidBdJrCPY4vDszn35pi0GNIKqwNEVDxh4xWkE7TCL/ydJ2m7A/0ZqADeERmoAN4RGagA3hEZqADeERmoAN4RGagA3hMYfrPPojIbDKhcAAAAASUVORK5CYII=";

    // Render a UserControl in a headless top-level and capture a real (Skia) frame to a PNG, returned as
    // the saved path so the snapshot can be eyeballed afterwards.
    private static string CaptureToPng(Control content, string name)
    {
        var window = new Window { Content = content, Width = 480, Height = 360 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull();
        var path = Path.Combine(Path.GetTempPath(), name);
        frame!.Save(path);
        window.Close();
        return path;
    }

    [AvaloniaFact]
    public void AddinPage_WithLogo_DecodesAndRenders()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hexide_logo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "logo.png"), Convert.FromBase64String(SampleLogoPng));
        try
        {
            var registry = Substitute.For<IAddinRegistry>();
            registry.GetStatus(Arg.Any<string>()).Returns(AddinStatus.Loaded);
            var info = new AddinInfo(dir, "Logo Add-in", "A demo add-in with a logo", "1.0", "Tester", LogoPath: "logo.png");
            var vm = new AddinOptionsPageViewModel(registry, info);

            // The decode pipeline (file → SafeImageDecoder → Avalonia Bitmap) runs in a real Avalonia
            // engine here, so a non-null Logo proves end-to-end decode (not just the gate).
            vm.HasLogo.Should().BeTrue();
            vm.Logo.Should().NotBeNull();

            var snapshot = CaptureToPng(new AddinOptionsPageView { DataContext = vm }, "hexide_addin_logo.png");
            new FileInfo(snapshot).Length.Should().BeGreaterThan(0);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [AvaloniaFact]
    public void LanguagePageView_RendersDropdown_Capture()
    {
        // The Language page lives inside the modal Options dialog, which MCP take_snapshot cannot reach
        // a specific page of — so capture a real (Skia) frame of the page to confirm it renders with the
        // header, label, and populated language combo.
        // Use the real service so the dropdown shows the actual entries, incl. "System default".
        // Developer mode on, so the dev-only Pseudo locales appear in this assertion.
        var devMode = Substitute.For<IDeveloperModeService>();
        devMode.IsEnabled.Returns(true);
        var loc = new LocalizationService(developerMode: devMode);
        loc.Apply("system");
        var languageSwitch = new LanguageSwitchService(loc, Substitute.For<IWindowManager>());
        var settings = Substitute.For<ISettingsService>();
        settings.ActiveLanguage.Returns("system");

        var vm = new LanguagePageViewModel(settings, languageSwitch, loc, () => { });
        vm.SelectedLanguage!.DisplayName.Should().Be("System default");
        vm.AvailableLanguages.Select(l => l.Id).Should().Contain(["system", "en", "pseudo", "pseudo-rtl"]);

        var snapshot = CaptureToPng(new LanguagePageView { DataContext = vm }, "hexide_language_page.png");
        new FileInfo(snapshot).Length.Should().BeGreaterThan(0);
    }

    [AvaloniaFact]
    public void LanguageRevertGateView_Capture()
    {
        // Build the gate via the real LocalizationService so the Keep/Revert labels are authentically
        // bilingual (English + the pseudo transform), then capture a real frame.
        var loc = new LocalizationService();
        loc.Apply("en");
        var vm = LanguageRevertGateViewModel.Create(loc, previousId: "en", newId: "pseudo");

        vm.KeepLabel.Should().Contain("Keep");          // previous language (readable)
        vm.KeepLabel.Should().Contain("⟦");             // new language (pseudo, bracketed)

        var snapshot = CaptureToPng(new LanguageRevertGateView { DataContext = vm }, "hexide_language_gate.png");
        new FileInfo(snapshot).Length.Should().BeGreaterThan(0);
    }

    [AvaloniaFact]
    public void AddinPage_WithoutLogo_RendersPlaceholder()
    {
        var (_, _, _, _, registry, _) = Mocks();
        var vm = new AddinOptionsPageViewModel(registry, SampleAddin);   // SampleAddin has no LogoPath

        vm.HasLogo.Should().BeFalse();
        vm.Logo.Should().BeNull();

        var snapshot = CaptureToPng(new AddinOptionsPageView { DataContext = vm }, "hexide_addin_placeholder.png");
        new FileInfo(snapshot).Length.Should().BeGreaterThan(0);
    }

    // A representative verified chain (the dev first-party fingerprints) for rendering the inspector.
    private static TrustChain SampleChain() => new()
    {
        BuildVersion = "4.0.0",
        BuildHash = "59c8adef92873901021c3e3acb1163f9e62df1ca4177001f2358491751cd6244",
        IsFirstParty = true,
        Publisher = new TrustLink("HexIDE (First-Party)", "com.hexide.firstparty",
            "7F80 9EAC 2D98 44FA 4590 D04B 8B47 94D5 977E D72F 0AEC 99D1 14CF B8B7 4CD7 59B1"),
        Intermediate = new TrustLink("HexIDE Marketplace (DEV)", "hexide-marketplace-dev",
            "1A2B 3C4D 5E6F 7081 92A3 B4C5 D6E7 F809 1A2B 3C4D 5E6F 7081 92A3 B4C5 D6E7 F809"),
        Root = new TrustLink("HexIDE root", "",
            "6403 15B0 E770 0633 2EE9 D2C6 0BFB 1232 CD47 34B4 ED03 79EB AC42 3A0A E438 525D"),
    };

    [AvaloniaFact]
    public void TrustChainView_RendersVerifiedChain()
    {
        var vm = new TrustChainViewModel(SampleChain(), null, isRevoked: false, asDialog: true);
        var snapshot = CaptureToPng(new TrustChainView { DataContext = vm }, "hexide_trustchain.png");
        new FileInfo(snapshot).Length.Should().BeGreaterThan(0);
    }

    [AvaloniaFact]
    public void TrustChainView_RendersUnavailableState()
    {
        var vm = new TrustChainViewModel(null, "No verifiable identity — unsigned developer build.",
            isRevoked: false, asDialog: false);
        var snapshot = CaptureToPng(new TrustChainView { DataContext = vm }, "hexide_trustchain_unavailable.png");
        new FileInfo(snapshot).Length.Should().BeGreaterThan(0);
    }

    [AvaloniaFact]
    public void ConsentDialog_OffersTrustDetailsWhenHosted()
    {
        var wm = Substitute.For<IWindowManager>();
        var vm = new AddinConsentDialogViewModel("My Add-in", "1.2.0", "Acme Ltd", "Acme Ltd", Substitute.For<ILocalizationService>(), SampleChain(), wm);

        vm.CanViewTrustDetails.Should().BeTrue();

        var snapshot = CaptureToPng(new AddinConsentDialog { DataContext = vm }, "hexide_consent_trustlink.png");
        new FileInfo(snapshot).Length.Should().BeGreaterThan(0);
    }

    [AvaloniaFact]
    public void AddinPage_WithContributedControl_RendersWithoutErrors()
    {
        var registry = Substitute.For<IAddinRegistry>();
        registry.GetStatus(Arg.Any<string>()).Returns(AddinStatus.Loaded);
        var marker = new TextBlock { Text = "addin-contributed" };
        var vm = new AddinOptionsPageViewModel(registry, SampleAddin, () => marker);
        var view = new AddinOptionsPageView { DataContext = vm };

        view.Measure(new Size(460, 320));
        view.Arrange(new Rect(0, 0, 460, 320));

        view.IsMeasureValid.Should().BeTrue();
        view.IsArrangeValid.Should().BeTrue();
        vm.HasContributedContent.Should().BeTrue();
        vm.ContributedContent.Should().BeSameAs(marker);
    }
}
