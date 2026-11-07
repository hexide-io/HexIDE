using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using AwesomeAssertions.Execution;
using Xunit.v3;

[assembly: HexIDE.Integration.Tests.CultureGuard]

namespace HexIDE.Integration.Tests;

/// <summary>Catch culture leaks at test boundaries and name the leaking test (#1612, #2322).</summary>
public sealed class CultureGuardAttribute : BeforeAfterTestAttribute
{
    private static string expectedCulture = "";
    private static string expectedUICulture = "";
    private static string? expectedDefaultCulture;
    private static string? expectedDefaultUICulture;

    // Capture before the shared Avalonia dispatcher starts running tests, never from a neighbour's context.
    [ModuleInitializer]
    public static void CaptureHostCultures()
    {
        expectedCulture = CultureInfo.CurrentCulture.Name;
        expectedUICulture = CultureInfo.CurrentUICulture.Name;
        expectedDefaultCulture = CultureInfo.DefaultThreadCurrentCulture?.Name;
        expectedDefaultUICulture = CultureInfo.DefaultThreadCurrentUICulture?.Name;
    }

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        CultureInfo.CurrentCulture.Name.Should().Be(expectedCulture,
            "{0} must start in the host culture; CurrentCulture was '{1}' (empty means invariant)",
            test.TestDisplayName, CultureInfo.CurrentCulture.Name);
        CultureInfo.CurrentUICulture.Name.Should().Be(expectedUICulture,
            "{0} must start in the host culture; CurrentUICulture was '{1}' (empty means invariant)",
            test.TestDisplayName, CultureInfo.CurrentUICulture.Name);
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        using var scope = new AssertionScope();
        Check(CultureInfo.CurrentCulture.Name, expectedCulture, "CurrentCulture", test);
        Check(CultureInfo.CurrentUICulture.Name, expectedUICulture, "CurrentUICulture", test);
        Check(CultureInfo.DefaultThreadCurrentCulture?.Name, expectedDefaultCulture, "DefaultThreadCurrentCulture", test, processWide: true);
        Check(CultureInfo.DefaultThreadCurrentUICulture?.Name, expectedDefaultUICulture, "DefaultThreadCurrentUICulture", test, processWide: true);
    }

    private static void Check(string? actual, string? expected, string member, IXunitTest test, bool processWide = false) =>
        actual.Should().Be(expected,
            "{0} must leave {1} at its host value; left '{2}', expected '{3}' (empty means invariant, <null> means no default){4}",
            test.TestDisplayName, member, actual ?? "<null>", expected ?? "<null>",
            processWide
                ? ". It is process-wide, so another test may have changed it while this one ran: one that sets it belongs in "
                  + "ProcessCultureCollection, which runs alone (#2500)"
                : "");
}

/// <summary>
/// The tests that set a process-wide culture default, or run <c>App.Initialize</c>, which pins both to the invariant
/// culture. A default changes for every thread at once, so while such a test ran, a plain test on another thread saw it
/// change, and the guard's After check named that test instead (#2500). xunit runs a collection that disables
/// parallelisation on its own, after the parallel ones. <see cref="ProcessCultureCollectionTests"/> keeps it complete.
/// </summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class ProcessCultureCollection;
