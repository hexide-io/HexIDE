using System;
using System.Globalization;
using System.Threading;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The locale a running VB6 program formats and reads text in: the user's regional settings, as VB6 takes them
/// (#777). <c>CStr(#1/2/2003#)</c> is <c>02/01/2003</c> under en-GB, measured.
/// </summary>
/// <remarks>
/// <para>
/// It is the interpreter's own, and deliberately not the thread's culture. The IDE pins the thread's culture to the
/// invariant one, so that the IDE's own language cannot change what a program computes
/// (<c>openspec/specs/localization</c>), and the parsing of source literals relies on that pin (#781). Event
/// procedures run on the UI thread, from Avalonia's events, so a culture set at the start of a run would not reach
/// them either.
/// </para>
/// <para>
/// It resolves in three steps:
/// <list type="number">
/// <item>a scope opened with <see cref="Use"/>, which flows with the code that opened it, as the thread's culture
/// does. That is how a test pins a locale without disturbing tests that run beside it;</item>
/// <item>the <see cref="Default"/> the host sets once at startup, from the user's culture before it pins its own;</item>
/// <item>the thread's culture, which is what everything read before this existed, so nothing else moves.</item>
/// </list>
/// </para>
/// <para>
/// What it governs: the text a value becomes (<c>CStr</c>, <c>&amp;</c>, <c>Format</c>), and text a program reads
/// back as a number or a date (<c>CDbl</c>, <c>CDate</c>, <c>IsDate</c>, a String stored into a number). Not
/// <c>Str</c> and <c>Val</c>, which VB6 fixes to a point, and not source literals, which VB6 always writes with one.
/// </para>
/// </remarks>
public static class VbLocale
{
    private static readonly AsyncLocal<CultureInfo?> Scoped = new();

    /// <summary>The user's locale, set by the host at startup. Null until a host sets it.</summary>
    public static CultureInfo? Default { get; set; }

    /// <summary>The locale the running program uses now.</summary>
    public static CultureInfo Current => Scoped.Value ?? Default ?? CultureInfo.CurrentCulture;

    /// <summary>
    /// Runs the code that follows in <paramref name="culture"/> until the result is disposed. It flows into awaited
    /// work started from here, and does not reach code running beside it.
    /// </summary>
    public static IDisposable Use(CultureInfo culture)
    {
        var previous = Scoped.Value;
        Scoped.Value = culture;
        return new Restore(previous);
    }

    private sealed class Restore(CultureInfo? previous) : IDisposable
    {
        public void Dispose() => Scoped.Value = previous;
    }
}
