using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Serilog;

namespace HexIDE.Utils;

internal static class TaskExtensions
{
    public static void ListenErrors(this Task t,
        [CallerMemberName] string? caller = null,
        [CallerFilePath] string? callerFile = null,
        [CallerLineNumber] int? callerLineNumber = null)
    {
        if (t.IsCompleted)
        {
            if (t.IsFaulted && t.Exception is { } aggregateException)
            {
                if (aggregateException.InnerExceptions.Count == 1)
                {
                    Log.Error(aggregateException.InnerExceptions[0],
                        "Error in {Caller} at {CallerFile}:{CallerLine}",
                        caller, callerFile, callerLineNumber);
                }
                else
                {
                    Log.Error(aggregateException,
                        "Error in {Caller} at {CallerFile}:{CallerLine}",
                        caller, callerFile, callerLineNumber);
                }
            }

            return;
        }

        t.ContinueWith(
            e =>
            {
                Log.Error(e.Exception,
                    "Error in {Caller} at {CallerFile}:{CallerLine}",
                    caller, callerFile, callerLineNumber);
            }, TaskContinuationOptions.OnlyOnFaulted);
    }
}