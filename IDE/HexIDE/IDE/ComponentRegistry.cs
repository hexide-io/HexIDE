using System.Collections.Generic;
using HexIDE.Addins;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using Serilog;

namespace HexIDE.IDE;

/// <summary>
/// The component classes the designer knows and what the toolbox offers, each kept with the owner that registered it
/// (#5594).
/// </summary>
/// <remarks>
/// <para>
/// <b>It starts empty.</b> The Basic provider registers its Form and controls, and their toolbox items, through the host it's
/// given, as an add-in registers its own. Nothing here names them.
/// </para>
/// <para>
/// <b>Deny by default beyond a plain control.</b> A class whose role isn't <see cref="ComponentRole.Control"/>, or that's a
/// container, needs its owner's grant (<see cref="RegistrationGrant.Components"/>): the designer gives such a class a place
/// no plain control has, as a form's root or as other controls' parent.
/// </para>
/// <para>
/// <b>Only adds.</b> A class whose type name a registration holds is refused, naming both owners, and so is one in a control
/// library's namespace while the libraries are HexIDE's own list. A toolbox item comes only from its class's owner.
/// </para>
/// </remarks>
public class ComponentRegistry : IComponentRegistry
{
    /// <summary>The icon a toolbox item shows when its owner names none, and a library's control shows.</summary>
    internal const string GenericIcon = "Geo.Object";

    private readonly List<IComponentClass> components = [];
    private readonly Dictionary<IComponentClass, Registration> owners = new(ReferenceEqualityComparer.Instance);
    private readonly List<ToolboxEntry> toolbox = [];

    /// <summary>Who may register what. HexIDE's own table; a test gives its own owners grants here.</summary>
    internal IReadOnlyList<RegistrationGrant> Grants { get; init; } = RegistrationGrants.All;

    public IReadOnlyList<IComponentClass> Components => components;

    public IReadOnlyList<ToolboxEntry> Toolbox => toolbox;

    public IReadOnlyList<IControlLibrary> Libraries => ControlLibraries.All;

    // Built on first use, so these classes load only once a project references one of their libraries, as they always did.
    private IReadOnlyList<LibraryControl>? libraryControls;

    public IReadOnlyList<LibraryControl> LibraryControls => libraryControls ??=
    [
        .. from library in ControlLibraries.All
           from control in InBoxComponentClass.Implemented
           where control.Libraries.Contains(library)
           select new LibraryControl(control, library, control.ControlClassName, library.TypeName(control.ControlClassName), GenericIcon),
    ];

    public event Action<IComponentClass>? ComponentRegistered;

    public event Action<IComponentClass>? ComponentUnregistered;

    public event Action<ToolboxEntry>? ToolboxItemAdded;

    public event Action<ToolboxEntry>? ToolboxItemRemoved;

    /// <summary>
    /// Registers <paramref name="component"/> for <paramref name="owner"/> until the handle is disposed, which takes out its
    /// toolbox item too.
    /// </summary>
    /// <exception cref="InvalidOperationException">The owner may not register it, or its type name is held.</exception>
    internal IDisposable Register(IComponentClass component, AddinOrigin owner)
    {
        ArgumentNullException.ThrowIfNull(component);
        var name = component.VBTypeName;

        if ((component.Role != ComponentRole.Control || component.IsContainer)
            && RegistrationGrants.For(owner, Grants) is not { Components: true })
        {
            var what = component.Role != ComponentRole.Control ? $"a {component.Role.ToString().ToLowerInvariant()} component" : "a container";
            Log.Warning("Components: refused {Addin} ({AddinId}): {Type} is {What}, which it holds no grant to register",
                owner.Title, owner.Id, name, what);
            throw new InvalidOperationException($"{owner.Title} isn't one HexIDE lets register {name}, which is {what}.");
        }

        foreach (var held in owners.Values)
        {
            if (!string.Equals(held.Class.VBTypeName, name, StringComparison.OrdinalIgnoreCase))
                continue;
            Log.Warning("Components: refused {Addin} ({AddinId}): {Owner} ({OwnerId}) holds {Type} already",
                owner.Title, owner.Id, held.Owner.Title, held.Owner.Id, name);
            throw new InvalidOperationException(
                $"{owner.Title} can't register {name}: {held.Owner.Title} ({held.Owner.Id}) holds that type name already.");
        }

        // The libraries' controls aren't registered yet, so a type name in a library's namespace is the library's.
        foreach (var library in Libraries)
        {
            if (!name.StartsWith(library.TypeName(""), StringComparison.OrdinalIgnoreCase))
                continue;
            Log.Warning("Components: refused {Addin} ({AddinId}): {Type} is in the {Library} library's namespace",
                owner.Title, owner.Id, name, library.Name);
            throw new InvalidOperationException(
                $"{owner.Title} can't register {name}: that type name is the {library.Name} control library's.");
        }

        var registration = new Registration(this, component, owner);
        components.Add(component);
        owners.Add(component, registration);
        AddinInitializationScope.Record(registration.Dispose);
        ComponentRegistered?.Invoke(component);
        return registration;
    }

    /// <summary>
    /// Offers <paramref name="component"/> in the toolbox, after the items already there, until the handle is disposed.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The class isn't registered, or <paramref name="owner"/> didn't register it, or the toolbox offers it already.
    /// </exception>
    internal IDisposable RegisterToolboxItem(IComponentClass component, string? iconKey, AddinOrigin owner)
    {
        ArgumentNullException.ThrowIfNull(component);
        var name = component.VBTypeName;
        string? refusal = null;
        if (!owners.TryGetValue(component, out var registration))
            refusal = $"{owner.Title} can't put {name} in the toolbox: it isn't registered.";
        else if (!SameOwner(registration.Owner, owner))
            refusal = $"{owner.Title} can't put {name} in the toolbox: {registration.Owner.Title} ({registration.Owner.Id}) registered it.";
        else if (toolbox.Exists(entry => ReferenceEquals(entry.Class, component)))
            refusal = $"{owner.Title} can't put {name} in the toolbox: it's there already.";
        if (refusal is not null)
        {
            Log.Warning("Components: refused {Addin} ({AddinId}) a toolbox item: {Refusal}", owner.Title, owner.Id, refusal);
            throw new InvalidOperationException(refusal);
        }

        var entry = new ToolboxEntry(component, iconKey ?? GenericIcon);
        toolbox.Add(entry);
        var handle = new ToolboxItem(this, entry);
        registration!.Items.Add(handle);
        AddinInitializationScope.Record(handle.Dispose);
        ToolboxItemAdded?.Invoke(entry);
        return handle;
    }

    // An owner is one add-in by its id; a composed owner is never a package that names itself after it.
    private static bool SameOwner(AddinOrigin a, AddinOrigin b) =>
        string.Equals(a.Id, b.Id, StringComparison.Ordinal) && a.IsComposed == b.IsComposed;

    private sealed class Registration(ComponentRegistry registry, IComponentClass component, AddinOrigin owner) : IDisposable
    {
        private bool disposed;

        internal IComponentClass Class => component;

        internal AddinOrigin Owner => owner;

        internal List<ToolboxItem> Items { get; } = [];

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            for (var i = Items.Count - 1; i >= 0; i--)
                Items[i].Dispose();
            registry.owners.Remove(component);
            registry.components.Remove(component);
            registry.ComponentUnregistered?.Invoke(component);
        }
    }

    private sealed class ToolboxItem(ComponentRegistry registry, ToolboxEntry entry) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            registry.toolbox.Remove(entry);
            registry.ToolboxItemRemoved?.Invoke(entry);
        }
    }
}
