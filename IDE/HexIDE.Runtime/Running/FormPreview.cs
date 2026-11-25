using Avalonia.Controls;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Running;

/// <summary>
/// The controls of the designer's preview of a form (#2003, #5633): spawned as a run spawns them, with no code set, prepared
/// or started, so events have no interpreter to dispatch to.
/// </summary>
/// <remarks>
/// Apart from the engine's run path, which names no distribution (RunFormIconTests): a box the preview raises takes the IDE's
/// name where it's given no title, as the designer's preview always did.
/// </remarks>
internal static class FormPreview
{
    internal static Control Spawn(FormDefinition form, VBFormRuntime preview) =>
        VBLoader.SpawnComponents(form, preview.Context.ExecutionContext, preview.Context.RootEnv,
            new StandaloneStandardLib(preview, HexIDE.IDE.Distribution.DisplayName));
}
