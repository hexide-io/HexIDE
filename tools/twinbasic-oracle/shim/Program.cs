// Stands in for msedgewebview2.exe in a copy of the WebView2 runtime that only the twinBASIC IDE is pointed at
// (WEBVIEW2_BROWSER_EXECUTABLE_FOLDER). It starts the real browser with a DevTools port added, so the harness can
// drive the IDE's front end. Child processes (--type=...) pass through unchanged. Nothing here touches the compiler.
//
// The browser starts without the launcher's environment, so tb.ps1 writes the settings beside this file instead,
// in tboracle-shim.cfg: "real=<path of the real msedgewebview2.exe>" and "port=<DevTools port>".
using System.Diagnostics;

Log($"start {Environment.CommandLine}");
var raw = Environment.CommandLine;
var rest = raw.StartsWith('"')
    ? raw[(raw.IndexOf('"', 1) + 1)..]
    : raw[(raw.IndexOf(' ') is var i && i < 0 ? raw.Length : i)..];
var cfg = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "tboracle-shim.cfg"))
    .Select(l => l.Split('=', 2)).Where(kv => kv.Length == 2)
    .ToDictionary(kv => kv[0].Trim(), kv => kv[1].Trim(), StringComparer.OrdinalIgnoreCase);
var real = cfg.GetValueOrDefault("real");
if (string.IsNullOrEmpty(real) || !File.Exists(real))
{
    Log("tboracle-shim.cfg must name the real msedgewebview2.exe as real=<path>");
    return 2;
}
var basePort = int.TryParse(cfg.GetValueOrDefault("port"), out var p) ? p : 9341;
var isBrowser = !rest.Contains("--type=", StringComparison.Ordinal);
if (isBrowser)
{
    // The IDE starts two browsers, its own and a web panel's. Each gets its own port so they never race for one.
    var port = rest.Contains("WebPanel", StringComparison.OrdinalIgnoreCase) ? basePort + 1 : basePort;
    rest = $" --remote-debugging-port={port}" + rest;
}
Log($"browser={isBrowser}");
using var child = Process.Start(new ProcessStartInfo(real, rest.TrimStart()) { UseShellExecute = false })!;
child.WaitForExit();
return child.ExitCode;

static void Log(string line)
{
    foreach (var dir in new[] { AppContext.BaseDirectory, Path.GetTempPath() })
    {
        try
        {
            File.AppendAllText(Path.Combine(dir, "tboracle-shim.log"), $"{DateTime.UtcNow:O} {line}{Environment.NewLine}");
            return;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
