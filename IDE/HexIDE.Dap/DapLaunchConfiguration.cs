using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexIDE.Dap;

/// <summary>An explicitly selected file, never auto-executed when a workspace opens.</summary>
public sealed record DapLaunchConfiguration
{
    public required string Executable { get; init; }
    public string[] Arguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public Dictionary<string, string?> Environment { get; init; } = [];
    public required string AdapterId { get; init; }
    public required JsonElement Launch { get; init; }
    public DapSourceBreakpoints[] Breakpoints { get; init; } = [];

    public static async Task<DapLaunchConfiguration> ReadAsync(string file, CancellationToken token = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(file))!;
        var text = await File.ReadAllTextAsync(file, token).ConfigureAwait(false);
        var configuration = JsonSerializer.Deserialize<DapLaunchConfiguration>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        }) ?? throw new InvalidDataException("Debug launch configuration must be an object.");
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.Executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.AdapterId);
        if (configuration.Launch.ValueKind != JsonValueKind.Object || configuration.Arguments is null ||
            configuration.Environment is null || configuration.Breakpoints is null || configuration.Arguments.Any(argument => argument is null))
            throw new InvalidDataException("Invalid debug launch arguments, environment or breakpoints.");
        var sources = configuration.Breakpoints.Select(source =>
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Path) || source.Lines is null || source.Lines.Any(line => line < 1))
                throw new InvalidDataException("Source breakpoints require a path and positive one-based lines.");
            return source with { Path = Path.GetFullPath(source.Path, directory) };
        }).ToArray();
        return configuration with
        {
            Executable = Path.GetFullPath(configuration.Executable, directory),
            WorkingDirectory = Path.GetFullPath(configuration.WorkingDirectory ?? ".", directory),
            Breakpoints = sources,
        };
    }

    public DapProcessOptions ProcessOptions(DapCapture capture) => new(Executable)
    {
        Arguments = Arguments, WorkingDirectory = WorkingDirectory, Environment = Environment, Capture = capture,
        ShutdownTimeout = TimeSpan.FromSeconds(5),
    };
}
