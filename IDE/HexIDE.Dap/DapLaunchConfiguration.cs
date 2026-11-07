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
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.EnumerateObject().FirstOrDefault(property =>
                property.Name.Equals("breakpoints", StringComparison.OrdinalIgnoreCase)).Value is { ValueKind: JsonValueKind.Array } breakpoints)
        {
            var entry = 0;
            foreach (var breakpoint in breakpoints.EnumerateArray())
            {
                ++entry;
                if (breakpoint.ValueKind != JsonValueKind.Object) continue;
                foreach (var property in breakpoint.EnumerateObject())
                    if (property.Name.Equals("condition", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Equals("hitCondition", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Equals("logMessage", StringComparison.OrdinalIgnoreCase))
                        throw new DapBreakpointConditionException(file, entry, property.Name);
            }
        }
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

    /// <summary>
    /// A configuration an add-in made for a run, checked as a file's is but with no file to resolve its paths against:
    /// the executable and working directory are fully qualified, or it is refused. It carries no breakpoints; the
    /// session is given the IDE's.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The executable or adapter id is missing, a path is not fully qualified, or the launch arguments are not an object.
    /// </exception>
    public static DapLaunchConfiguration FromContributed(HexIDE.Addins.AddinLaunchConfiguration contributed)
    {
        ArgumentNullException.ThrowIfNull(contributed);
        if (string.IsNullOrWhiteSpace(contributed.Executable) || string.IsNullOrWhiteSpace(contributed.AdapterId))
            throw new InvalidDataException("A debug launch configuration needs an executable and an adapter id.");
        if (!Path.IsPathFullyQualified(contributed.Executable))
            throw new InvalidDataException($"The debug adapter's executable is not a full path: {contributed.Executable}");
        if (contributed.WorkingDirectory is { } directory && !Path.IsPathFullyQualified(directory))
            throw new InvalidDataException($"The debug adapter's working directory is not a full path: {directory}");
        if (contributed.Launch.ValueKind != JsonValueKind.Object || contributed.Arguments is null ||
            contributed.Environment is null || contributed.Arguments.Any(argument => argument is null))
            throw new InvalidDataException("Invalid debug launch arguments or environment.");
        return new DapLaunchConfiguration
        {
            Executable = Path.GetFullPath(contributed.Executable),
            Arguments = [.. contributed.Arguments],
            WorkingDirectory = Path.GetFullPath(contributed.WorkingDirectory ?? Path.GetDirectoryName(contributed.Executable)!),
            Environment = new Dictionary<string, string?>(contributed.Environment),
            AdapterId = contributed.AdapterId,
            Launch = contributed.Launch.Clone(),
        };
    }

    public DapProcessOptions ProcessOptions(DapCapture capture) => new(Executable)
    {
        Arguments = Arguments, WorkingDirectory = WorkingDirectory, Environment = Environment, Capture = capture,
        ShutdownTimeout = TimeSpan.FromSeconds(5),
    };
}

public sealed class DapBreakpointConditionException(string file, int entry, string option) : Exception
{
    public string File { get; } = file;
    public int Entry { get; } = entry;
    public string Option { get; } = option;
}
