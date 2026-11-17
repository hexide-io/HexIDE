using System.Runtime.InteropServices;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The Windows system locale and the system and user interface languages. A UserControl's <c>Ambient.LocaleID</c>
/// follows one of them, or none, in a way not yet told apart (#4266): it read 1033 under en-GB and de-DE user locales,
/// on a Windows whose system locale and interface languages were all US English (measured on vb6.exe SP6).
/// </summary>
internal static partial class WindowsLanguages
{
    internal readonly record struct Languages(int SystemLocale, int SystemInterface, int UserInterface)
    {
        internal bool AllUsEnglish => SystemLocale == 1033 && SystemInterface == 1033 && UserInterface == 1033;
    }

    private sealed record Override(Languages? Value);

    private static readonly AsyncLocal<Override?> Scoped = new();

    /// <summary>The host's languages, or null off Windows, which has none of these.</summary>
    internal static Languages? Current => Scoped.Value is { } scoped ? scoped.Value
        : OperatingSystem.IsWindows()
            ? new Languages((int)GetSystemDefaultLCID(), GetSystemDefaultUILanguage(), GetUserDefaultUILanguage())
            : null;

    /// <summary>Puts <paramref name="languages"/> in place of the host's until disposed; null stands for a host off
    /// Windows. For tests, which must not depend on the machine they run on.</summary>
    internal static IDisposable Use(Languages? languages)
    {
        var previous = Scoped.Value;
        Scoped.Value = new Override(languages);
        return new Restore(previous);
    }

    private sealed class Restore(Override? previous) : IDisposable
    {
        public void Dispose() => Scoped.Value = previous;
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetSystemDefaultLCID();

    [LibraryImport("kernel32.dll")]
    private static partial ushort GetSystemDefaultUILanguage();

    [LibraryImport("kernel32.dll")]
    private static partial ushort GetUserDefaultUILanguage();
}
