using HexIDE.IDE;

namespace HexIDE.Events;

/// <summary>
/// Raised when the user confirms VB6's "reset your project?" prompt after editing code while the project is
/// running/paused. <c>ProjectRunnerService</c> handles it by ending the run. Routed through the event bus so the
/// code editor doesn't take a direct dependency on <c>IProjectRunnerService</c> (which would close a DI cycle via
/// the editor factory).
/// </summary>
public class EndProjectRequestedEvent : IEvent
{
}
