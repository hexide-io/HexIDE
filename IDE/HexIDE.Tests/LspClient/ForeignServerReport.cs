using System.Diagnostics;
using System.Text.Json;

namespace HexIDE.Tests.LspClient;

internal static class ForeignServerReport
{
    public const string ReportVariable = "HEXIDE_FOREIGN_LSP_REPORT";

    public static void Write(ForeignServer server, string reportPath)
    {
        var (path, origin) = server.Resolve();
        var version = path is null ? "unavailable" : server.HostedOnNode && origin == "pinned download"
            ? ReadPackageVersion(ForeignServerAcquisition.EnsureNodeServerAvailable()!)
            : ReadVersion(path);
        var script = server.HostedOnNode && origin == "pinned download"
            ? $"; script={ForeignServerAcquisition.EnsureNodeServerAvailable()}" : "";
        File.AppendAllText(reportPath,
            $"{server.OnPath}: {origin}; executable={path ?? "unavailable"}{script}; version={version}{Environment.NewLine}");
    }

    internal static string ReadPackageVersion(string script)
    {
        var package = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(script))!, "package.json");
        using var json = JsonDocument.Parse(File.ReadAllText(package));
        return json.RootElement.GetProperty("version").GetString()!;
    }

    internal static string ReadVersion(string executable)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable, "--version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            if (process is null) return "unavailable (could not start)";
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                return "unavailable (--version timed out)";
            }
            var output = (stdout.GetAwaiter().GetResult() + " " + stderr.GetAwaiter().GetResult()).Trim();
            return output.Length == 0 ? $"unavailable (--version exited {process.ExitCode})"
                : string.Join(" ", output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception)
        {
            return $"unavailable ({e.Message})";
        }
    }
}
