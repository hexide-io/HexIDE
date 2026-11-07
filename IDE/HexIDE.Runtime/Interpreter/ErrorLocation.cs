using System;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The module and line an error was raised at, counted from the top of the file as the code window numbers it (#955).
/// The run-time error dialog names the statement but not where it is, and once the dialog has closed neither a person
/// nor <c>get_last_runtime_error</c> could reach the line, though the interpreter walks it.
/// </summary>
/// <remarks>
/// Carried on the exception's <see cref="Exception.Data"/>, so a compile error met mid-run and a construct HexIDE does
/// not implement carry it as a run-time error does. The innermost statement the error passes out of notes it, first;
/// the statements it passes on the way out, its callers', leave it alone.
/// </remarks>
public readonly record struct ErrorLocation(string Module, int Line)
{
    private const string Key = "HexIDE.ErrorLocation";
    private const string StatementKey = "HexIDE.UnimplementedStatement";

    /// <summary>The innermost unimplemented statement, sliced from its own source, if one was reached (#758).</summary>
    public static string? StatementOf(Exception? error) =>
        error is null ? null : StatementOf(error.InnerException) ?? error.Data[StatementKey] as string;

    /// <summary>
    /// Where <paramref name="error"/> was raised, or null when no statement it passed noted it. An inner exception's comes
    /// first: <see cref="VBRunTimeException.At"/> wraps an error a statement nearer it may have noted already.
    /// </summary>
    public static ErrorLocation? Of(Exception? error) =>
        error is null ? null : Of(error.InnerException) ?? (error.Data[Key] is ErrorLocation at ? at : null);

    /// <summary>
    /// Notes where <paramref name="error"/> was raised, unless a statement nearer has. Answers false, for an exception
    /// filter that only looks.
    /// </summary>
    internal static bool Note(Exception error, string module, int line, string? statement = null)
    {
        try
        {
            if (!error.Data.Contains(Key))
                error.Data[Key] = new ErrorLocation(module, line);
            if (statement is not null && !error.Data.Contains(StatementKey))
                error.Data[StatementKey] = statement;
        }
        catch (Exception)
        {
            // An exception whose Data refuses entries goes unlocated rather than changing what the run does.
        }
        return false;
    }
}
