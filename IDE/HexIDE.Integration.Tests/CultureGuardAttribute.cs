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
        Check(CultureInfo.DefaultThreadCurrentCulture?.Name, expectedDefaultCulture, "DefaultThreadCurrentCulture", test);
        Check(CultureInfo.DefaultThreadCurrentUICulture?.Name, expectedDefaultUICulture, "DefaultThreadCurrentUICulture", test);
    }

    private static void Check(string? actual, string? expected, string member, IXunitTest test) =>
        actual.Should().Be(expected,
            "{0} must leave {1} at its host value; left '{2}', expected '{3}' (empty means invariant, <null> means no default)",
            test.TestDisplayName, member, actual ?? "<null>", expected ?? "<null>");
}
