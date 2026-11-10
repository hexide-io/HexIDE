using System.Globalization;

namespace HexIDE.IDE;

/// <summary>The user's regional settings, which a running program formats and reads text in (#777).</summary>
public static class UserRegion
{
    /// <summary>
    /// Set once by the host as it starts, before it pins every thread's culture to the invariant one, because after that the
    /// user's culture can no longer be read. Null until a host sets it.
    /// </summary>
    public static CultureInfo? Culture { get; set; }
}
