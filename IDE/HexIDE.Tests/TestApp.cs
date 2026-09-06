using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(HexIDE.Tests.TestApp))]

namespace HexIDE.Tests;

/// <summary>
/// The Avalonia application these tests run against, and the reason they no longer race.
///
/// <para>
/// Avalonia binds its dispatcher to whichever thread sets it up, and every later access must come from
/// that thread. This project used to call <c>SetupWithoutStarting</c> from whichever test class asked
/// first — so when that call landed on a drifted async continuation, all 282 Avalonia-dependent tests
/// failed together with <c>VerifyAccess</c>, at random, roughly one CI run in twelve
/// (hexide-io/HexIDE#286, #292).
/// </para>
///
/// <para>
/// <c>[AvaloniaFact]</c> removes that invariant rather than trying to satisfy it. Avalonia caches one
/// session per assembly and runs every such test on the single thread that session owns — measured, two
/// classes and a post-await continuation all on one thread — so there is no first caller to get it wrong
/// and no drift for a later test to inherit. <c>AvaloniaThreadAffinityTests</c> asserts that directly.
/// </para>
///
/// <para>
/// Deliberately plainer than <c>HexIDE.Integration.Tests</c>'s app, which loads SimpleTheme and Skia
/// because it captures rendered frames. These are ViewModel tests: they need the Avalonia
/// infrastructure — the <c>AssetLoader</c> for <c>avares://</c> URIs, the dispatcher, the property
/// system — and nothing that draws. This mirrors exactly what the old setup built, so the migration
/// changes which thread the tests run on and nothing else.
/// </para>
///
/// <para>
/// <b>No isolation attribute, and that is a measured choice rather than an oversight.</b> The framework
/// default is <c>PerTest</c>, which builds a fresh <c>Application</c> and <c>Dispatcher</c> for every
/// test. <c>PerAssembly</c> was tried first, on the theory that fewer dispatchers meant fewer chances to
/// bind something to the wrong one — it did not fix anything, because the actual fault was
/// <c>IconFactory</c> holding a static brush across dispatcher lifetimes. With that fixed, both settings
/// pass 8 runs of the full suite each, so the default stands: a setting kept "just in case" is one
/// nobody can justify removing later. <c>PerTest</c> is also the harder case, since it changes
/// dispatcher most often, which makes 8 green runs there the stronger evidence.
/// </para>
/// </summary>
public class TestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
