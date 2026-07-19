using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace HexIDE.Infrastructure;

/// <summary>
/// Centralized Serilog configuration for the HexIDE IDE process.
/// Log files are written to {LocalAppData}/HexIDE/logs/ide/.
/// </summary>
public static class LoggingSetup
{
    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    private const long FileSizeLimitBytes = 50 * 1024 * 1024; // 50 MB
    private const int RetainedFileCountLimit = 7;

    /// <summary>
    /// The <see cref="ILoggerFactory"/> backed by Serilog.
    /// Available after <see cref="Initialise"/> has been called.
    /// Used by Pure.DI to produce <see cref="ILogger{T}"/> instances.
    /// </summary>
    public static ILoggerFactory LoggerFactory { get; private set; } = NullLoggerFactory.Instance;

    /// <summary>
    /// Configures the global Serilog logger and the MEL <see cref="ILoggerFactory"/>.
    /// Call once at application startup, before DI initialisation.
    /// Each IDE session creates a new log file with a unique timestamp.
    /// </summary>
    public static void Initialise()
    {
        var logDir = GetLogDirectory("ide");
        var minLevel = GetMinimumLevel();
        var sessionStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var logPath = Path.Combine(logDir, $"ide-{sessionStamp}.log");

        var config = new LoggerConfiguration()
            .MinimumLevel.Is(minLevel)
            .WriteTo.File(
                path: logPath,
                fileSizeLimitBytes: FileSizeLimitBytes,
                outputTemplate: OutputTemplate,
                shared: false);

        var serilogLogger = config.CreateLogger();

        // Set the static Serilog logger for use in static contexts (e.g. ListenErrors)
        Log.Logger = serilogLogger;

        // Create the MEL ILoggerFactory so Pure.DI can produce ILogger<T> instances
        LoggerFactory = new SerilogLoggerFactory(serilogLogger);

        // Prune old session logs (keep most recent N files)
        PruneOldLogs(logDir, "ide-*.log", RetainedFileCountLimit);
    }

    /// <summary>
    /// Flushes and closes the Serilog pipeline. Call on application shutdown.
    /// </summary>
    public static void Shutdown()
    {
        Log.CloseAndFlush();
        (LoggerFactory as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Returns the cross-platform log directory for the given subsystem.
    /// Creates the directory if it does not exist.
    /// </summary>
    public static string GetLogDirectory(string subsystem)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logDir = Path.Combine(localAppData, "HexIDE", "logs", subsystem);
        Directory.CreateDirectory(logDir);
        return logDir;
    }

    /// <summary>
    /// Reads HEXIDE_LOG_LEVEL environment variable to override the default log level.
    /// Supported values: Verbose, Debug, Information (default), Warning, Error, Fatal.
    /// </summary>
    private static LogEventLevel GetMinimumLevel()
    {
        var envLevel = Environment.GetEnvironmentVariable("HEXIDE_LOG_LEVEL");
        if (!string.IsNullOrWhiteSpace(envLevel) &&
            Enum.TryParse<LogEventLevel>(envLevel, ignoreCase: true, out var parsed))
        {
            return parsed;
        }
        return LogEventLevel.Information;
    }

    /// <summary>
    /// Deletes old log files in the directory, keeping only the most recent
    /// <paramref name="keepCount"/> files matching <paramref name="pattern"/>.
    /// </summary>
    private static void PruneOldLogs(string directory, string pattern, int keepCount)
    {
        try
        {
            var files = new DirectoryInfo(directory)
                .GetFiles(pattern)
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(keepCount);

            foreach (var file in files)
            {
                try { file.Delete(); }
                catch { /* best effort */ }
            }
        }
        catch
        {
            // Don't let log cleanup prevent app startup
        }
    }
}
