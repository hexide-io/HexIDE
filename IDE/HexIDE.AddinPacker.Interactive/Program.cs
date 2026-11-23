using HexIDE.AddinPacker;
using HexIDE.AddinPacker.Interactive;
using Spectre.Console;

// hexide-pack asks only on an interactive terminal outside CI. Anywhere else a missing option fails at once, and the
// message says why it didn't ask (#5555).
var ci = Environment.GetEnvironmentVariable("CI");
var blocker = !string.IsNullOrEmpty(ci) && ci != "0" && !string.Equals(ci, "false", StringComparison.OrdinalIgnoreCase)
    ? "the CI environment variable is set"
    : Console.IsInputRedirected || Console.IsOutputRedirected || !AnsiConsole.Profile.Capabilities.Interactive
        ? "input or output isn't a terminal"
        : null;
return new HexidePack(AnsiConsole.Console, Console.Out, Console.Error, PackerCli.Run, blocker, OperatingSystem.IsWindows()).Run(args);
