using System.Reflection;

namespace HexIDE.Utils;

/// <summary>
/// The version this build reports about itself, read from the assembly rather than duplicated.
/// <para>
/// <c>Directory.Build.props</c> sets <c>&lt;Version&gt;</c>; CI additionally passes a
/// <c>SourceRevisionId</c>, which the SDK appends to <c>AssemblyInformationalVersion</c> as
/// <c>0.1.0+abc1234</c>. A local build simply has no suffix. That distinction is the whole point:
/// an alpha bug report that quotes "0.1.0" is nearly useless, and one that quotes "0.1.0 (abc1234)"
/// identifies an exact commit.
/// </para>
/// </summary>
public static class BuildInfo
{
    static BuildInfo()
    {
        var informational = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            Version = typeof(BuildInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            Commit = null;
        }
        else
        {
            var plus = informational.IndexOf('+');
            Version = plus >= 0 ? informational[..plus] : informational;
            Commit = plus >= 0 && plus + 1 < informational.Length ? informational[(plus + 1)..] : null;
        }
    }

    /// <summary>The semantic version, e.g. <c>0.1.0</c>.</summary>
    public static string Version { get; }

    /// <summary>The short commit the build came from, or null for a local build.</summary>
    public static string? Commit { get; }

    /// <summary>
    /// Version for display, with the commit when there is one: <c>0.1.0</c> or <c>0.1.0 (abc1234)</c>.
    /// Deliberately carries no words — appending "Version" or "build" here would be a new user-facing
    /// string, and therefore a localization key needed in every shipped pack.
    /// </summary>
    public static string Display => Commit is null ? Version : $"{Version} ({Commit})";
}
