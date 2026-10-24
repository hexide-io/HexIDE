using System;
using Serilog;

namespace HexIDE.Utils;

internal static class LoggedTimerCallback
{
    public static void Run(string purpose, Action callback)
    {
        try
        {
            callback();
        }
        catch (Exception e)
        {
            Log.Error(e, "Timer callback failed: {TimerPurpose}", purpose);
        }
    }
}
