using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Threading;
using HexIDE.Runtime.Components;
using Serilog;

namespace HexIDE.Addins;

/// <summary>
/// What draws the form designer's controls: the one drawer an owner registered (#5591), which the designer asks for each
/// control's picture.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deny by default.</b> Only an owner whose grant says so may register (<see cref="RegistrationGrant.DesignerPictures"/>),
/// because the drawer is given a place in the designer's tree. Today that's the Basic provider alone.
/// </para>
/// <para>
/// <b>One at a time, per application.</b> While one registration holds, another is refused, naming both owners, so nothing
/// replaces the drawer silently. It's held per application, as the resources are: the headless tests make more than one in a
/// run, and each registers the provider afresh.
/// </para>
/// </remarks>
internal sealed class AddinDesignerPictureService
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

    internal IDisposable Register(Func<IDesignerDrawingSurface, IDesignerPictures> pictures, AddinOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(pictures);
        Dispatcher.UIThread.VerifyAccess();
        var scope = Scope ?? Application.Current
            ?? throw new InvalidOperationException("There is no application to draw controls for.");

        if (RegistrationGrants.For(origin, Grants) is not { DesignerPictures: true })
        {
            Log.Warning("Designer pictures: refused {Addin} ({AddinId}): it holds no grant to draw the designer's controls",
                origin.Title, origin.Id);
            throw new InvalidOperationException($"{origin.Title} isn't one HexIDE lets draw the designer's controls.");
        }

        var holder = Held.GetOrCreateValue(scope);
        if (holder.Current is { } held)
        {
            Log.Warning("Designer pictures: refused {Addin} ({AddinId}): {Owner} ({OwnerId}) draws them already",
                origin.Title, origin.Id, held.Owner.Title, held.Owner.Id);
            throw new InvalidOperationException(
                $"{origin.Title} can't draw the designer's controls: {held.Owner.Title} ({held.Owner.Id}) draws them already.");
        }

        holder.Current = new Registration(holder, origin, pictures);
        Log.Information("Designer pictures: {Addin} ({AddinId}) draws the designer's controls", origin.Title, origin.Id);
        return holder.Current;
    }

    /// <summary>What draws the controls in <paramref name="scope"/>, an application, or null when nothing is registered there.</summary>
    internal static Func<IDesignerDrawingSurface, IDesignerPictures>? For(object? scope) =>
        scope is not null && Held.TryGetValue(scope, out var holder) ? holder.Current?.Pictures : null;

    private sealed class Registration(Holder holder, AddinOrigin owner, Func<IDesignerDrawingSurface, IDesignerPictures> pictures)
        : IDisposable
    {
        private bool disposed;

        internal AddinOrigin Owner => owner;

        internal Func<IDesignerDrawingSurface, IDesignerPictures> Pictures => pictures;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            Dispatcher.UIThread.Invoke(() =>
            {
                // Only its own: a handle disposed late never takes out a drawer registered after it.
                if (ReferenceEquals(holder.Current, this))
                    holder.Current = null;
            });
        }
    }
}
