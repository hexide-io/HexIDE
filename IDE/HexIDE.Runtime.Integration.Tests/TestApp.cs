using System;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Simple;

[assembly: AvaloniaTestApplication(typeof(HexIDE.Runtime.Integration.Tests.TestApp))]

namespace HexIDE.Runtime.Integration.Tests;

/// <summary>
/// The application a made program runs in: SimpleTheme and the runtime dictionaries the Standalone runner's
/// <c>App.axaml</c> merges, and nothing of the IDE's. <see cref="TestAppTests"/> keeps the list equal to the runner's.
/// </summary>
/// <remarks>
/// Two things differ from the runner, both for the tests' sake:
/// <list type="bullet">
/// <item>The theme variant is Light where the runner follows the host, so a drawing doesn't change with dark mode.</item>
/// <item>Drawing is real, under Skia, so a test can read the pixels a control drew.</item>
/// </list>
/// </remarks>
public class TestApp : Application
{
    // The runtime's assembly, named through one of its types. Naming a type loads the assembly, and Avalonia finds an
    // avares:// address's compiled XAML only in an assembly already loaded: with the bare address loaded first, every
    // test of a class that had touched nothing of the runtime's failed to start, and xunit's runner, not MTP, said so.
    private static readonly string Runtime = typeof(HexIDE.Runtime.BuiltinTypes.VBColorExtensions).Assembly.GetName().Name!;

    /// <summary>The dictionaries under <c>HexIDE.Runtime/BuiltinControls/</c> that the runner merges, in its order.</summary>
    internal static readonly string[] RunnerDictionaries = ["SystemColors.axaml", "VBLabel.axaml", "Resources.axaml"];

    public override void Initialize()
    {
        HexIDE.Integration.Tests.HeadlessFontWarmUp.Run();
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new SimpleTheme());
        foreach (var dictionary in RunnerDictionaries)
        {
            Resources.MergedDictionaries.Add(new ResourceInclude(new Uri($"avares://{Runtime}/"))
            {
                Source = new Uri($"avares://{Runtime}/BuiltinControls/{dictionary}"),
            });
        }
        HexIDE.Integration.Tests.WindowLeakGuard.Track();
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia();
}
