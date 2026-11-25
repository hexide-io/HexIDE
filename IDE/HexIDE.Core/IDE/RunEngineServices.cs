using System;

namespace HexIDE.IDE;

/// <summary>
/// What the IDE gives a run engine its owner registers (#5616): the running program's host, the windows its own boxes are
/// shown in, and the two things it reads about the IDE. Nothing here changes the IDE's settings.
/// </summary>
public sealed class RunEngineServices
{
    private readonly Func<bool> rdcoreWin64OnlySetOnWindows;
    private readonly Func<bool> singleView;

    /// <summary>Makes the services a run engine is given.</summary>
    /// <param name="host">The IDE's side of a running program: its forms' windows and the IDE's own dialogs.</param>
    /// <param name="dialogs">Where the program's MsgBox and InputBox are shown.</param>
    /// <param name="rdcoreWin64OnlySetOnWindows">Reads the IDE's setting of that name, as it stands when asked.</param>
    /// <param name="singleView">Reads whether the IDE shows windows inside its own view rather than as desktop windows.</param>
    public RunEngineServices(IProgramHost host, IProgramDialogWindows dialogs, Func<bool> rdcoreWin64OnlySetOnWindows,
        Func<bool> singleView)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(rdcoreWin64OnlySetOnWindows);
        ArgumentNullException.ThrowIfNull(singleView);
        Host = host;
        Dialogs = dialogs;
        this.rdcoreWin64OnlySetOnWindows = rdcoreWin64OnlySetOnWindows;
        this.singleView = singleView;
    }

    /// <summary>The IDE's side of a running program: its forms' windows and the IDE's own dialogs.</summary>
    public IProgramHost Host { get; }

    /// <summary>Where the program's MsgBox and InputBox are shown.</summary>
    public IProgramDialogWindows Dialogs { get; }

    /// <summary>
    /// The IDE's setting of that name, read as it stands now: a person can change it between runs (#5036). Read only, so an
    /// engine can't change it.
    /// </summary>
    public bool RDCoreWin64OnlySetOnWindows => rdcoreWin64OnlySetOnWindows();

    /// <summary>
    /// Whether the IDE shows windows inside its own view, as in a browser, rather than as desktop windows: read as it stands
    /// now, as the engine read it before it registered.
    /// </summary>
    public bool SingleView => singleView();
}
