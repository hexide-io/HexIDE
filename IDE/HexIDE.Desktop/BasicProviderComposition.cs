using HexIDE.Addins;
using HexIDE.Runtime;
using Serilog;

namespace HexIDE.Desktop;

/// <summary>
/// Where the Basic provider joins the IDE (#3411, #5396). This is meant to be the only product file that names the provider:
/// the IDE itself reaches it only through what it registers.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same checks as any add-in.</b> The provider is given an owned host under its own identity, as the registry gives
/// an add-in once its consent is settled, so everything it registers is checked and owned like an add-in's contribution.
/// There is no other way in.
/// </para>
/// <para>
/// <b>No more privilege than it uses.</b> Its origin isn't first-party yet, since nothing it registers needs that. The
/// slice that moves the VB6 project type onto it decides that, in view.
/// </para>
/// <para>
/// <b>Phase 3</b> of the move loads <see cref="BasicProvider"/> as a bundled add-in instead, and this file goes.
/// </para>
/// </remarks>
internal static class BasicProviderComposition
{
    /// <summary>The provider's identity: what owns its registrations, and what a refusal names.</summary>
    internal static AddinOrigin Origin { get; } = new("hexide.basic", "HexIDE.Basic");

    /// <summary>
    /// Makes the provider and lets it register. Called before add-ins load and before any project can open, so the first
    /// designer drawn already has the provider's themes.
    /// </summary>
    /// <returns>
    /// The provider. It isn't disposed at shutdown: its themes stay until the process ends, so a window still closing keeps
    /// drawing as it did.
    /// </returns>
    internal static BasicProvider? Register(IHexIdeHost host)
    {
        if (host.ToolWindows is not AddinToolWindowService tools)
        {
            Log.Error("The Basic provider wasn't registered: the IDE's host has no add-in tool windows to give it an owned host");
            return null;
        }

        var provider = new BasicProvider();
        try
        {
            provider.Initialize(new AddinOwnedHost(host, tools.ForOwner(Origin), Origin));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Its own dictionaries refused is a defect here, not a person's mistake: say so in the log and carry on, as a
            // missing resource would, rather than stop the IDE from starting.
            Log.Error(ex, "The Basic provider's registration was refused");
        }
        return provider;
    }
}
