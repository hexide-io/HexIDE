using HexIDE.Addins;

namespace HexIDE.Runtime;

/// <summary>
/// The Basic provider's way into the IDE: everything it contributes, registered through the host an add-in is given (#3411,
/// #5396).
/// </summary>
/// <remarks>
/// <para>
/// For now HexIDE.Desktop makes this and calls <see cref="Initialize"/> at startup, with an owned host under the provider's
/// own identity, so its registrations meet the same checks as any add-in's. Phase 3 of the move loads it as a bundled
/// add-in instead, and nothing here changes.
/// </para>
/// <para>
/// It registers its controls' resources, its language packs, and what draws the designer's controls (#5591). The run
/// engine, the designer's registry and the project type join it as their slices land.
/// </para>
/// </remarks>
public sealed class BasicProvider : IAddin
{
    /// <summary>The dictionaries the controls' themes are in, which the designer and a running program both need.</summary>
    internal static readonly IReadOnlyList<Uri> Resources =
    [
        new("avares://HexIDE.Runtime/BuiltinControls/VBTimer.axaml"),
        new("avares://HexIDE.Runtime/BuiltinControls/VBLabel.axaml"),
        new("avares://HexIDE.Runtime/BuiltinControls/Resources.axaml"),
    ];

    private readonly List<IDisposable> registrations = [];

    // A real frame: the host names the caller's assembly as the dictionaries' owner, so this can't be inlined away.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Initialize(IHexIdeHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        foreach (var dictionary in Resources)
            registrations.Add(host.RegisterResources(dictionary));
        registrations.Add(host.RegisterLanguagePacks(new Uri("avares://HexIDE.Runtime/Localization/Packs/")));
        // Last: a refusal here leaves the themes and text already registered.
        registrations.Add(host.RegisterDesignerPictures(surface => new InProcessDesignerPictures(surface)));
    }

    public void Dispose()
    {
        for (var i = registrations.Count - 1; i >= 0; i--)
            registrations[i].Dispose();
        registrations.Clear();
    }
}
