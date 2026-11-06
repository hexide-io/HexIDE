using System.Text.Json;

namespace HexIDE.Addins;

/// <summary>
/// A way to run a project that an add-in contributes: a launch configuration for the External Debug Session, made
/// for the project when the user asks for a run, and never when a project opens.
/// </summary>
/// <remarks>
/// <para>
/// <b>F5 stays the interpreter's unless the user says otherwise.</b> A launch is offered under Run &gt; F5 Runs, where
/// the user chooses, per project, what F5 runs; the choice is remembered beside the project, for that user. Installing
/// an add-in changes nothing until then. Each launch is also listed under Run &gt; Start Once With, which runs it once
/// without touching F5.
/// </para>
/// <para>
/// <b>The breakpoints are the IDE's.</b> The session is given the gutter marks saved for the project, with their
/// conditions refused as an external session refuses them, so the configuration carries none.
/// </para>
/// </remarks>
/// <param name="Title">What the Run menu's F5 Runs and Start Once With call it, such as <c>"RDCore"</c>.</param>
public sealed record AddinDebugLaunch(string Title)
{
    /// <summary>
    /// Whether this launch can run the project. Asked when the Run menu is shown and when the user asks for a run, and,
    /// for whether F5 can start a project the user chose it for, again only when what it is told of the project changes,
    /// never at each edit. Every project, unless the add-in says otherwise.
    /// </summary>
    public Func<AddinProjectInfo, bool> Claims { get; init; } = _ => true;

    /// <summary>
    /// The configuration for one run of the project, made when the run starts. The token is cancelled when the user
    /// ends the session before it has started. One that throws ends the run with its message.
    /// </summary>
    public required Func<AddinProjectInfo, CancellationToken, Task<AddinLaunchConfiguration>> CreateConfigurationAsync { get; init; }
}

/// <summary>
/// What starts a debug adapter and launches the program under it: the fields of a launch configuration file, as
/// <c>docs/debug-launch.md</c> describes them, without its breakpoints.
/// </summary>
/// <remarks>
/// Paths are taken as they are, not against a file's folder as a configuration file's are: <see cref="Executable"/>
/// and <see cref="WorkingDirectory"/> are fully qualified, or the run is refused.
/// </remarks>
public sealed record AddinLaunchConfiguration
{
    /// <summary>The debug adapter's executable, fully qualified.</summary>
    public required string Executable { get; init; }

    /// <summary>Its arguments, each passed as it is.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Its working directory, fully qualified; the executable's folder when null.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Variables set for it, or removed when null, on top of the IDE's own environment.</summary>
    public IReadOnlyDictionary<string, string?> Environment { get; init; } = new Dictionary<string, string?>();

    /// <summary>The adapter's id, sent in <c>initialize</c> and shown as the session's name.</summary>
    public required string AdapterId { get; init; }

    /// <summary>The <c>launch</c> request's arguments, a JSON object passed to the adapter as it is.</summary>
    public required JsonElement Launch { get; init; }
}
