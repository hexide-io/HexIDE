namespace HexIDE.Runtime.ProjectElements;

/// <summary>
/// A project's target, stored as [HexIDE] Targets=. It selects the predefined conditional constants (#4821), and RDCore
/// also switches on the run-time additions RDCore makes to VB6 (#4967). It is not a run engine: HexIDE's own runs the
/// project either way.
/// </summary>
internal enum ProjectTarget
{
    None = 0,
    RDCore = 1,
}
