using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Threading;
using HexIDE.Runtime.Components;
using Serilog;

namespace HexIDE.Addins;

/// <summary>
/// What gives VB6's scale rules at design time: the one set an owner registered (#5758), which the Properties window, the
/// toolbar's readout, the form designer and automation ask through <see cref="VisualDesigner.DesignerClassCalls"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deny by default.</b> Only an owner whose grant says so may register (<see cref="RegistrationGrant.DesignPositions"/>),
/// because its answers decide the positions written into the user's forms. Today that's the Basic provider alone.
/// </para>
/// <para>
/// <b>One at a time, per application.</b> While one registration holds, another is refused, naming both owners, so nothing
/// replaces the rules silently. It's held per application, as the designer's drawer is: the headless tests make more than one
/// in a run, and each registers the provider afresh.
/// </para>
/// </remarks>
internal sealed class AddinDesignPositionService
{
    /// <summary>Who may register what. HexIDE's own table; a test gives its own owners grants here.</summary>
    internal IReadOnlyList<RegistrationGrant> Grants { get; init; } = RegistrationGrants.All;

    /// <summary>What registrations are held for: the application, unless a test gives a scope of its own.</summary>
    internal object? Scope { get; init; }

    private static readonly ConditionalWeakTable<object, Holder> Held = new();

    private sealed class Holder
    {
        internal Registration? Current;
    }

    internal IDisposable Register(IDesignPositions positions, AddinOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(positions);
        Dispatcher.UIThread.VerifyAccess();
        var scope = Scope ?? Application.Current
            ?? throw new InvalidOperationException("There is no application to give positions for.");

        if (RegistrationGrants.For(origin, Grants) is not { DesignPositions: true })
        {
            Log.Warning("Design positions: refused {Addin} ({AddinId}): it holds no grant to give VB6's scale rules",
                origin.Title, origin.Id);
            throw new InvalidOperationException($"{origin.Title} isn't one HexIDE lets give the designer's scale rules.");
        }

        var holder = Held.GetOrCreateValue(scope);
        if (holder.Current is { } held)
        {
            Log.Warning("Design positions: refused {Addin} ({AddinId}): {Owner} ({OwnerId}) gives them already",
                origin.Title, origin.Id, held.Owner.Title, held.Owner.Id);
            throw new InvalidOperationException(
                $"{origin.Title} can't give the designer's scale rules: {held.Owner.Title} ({held.Owner.Id}) gives them already.");
        }

        holder.Current = new Registration(holder, origin, positions);
        Log.Information("Design positions: {Addin} ({AddinId}) gives the designer's scale rules", origin.Title, origin.Id);
        return holder.Current;
    }

    /// <summary>What gives the scale rules in <paramref name="scope"/>, an application, or null when nothing is registered there.</summary>
    internal static IDesignPositions? For(object? scope) =>
        scope is not null && Held.TryGetValue(scope, out var holder) ? holder.Current?.Positions : null;

    private sealed class Registration(Holder holder, AddinOrigin owner, IDesignPositions positions) : IDisposable
    {
        private bool disposed;

        internal AddinOrigin Owner => owner;

        internal IDesignPositions Positions => positions;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            Dispatcher.UIThread.Invoke(() =>
            {
                // Only its own: a handle disposed late never takes out rules registered after it.
                if (ReferenceEquals(holder.Current, this))
                    holder.Current = null;
            });
        }
    }
}
