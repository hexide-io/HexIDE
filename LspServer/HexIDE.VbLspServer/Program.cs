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
var logDir = GetLogDirectory("lsp");
var sessionStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(GetMinimumLevel())
    .WriteTo.File(
        path: Path.Combine(logDir, $"lsp-{sessionStamp}.log"),
        fileSizeLimitBytes: 50 * 1024 * 1024,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
        shared: false)
    .CreateLogger();

PruneOldLogs(logDir, "lsp-*.log", 7);
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

static void PruneOldLogs(string directory, string pattern, int keepCount)
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
        // Don't let cleanup prevent server startup.
    }
}
