using System;
using HexIDE.Localization;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.IDE;

/// <summary>
/// What the runtime-error dialog says about whatever stopped a running program. Built in one place so a form
/// and Sub Main describe the same error the same way.
/// </summary>
internal static class RunErrorText
{
    /// <summary>A VB6 run-time error, followed by the statement that raised it where there is one.</summary>
    public static string For(VBRunTimeException e)
    {
        // Sliced from the context's OWN input stream, not from the form's code.
        //
        // Two bugs in one line, both reachable the moment a form can call into a .bas (#220). The offsets
        // belong to whichever module raised, so indexing the FORM's source threw ArgumentOutOfRangeException
        // inside the dialog's handler, which meant no dialog at all: a runtime error inside a module vanished
        // silently and the program appeared to carry on. And the length was `Stop - Start`, but ANTLR's
        // StopIndex is INCLUSIVE, so every message lost its last character ("NoSuchSub" rendered as
        // "NoSuchSu").
        //
        // GetText takes an inclusive interval, so it fixes the truncation as well as the source.
        var at = e.Context is { Start: { InputStream: { } input } start, Stop: { } stop }
              && stop.StopIndex >= start.StartIndex
            ? "\n\nat " + input.GetText(new Antlr4.Runtime.Misc.Interval(start.StartIndex, stop.StopIndex))
            : "";   // built-in errors carry no parse context
        return e.Message + at;
    }

    /// <summary>
    /// An exception that is not a VB6 error (#754): a construct HexIDE does not implement, or a fault in the
    /// engine. Neither is trappable, and neither is given an <c>Err</c> number a program could branch on.
    /// </summary>
    /// <remarks>
    /// <para>A construct HexIDE does not implement says so, with no VB6 error number, because VB6 would have
    /// run it. Numbering it would blame the program for HexIDE's gap.</para>
    /// <para>Anything else is reported as VB6's own "Internal error" (51), which is what VB6 says when its
    /// runtime fails rather than the program. The exception's type and message follow, because they are what
    /// a bug report needs. The text has the shape <see cref="VBRunTimeException"/> gives a VB6 error, but no
    /// such exception is made: one would read as trappable to anyone who later threw it.</para>
    /// </remarks>
    public static string ForFault(Exception e, ILocalizationService localization)
    {
        if (e is NotImplementedException)
            return string.Format(localization.GetString("Str.ProjectRunner.NotImplementedYet"), e.Message);

        var internalError = VBStandardError.InternalError;
        return $"Run-time error '{internalError.ErrNo}':\n\n{internalError.Description}\n\n"
             + $"{e.GetType().FullName}: {e.Message}";
    }
}
