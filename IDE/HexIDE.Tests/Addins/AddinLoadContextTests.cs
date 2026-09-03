using System.Reflection;
using HexIDE.Addins;

namespace HexIDE.Tests.Addins;

/// <summary>
/// Which copy of an assembly an add-in gets — the host's, or its own.
///
/// <para>
/// The old policy deferred to <c>Default.Assemblies</c> by simple name: if the host had anything called
/// that, the host won. Version-blind, so a package shipping v9 silently got the host's v8 and found out
/// as a <c>MissingMethodException</c> at the call site; and order-dependent, because
/// <c>Default.Assemblies</c> is whatever is loaded <em>so far</em> — a host assembly loaded lazily after
/// an add-in had resolved its own copy left two assemblies of one simple name live in different contexts.
/// </para>
///
/// <para>
/// Neither symptom appears with one first-party add-in, which is exactly why this is worth pinning now:
/// which copy wins is observable to every third-party package, so it stops being changeable the moment a
/// marketplace exists.
/// </para>
/// </summary>
public class AddinLoadContextTests
{
    private static AddinManifest ManifestListing(params string[] files) => new()
    {
        Id = "com.example.test",
        Files = files.Select(f => new AddinFileHash { Path = f, Sha256 = new string('0', 64) }).ToArray(),
    };

    // ---- the sharing policy ------------------------------------------------------------------------

    [Theory]
    [InlineData("HexIDE.Core")]        // the contract itself — IAddin, IHexIdeHost and everything they carry
    [InlineData("HexIDE")]
    [InlineData("HexIDE.Runtime")]
    [InlineData("System")]
    [InlineData("System.Text.Json")]
    [InlineData("netstandard")]
    [InlineData("mscorlib")]
    public void TheContractAndTheFrameworkAreAlwaysTheHostsCopy(string name)
        => AddinLoadContext.MustShareWithHost(name).Should().BeTrue();

    [Theory]
    [InlineData("Avalonia")]
    [InlineData("Avalonia.Base")]
    [InlineData("Avalonia.Controls")]
    public void AvaloniaIsSharedEvenThoughItAppearsInNoContractSignature(string name)
    {
        // The non-obvious entry. IToolWindowContributor.Register(…, Func<object> factory) and
        // IOptionsPageContributor.RegisterOptionsPage(Func<object>) are typed as `object` on purpose, so
        // the contract stays UI-framework-agnostic — but the add-in returns a control and the HOST casts
        // it. The dependency is invisible to the compiler and mandatory at runtime, so an add-in
        // resolving its own Avalonia would fail that cast rather than fail to build.
        AddinLoadContext.MustShareWithHost(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("Newtonsoft.Json")]
    [InlineData("Autofac")]
    [InlineData("Microsoft.Extensions.DependencyInjection")]
    [InlineData("Serilog")]
    [InlineData("SomeVendor.Proprietary.Sdk")]
    public void EverythingElseIsTheAddinsOwnBusiness(string name)
    {
        // Including a DI container: an add-in that wants dependency injection brings its own, at its own
        // version, and it cannot collide with the host's — the host's is a Pure.DI compile-time
        // composition with no runtime container to collide with in the first place.
        AddinLoadContext.MustShareWithHost(name).Should().BeFalse();
    }

    [Fact]
    public void TheSharingDecisionIsCaseInsensitive()
    {
        // Assembly simple names are compared case-insensitively by the runtime; a policy that was not
        // would be a lottery decided by how a package spelled its file name.
        AddinLoadContext.MustShareWithHost("hexide.core").Should().BeTrue();
        AddinLoadContext.MustShareWithHost("AVALONIA.CONTROLS").Should().BeTrue();
        AddinLoadContext.MustShareWithHost("newtonsoft.json").Should().BeFalse();
    }

    [Fact]
    public void TheDecisionDoesNotDependOnWhatIsLoadedYet()
    {
        // The property the old policy lacked. MustShareWithHost consults an enumerated set and nothing
        // else, so it answers identically before and after any host assembly happens to load — which is
        // what stops two copies of one simple name going live in different contexts.
        var before = AddinLoadContext.MustShareWithHost("Newtonsoft.Json");
        _ = typeof(System.Text.Json.JsonDocument).Assembly;   // force a load
        AddinLoadContext.MustShareWithHost("Newtonsoft.Json").Should().Be(before);
    }

    // ---- shadow reporting -------------------------------------------------------------------------

    [Fact]
    public void APackageCarryingASharedAssemblyStillLoadsAndTheShadowingIsRecorded()
    {
        // Shadowing is correct — those types must unify — but silent, and the symptom (a method that
        // exists on the author's machine and not at runtime) names no cause. Recording it is what lets
        // the registry say so.
        var alc = new AddinLoadContext(AppContext.BaseDirectory, ManifestListing("HexIDE.Core.dll"));

        var resolved = alc.LoadFromAssemblyName(new AssemblyName("HexIDE.Core"));

        resolved.Should().BeSameAs(typeof(IAddin).Assembly, "the host's copy is the one that must win");
        alc.ShadowedByHost.Should().ContainSingle().Which.Should().Be("HexIDE.Core");
    }

    [Fact]
    public void NothingIsReportedWhenThePackageCarriesNoSharedAssembly()
    {
        var alc = new AddinLoadContext(AppContext.BaseDirectory, ManifestListing("Contoso.Widgets.dll"));

        alc.LoadFromAssemblyName(new AssemblyName("HexIDE.Core"));

        alc.ShadowedByHost.Should().BeEmpty("the package shipped no copy of it, so nothing was shadowed");
    }

    [Fact]
    public void ARepeatedlyRequestedShadowIsReportedOnce()
    {
        var alc = new AddinLoadContext(AppContext.BaseDirectory, ManifestListing("HexIDE.Core.dll"));

        alc.LoadFromAssemblyName(new AssemblyName("HexIDE.Core"));
        alc.LoadFromAssemblyName(new AssemblyName("HexIDE.Core"));

        alc.ShadowedByHost.Should().ContainSingle("it is a diagnostic, not a resolution counter");
    }

    [Fact]
    public void OnlyManagedAssembliesInTheManifestAreConsidered()
    {
        // The manifest lists every file in the package — icons, READMEs, .sig files. Only .dll/.exe are
        // candidates for assembly resolution.
        var alc = new AddinLoadContext(AppContext.BaseDirectory,
            ManifestListing("HexIDE.Core.png", "addin.json", "HexIDE.Core.dll.sig"));

        alc.LoadFromAssemblyName(new AssemblyName("HexIDE.Core"));

        alc.ShadowedByHost.Should().BeEmpty("none of those is an assembly the package could have shipped");
    }
}
