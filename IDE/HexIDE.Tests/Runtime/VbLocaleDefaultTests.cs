using System.Globalization;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Tests.Runtime;

/// <summary>
/// The locale the host hands the interpreter at startup (#777). Here rather than beside the other locale tests
/// because <see cref="VbLocale.Default"/> is process-wide, and this project runs its tests one at a time.
/// </summary>
public class VbLocaleDefaultTests
{
    private static readonly System.DateTime JanuaryTheSecond = new(2003, 1, 2);

    [Fact]
    public void The_hosts_locale_wins_over_the_thread_culture_and_a_scope_wins_over_both()
    {
        var savedDefault = VbLocale.Default;
        var savedThread = CultureInfo.CurrentCulture;
        try
        {
            // The IDE's arrangement: the user is in en-GB, and every thread is pinned to the invariant culture.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            VbLocale.Default = CultureInfo.GetCultureInfo("en-GB");

            VbDisplay.ToText(new Vb6Value(VbDate.FromDateTime(JanuaryTheSecond))).Should().Be("02/01/2003");

            using (VbLocale.Use(CultureInfo.GetCultureInfo("en-US")))
                VbDisplay.ToText(new Vb6Value(VbDate.FromDateTime(JanuaryTheSecond))).Should().Be("1/2/2003");

            VbDisplay.ToText(new Vb6Value(VbDate.FromDateTime(JanuaryTheSecond))).Should().Be("02/01/2003", "the scope has closed");
        }
        finally
        {
            VbLocale.Default = savedDefault;
            CultureInfo.CurrentCulture = savedThread;
        }
    }

    [Fact]
    public void With_no_host_the_thread_culture_is_used_as_before()
    {
        var savedDefault = VbLocale.Default;
        var savedThread = CultureInfo.CurrentCulture;
        try
        {
            VbLocale.Default = null;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");

            VbLocale.Current.Name.Should().Be("en-GB");
        }
        finally
        {
            VbLocale.Default = savedDefault;
            CultureInfo.CurrentCulture = savedThread;
        }
    }
}
