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

    // Three limits, multiplied, are the whole disk budget — state them together so the total is
    // deliberate rather than emergent:
    //     10 MB per part x 5 parts per session x 7 sessions = 350 MB per log directory.
    // A part fills, the sink rolls to the next one, and Serilog drops the session's OLDEST part.
    // Without rolling a full part simply stops accepting events for the life of the process —
    // silently, with nothing in the file and nothing on Serilog's SelfLog — so the tail of the
    // session is lost, which is the end a post-mortem reader opens the file at (#357).
    private const long PartSizeLimitBytes = 10 * 1024 * 1024; // 10 MB
    private const int RetainedPartsPerSession = 5;            // => 50 MB per session
    private const int RetainedSessionCount = 7;               // => 350 MB per directory

    /// <summary>File name prefix shared by every IDE session log, including its rolled parts.</summary>
    private const string LogFilePrefix = "ide-";

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
        var logPath = Path.Combine(logDir, $"{LogFilePrefix}{sessionStamp}.log");

        var serilogLogger = BuildConfiguration(logPath, minLevel).CreateLogger();

        // Set the static Serilog logger for use in static contexts (e.g. ListenErrors)
        Log.Logger = serilogLogger;

        // Create the MEL ILoggerFactory so Pure.DI can produce ILogger<T> instances
        LoggerFactory = new SerilogLoggerFactory(serilogLogger);

        // Prune old sessions (keep the most recent N, each with all of its rolled parts)
        PruneOldLogs(logDir, LogFilePrefix, RetainedSessionCount);
    }

    /// <summary>
    /// Builds the production file-sink configuration. Extracted so a test can drive the real shape
    /// with a small size limit instead of restating it; the defaults are the shipped values.
    /// </summary>
    internal static LoggerConfiguration BuildConfiguration(
        string logPath,
        LogEventLevel minimumLevel,
        long partSizeLimitBytes = PartSizeLimitBytes,
        int retainedPartsPerSession = RetainedPartsPerSession)
    {
        return new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .WriteTo.File(
                path: logPath,
                fileSizeLimitBytes: partSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: retainedPartsPerSession,
                outputTemplate: OutputTemplate,
                shared: false);
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
    /// Deletes old log files in the directory, keeping the most recent
    /// <paramref name="keepSessions"/> <em>sessions</em> — every rolled part of a retained session
    /// is kept, and every part of a pruned one is deleted.
    /// </summary>
    /// <remarks>
    /// Counting files here rather than sessions is what makes rolling and retention collide: one
    /// busy session produces several parts, which a file count reads as several sessions, so the
    /// next startup deletes that session's own opening part while claiming to have kept seven
    /// sessions' worth of history.
    /// </remarks>
    internal static void PruneOldLogs(string directory, string prefix, int keepSessions)
    {
        try
        {
            var staleSessions = new DirectoryInfo(directory)
                .GetFiles(prefix + "*.log")
                .GroupBy(f => SessionKeyOf(f.Name, prefix), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Skip(keepSessions);

            foreach (var session in staleSessions)
            {
                foreach (var file in session)
                {
                    try { file.Delete(); }
                    catch { /* best effort */ }
                }
            }
        }
        catch
        {
            // Don't let log cleanup prevent app startup
        }
    }

    /// <summary>
    /// Reduces a log file name to the session it belongs to. Serilog's roller appends
    /// <c>_001</c>, <c>_002</c>… to the configured name, so <c>ide-20260911-120000.log</c> and
    /// <c>ide-20260911-120000_003.log</c> are two parts of one session and share a key.
    /// </summary>
    /// <remarks>
    /// The key is the session's own timestamp, which sorts chronologically as text, so pruning does
    /// not depend on <c>CreationTimeUtc</c> — a value Windows will happily copy from a deleted file
    /// of the same name onto its replacement.
    /// </remarks>
    internal static string SessionKeyOf(string fileName, string prefix)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            stem = stem[prefix.Length..];
        }

        var underscore = stem.LastIndexOf('_');
        if (underscore <= 0 || underscore == stem.Length - 1)
        {
            return stem;
        }

        foreach (var c in stem.AsSpan(underscore + 1))
        {
            if (!char.IsAsciiDigit(c))
            {
                return stem;
            }
        }

        return stem[..underscore];
    }
}
