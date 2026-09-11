// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// HexIDE VB6 Language Server (MIT) — EmmyLua.LanguageServer.Framework shell.
// Communicates over stdio using the Language Server Protocol (LSP). stdout is the LSP channel;
// all logging goes to a rolling file (never stdout — that would corrupt the frame stream).

using HexIDE.VbLspServer;
using Serilog;
using Serilog.Events;

// stdout carries LSP frames — keep it byte-clean.
Console.OutputEncoding = System.Text.Encoding.UTF8;

// Per-process rolling log under %LOCALAPPDATA%/HexIDE/logs/lsp (reproduces the prior server's contract).
// Three limits, multiplied, are the whole disk budget — stated together so the total is deliberate:
//     10 MB per part x 5 parts per session x 7 sessions = 350 MB per log directory.
// Rolling is not optional: a full part with rollOnFileSizeLimit at its default stops accepting
// events for the life of the process, silently, taking the crash at the end of the session with
// it (#357). Mirrors IDE/HexIDE/Infrastructure/LoggingSetup.cs — the two processes log separately
// by design, so the policy is duplicated rather than shared.
const long PartSizeLimitBytes = 10 * 1024 * 1024;
const int RetainedPartsPerSession = 5;
const int RetainedSessionCount = 7;
const string LogFilePrefix = "lsp-";

var logDir = GetLogDirectory("lsp");
var sessionStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(GetMinimumLevel())
    .WriteTo.File(
        path: Path.Combine(logDir, $"{LogFilePrefix}{sessionStamp}.log"),
        fileSizeLimitBytes: PartSizeLimitBytes,
        rollOnFileSizeLimit: true,
        retainedFileCountLimit: RetainedPartsPerSession,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
        shared: false)
    .CreateLogger();

PruneOldLogs(logDir, LogFilePrefix, RetainedSessionCount);
Log.Information("HexIDE VB6 LSP server (MIT shell) starting");

try
{
    var server = LspServerHost.Create(Console.OpenStandardInput(), Console.OpenStandardOutput());
    await server.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "LSP server crashed");
}
finally
{
    Log.Information("HexIDE VB6 LSP server exiting");
    Log.CloseAndFlush();
}

static string GetLogDirectory(string subsystem)
{
    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    var dir = Path.Combine(localAppData, "HexIDE", "logs", subsystem);
    Directory.CreateDirectory(dir);
    return dir;
}

static LogEventLevel GetMinimumLevel()
{
    var envLevel = Environment.GetEnvironmentVariable("HEXIDE_LOG_LEVEL");
    if (!string.IsNullOrWhiteSpace(envLevel) &&
        Enum.TryParse<LogEventLevel>(envLevel, ignoreCase: true, out var parsed))
    {
        return parsed;
    }
    return LogEventLevel.Information;
}

// Keeps the most recent N *sessions*, all of their rolled parts included. Counting files instead
// would read one busy session's parts as several sessions and delete that session's opening part.
static void PruneOldLogs(string directory, string prefix, int keepSessions)
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
        // Don't let cleanup prevent server startup.
    }
}

// "lsp-20260911-120000.log" and "lsp-20260911-120000_003.log" are two parts of one session.
static string SessionKeyOf(string fileName, string prefix)
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
