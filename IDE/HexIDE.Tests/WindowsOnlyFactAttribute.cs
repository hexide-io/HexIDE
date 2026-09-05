namespace HexIDE.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that skips — <b>visibly</b> — anywhere but Windows.
///
/// <para>
/// For behaviour that is genuinely platform-specific by design rather than by accident: filesystem case
/// sensitivity is the case this was written for, where the product deliberately answers differently per
/// host and asserting one answer everywhere makes a correct implementation fail.
/// </para>
///
/// <para>
/// Deliberately a skip rather than an early <c>return</c>, matching <c>ForeignServerFactAttribute</c>. A
/// test that returns early passes, and a passing test that asserted nothing is the failure this project
/// keeps warning about. A skip says so in the runner output.
/// </para>
/// </summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows-only: this pins behaviour the product deliberately varies by host filesystem.";
    }
}
