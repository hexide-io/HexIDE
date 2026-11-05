using HexIDE.Addins;
using HexIDE.Dap;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Utils;
using Microsoft.Extensions.Logging;

namespace HexIDE.Debugging;

/// <summary>A launch an add-in contributed, under the name of the add-in that did.</summary>
/// <param name="Key">
/// What a project's choice of it is remembered as: the add-in's id and the launch's title, which stay the same from one
/// session to the next, as the instance does not, and as the add-in's title need not.
/// </param>
/// <param name="Origin">The add-in's title, for display.</param>
public sealed record ContributedDebugLaunch(string Key, string Origin, AddinDebugLaunch Launch)
{
    public string Title => Launch.Title;

    /// <summary>The launch title a remembered key names, to say which launch a choice was when it is gone.</summary>
    public static string TitleOf(string key) => key.IndexOf('/') is var at and >= 0 ? key[(at + 1)..] : key;
}

/// <summary>
/// The launches add-ins contribute for the External Debug Session, and the start of one for a project.
/// </summary>
/// <remarks>
/// Nothing here runs on its own: a launch is asked for its configuration only when a run of it starts, from F5 when the
/// user chose it for the project or from its own Run menu command.
/// </remarks>
public sealed class DebugLaunchService(ExternalDebugService debugger, ILogger<DebugLaunchService>? logger = null)
{
    private readonly object _gate = new();
    private readonly List<ContributedDebugLaunch> _launches = [];

    /// <summary>Raised when a launch is contributed or taken out.</summary>
    public event Action? LaunchesChanged;

    /// <summary>Every contributed launch, in the order they were contributed.</summary>
    public IReadOnlyList<ContributedDebugLaunch> Launches
    {
        get
        {
            lock (_gate) return [.. _launches];
        }
    }

    internal IDisposable Register(AddinDebugLaunch launch, AddinOrigin origin) => Register(launch, origin.Id, origin.Title);

    /// <summary>Adds an add-in's launch.</summary>
    /// <param name="originId">The add-in's id, which keys the remembered choice of the launch.</param>
    /// <param name="origin">The add-in's title, which names the launch in the Run menu.</param>
    /// <returns>What takes it out again.</returns>
    /// <exception cref="ArgumentException">It has no title, or nothing makes its configuration.</exception>
    /// <exception cref="InvalidOperationException">The add-in contributed a launch with that title already.</exception>
    public IDisposable Register(AddinDebugLaunch launch, string originId, string origin)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentException.ThrowIfNullOrWhiteSpace(launch.Title, nameof(launch));
        ArgumentException.ThrowIfNullOrWhiteSpace(originId);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);
        if (launch.CreateConfigurationAsync is null || launch.Claims is null)
            throw new ArgumentException("A launch needs what makes its configuration, and what says which projects it runs.", nameof(launch));

        var contributed = new ContributedDebugLaunch(originId + "/" + launch.Title, origin, launch);
        lock (_gate)
        {
            if (_launches.Any(known => known.Key == contributed.Key))
                throw new InvalidOperationException($"{origin} contributes a launch called '{launch.Title}' already.");
            _launches.Add(contributed);
        }
        LaunchesChanged?.Invoke();

        var handle = new ActionDisposable(() =>
        {
            bool removed;
            lock (_gate) removed = _launches.Remove(contributed);
            if (removed) LaunchesChanged?.Invoke();
        });
        // An add-in whose initialisation fails has its launch taken out with its other contributions.
        AddinInitializationScope.Record(handle.Dispose);
        return handle;
    }

    /// <summary>The launch a project's remembered choice names, or null when no launch has that name now.</summary>
    public ContributedDebugLaunch? Find(string key)
    {
        lock (_gate) return _launches.FirstOrDefault(known => known.Key == key);
    }

    /// <summary>The launches that say they can run this project.</summary>
    public IReadOnlyList<ContributedDebugLaunch> For(ProjectDefinition project)
    {
        var described = AddinProjectService.Describe(project);
        return [.. Launches.Where(launch => Claims(launch, described))];
    }

    /// <summary>Whether this launch says it can run this project. One whose answer throws cannot.</summary>
    public bool Claims(ContributedDebugLaunch launch, ProjectDefinition project) =>
        Claims(launch, AddinProjectService.Describe(project));

    private bool Claims(ContributedDebugLaunch launch, AddinProjectInfo project)
    {
        try
        {
            return launch.Launch.Claims(project);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "{Origin}'s launch '{Title}' could not say whether it runs {Project}.",
                launch.Origin, launch.Title, project.ProjectName);
            return false;
        }
    }

    /// <summary>
    /// Starts an External Debug Session for the project with this launch's configuration, made now. A configuration
    /// that fails to be made, or is refused, ends the start as a launch file that fails does.
    /// </summary>
    public Task StartAsync(ContributedDebugLaunch launch, ProjectDefinition project)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(project);
        var described = AddinProjectService.Describe(project);
        return debugger.StartAsync(async token =>
            DapLaunchConfiguration.FromContributed(await launch.Launch.CreateConfigurationAsync(described, token)));
    }
}
