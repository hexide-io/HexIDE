using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit.v3;

[assembly: HexIDE.Integration.Tests.CultureGuard]

namespace HexIDE.Integration.Tests;

/// <summary>Catch dispatcher culture leaks at the next test's boundary (#1612).</summary>
public sealed class CultureGuardAttribute : BeforeAfterTestAttribute
{
    private static string expectedCulture = "";
    private static string expectedUICulture = "";

    // Capture before the shared Avalonia dispatcher starts running tests, never from a neighbour's context.
    [ModuleInitializer]
    public static void CaptureHostCultures()
    {
        expectedCulture = CultureInfo.CurrentCulture.Name;
        expectedUICulture = CultureInfo.CurrentUICulture.Name;
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
}
