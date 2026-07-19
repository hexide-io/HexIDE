namespace HexIDE.Desktop;

internal static class ServerOptions
{
    public static int? Port { get; private set; }
    public static bool NewProject { get; private set; }
    public static string? ProjectPath { get; private set; }
    public static string? Personality { get; private set; }

    /// <summary><c>--developer-mode</c>: enable session developer mode (unlocks the Developer options
    /// node + the dev-gated capabilities, and shows the title-bar suffix).</summary>
    public static bool DeveloperMode { get; private set; }

    public static void ParseArgs(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (IsFlag(arg, "server-port") && i + 1 < args.Length && int.TryParse(args[i + 1], out var port))
            {
                Port = port;
                i++;
            }
            else if (IsFlag(arg, "newproject"))
            {
                NewProject = true;
            }
            else if (IsFlag(arg, "developer-mode"))
            {
                DeveloperMode = true;
            }
            else if (IsFlag(arg, "personality") && i + 1 < args.Length)
            {
                Personality = args[i + 1];
                i++;
            }
            else if (!arg.StartsWith('-') && !arg.StartsWith('/') &&
                     arg.EndsWith(".vbp", StringComparison.OrdinalIgnoreCase))
            {
                ProjectPath = arg;
            }
        }
    }

    private static bool IsFlag(string arg, string name) =>
        arg.Equals($"--{name}", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals($"/{name}", StringComparison.OrdinalIgnoreCase);
}
