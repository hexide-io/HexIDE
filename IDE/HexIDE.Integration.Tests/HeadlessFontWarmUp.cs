using System;
using System.Reflection;
using Avalonia.Media;

namespace HexIDE.Integration.Tests;

internal static class HeadlessFontWarmUp
{
    internal static void Run()
    {
        var family = FontManager.Current.DefaultFontFamily.Name;
        // Avalonia hides these APIs from its reference assemblies. Probe the same uncached creation
        // stages as SystemFontCollection, including TryCreate's actual null result (#1578).
        var createGlyph = (typeof(GlyphTypeface).GetMethod("TryCreate", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw Missing("GlyphTypeface.TryCreate"))
            .CreateDelegate<Func<IPlatformTypeface, FontSimulations, GlyphTypeface?>>();
        Run(family, () => CreatePlatformTypeface(family), createGlyph);
    }

    internal static IPlatformTypeface? CreatePlatformTypeface(string family)
    {
        var platform = (typeof(FontManager).GetProperty("PlatformImpl", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw Missing("FontManager.PlatformImpl"))
            .GetValue(FontManager.Current) ?? throw Missing("a FontManager.PlatformImpl value");
        object?[] args = [family, FontStyle.Normal, FontWeight.Bold, FontStretch.Normal, null];
        var method = platform.GetType().GetMethod("TryCreateGlyphTypeface",
            [typeof(string), typeof(FontStyle), typeof(FontWeight), typeof(FontStretch), typeof(IPlatformTypeface).MakeByRefType()])
            ?? throw Missing(platform.GetType().Name + ".TryCreateGlyphTypeface");
        return (bool)method.Invoke(platform, args)! ? (IPlatformTypeface?)args[4] : null;
    }

    // These members are internal to Avalonia, so an upgrade can rename them. Say which one went, rather than fail every
    // UI test with a bare NullReferenceException at startup.
    private static InvalidOperationException Missing(string member) => new(
        $"Headless font startup probe (#1578): Avalonia no longer has {member}; update HeadlessFontWarmUp to its font API.");

    internal static void Run(string family, Func<IPlatformTypeface?> createPlatformTypeface,
        Func<IPlatformTypeface, FontSimulations, GlyphTypeface?> createGlyphTypeface)
    {
        using var platformTypeface = createPlatformTypeface();
        if (platformTypeface == null)
            throw new InvalidOperationException($"Headless font startup: default family '{family}', Normal/Bold/Normal: " +
                "no platform typeface; GlyphTypeface.TryCreate: not called.");

        var glyphTypeface = createGlyphTypeface(platformTypeface, FontSimulations.None);
        if (glyphTypeface == null)
        {
            throw new InvalidOperationException($"Headless font startup: default family '{family}', Normal/Bold/Normal: " +
                "GlyphTypeface.TryCreate: null.");
        }
    }
}
