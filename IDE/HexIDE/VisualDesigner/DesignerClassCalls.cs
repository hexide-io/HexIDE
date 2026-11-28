using System;
using System.Collections.Concurrent;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using Serilog;

namespace HexIDE.VisualDesigner;

/// <summary>
/// The designer's calls into a registered component class's optional contracts (#5633): its title-bar caption,
/// the starting values of a control it draws, where a control added by double-clicking its tool goes (#5681), a
/// container's pages (#5742), which automation asks through here too, and the library a control is in (#5749). A
/// class's code is the provider's or an add-in's, so a throw, or a null where an answer is needed, answers as though
/// the class didn't have the contract, and the designer stays usable. VB6's scale rules come the same way from the
/// positions service its provider registered (#5758), which the Properties window, the toolbar's readout and automation
/// ask through here too.
/// </summary>
/// <remarks>
/// The first failure from each class and contract is logged as a warning, and later ones at debug: the caption is asked
/// again on every property change, so a class that always throws would otherwise fill the log.
/// </remarks>
internal static class DesignerClassCalls
{
    /// <summary>What a root whose class gives no caption shows: the generic window glyph, in a title bar that shows one.</summary>
    internal static readonly DesignerCaption NoCaption = new(null, UseFormGlyph: true, Visible: true);

    private static readonly ConcurrentDictionary<(Type Class, string Contract), bool> Reported = new();

    /// <summary>The caption <paramref name="root"/>'s class gives it, or <see cref="NoCaption"/>.</summary>
    /// <remarks>Only ever asked of a designer's root: a control on it is never asked, whatever its class implements.</remarks>
    internal static DesignerCaption CaptionOf(ComponentInstance root)
    {
        if (root.BaseClass is not IDesignerCaption caption)
            return NoCaption;
        try
        {
            if (caption.CaptionOf(root) is { } answer)
                return answer;
            Failed(caption, nameof(IDesignerCaption), null);
        }
        catch (Exception ex)
        {
            Failed(caption, nameof(IDesignerCaption), ex);
        }
        return NoCaption;
    }

    /// <summary>Lets <paramref name="instance"/>'s class set its starting values, for a control just drawn and named.</summary>
    internal static void Drawn(ComponentInstance instance, string name)
    {
        if (instance.BaseClass is not IDesignerDrawnDefaults defaults)
            return;
        try
        {
            defaults.Drawn(instance, name);
        }
        catch (Exception ex)
        {
            Failed(defaults, nameof(IDesignerDrawnDefaults), ex);
        }
    }

    /// <summary>
    /// The bounds <paramref name="componentClass"/> gives a control added by double-clicking its tool on a form of the
    /// given size, in pixels, or null to centre it at its added size as any other class's (#5681).
    /// </summary>
    internal static DesignerBounds? AddedBounds(IComponentClass componentClass, double formWidth, double formHeight)
    {
        if (componentClass is not IDesignerAddedPlacement placement)
            return null;
        try
        {
            if (placement.AddedBounds(formWidth, formHeight) is not { } bounds)
                return null;
            if (Usable(bounds))
                return bounds;
            Failed(placement, nameof(IDesignerAddedPlacement), null);
        }
        catch (Exception ex)
        {
            Failed(placement, nameof(IDesignerAddedPlacement), ex);
        }
        return null;
    }

    /// <summary>Whether Align to Grid moves a control of <paramref name="componentClass"/> added by double-clicking its tool.</summary>
    internal static bool AddedOnGrid(IComponentClass componentClass)
    {
        if (componentClass is not IDesignerAddedPlacement placement)
            return true;
        try
        {
            return placement.AddedOnGrid;
        }
        catch (Exception ex)
        {
            Failed(placement, nameof(IDesignerAddedPlacement), ex);
            return true;
        }
    }

    /// <summary>
    /// Whether the designer and automation ask <paramref name="componentClass"/> for pages: only through
    /// <see cref="IDesignerPages"/>, and only of a container's class, since registering a container needs the provider's
    /// grant and a plain control doesn't (#5742).
    /// </summary>
    internal static bool HasPages(IComponentClass componentClass) =>
        componentClass is IDesignerPages && componentClass.IsContainer;

    /// <summary>Lets <paramref name="instance"/>'s class read its pages, for a container on a form the designer opens.</summary>
    internal static void LoadPages(ComponentInstance instance) =>
        AskPages(instance, pages => { pages.LoadPages(instance); return true; }, out _);

    /// <summary>Lets <paramref name="instance"/>'s class record which page each of <paramref name="children"/> is on.</summary>
    internal static void SynchronizePages(ComponentInstance instance, IReadOnlyList<ComponentInstance> children) =>
        AskPages(instance, pages => { pages.SynchronizePages(instance, children); return true; }, out _);

    /// <summary>
    /// Whether <paramref name="instance"/>'s class showed the page whose tab is under a click at (x, y), in pixels from
    /// its top-left corner: false for a class without pages, a click on no tab, or a throw.
    /// </summary>
    internal static bool ShowPageAt(ComponentInstance instance, double x, double y, double width, double height,
        IReadOnlyList<ComponentInstance> children) =>
        AskPages(instance, pages => pages.ShowPageAt(instance, x, y, width, height, children), out var shown) && shown;

    /// <summary>Whether <paramref name="instance"/>'s class showed <paramref name="page"/>: false for a class without pages, or a throw.</summary>
    internal static bool ShowPage(ComponentInstance instance, int page, IReadOnlyList<ComponentInstance> children) =>
        AskPages(instance, pages => { pages.ShowPage(instance, page, children); return true; }, out _);

    /// <summary>The page <paramref name="instance"/> shows, or null for a class without pages, or a throw.</summary>
    internal static int? ShownPage(ComponentInstance instance) =>
        AskPages(instance, pages => pages.ShownPage(instance), out var page) ? page : null;

    /// <summary>How many pages <paramref name="instance"/> has, or null for a class without pages, or a throw.</summary>
    internal static int? PageCount(ComponentInstance instance) =>
        AskPages(instance, pages => pages.PageCount(instance), out var count) ? count : null;

    /// <summary>
    /// The page of <paramref name="container"/>'s child named <paramref name="childName"/>, or null when no page names
    /// it, the class has no pages, or it throws.
    /// </summary>
    internal static int? PageOf(ComponentInstance container, string childName) =>
        AskPages(container, pages => pages.PageOf(container, childName), out var page) ? page : null;

    /// <summary>
    /// Whether <paramref name="instance"/>'s class answered whether its page can be switched: false for a class without
    /// pages, or a throw, so a caller can refuse rather than switch without the class's check.
    /// </summary>
    internal static bool PageSwitchRefusal(ComponentInstance instance, out string? refusal) =>
        AskPages(instance, pages => pages.PageSwitchRefusal(instance), out refusal);

    private static bool AskPages<T>(ComponentInstance instance, Func<IDesignerPages, T> question, out T answer)
    {
        answer = default!;
        if (!HasPages(instance.BaseClass) || instance.BaseClass is not IDesignerPages pages)
            return false;
        try
        {
            answer = question(pages);
            return true;
        }
        catch (Exception ex)
        {
            Failed(pages, nameof(IDesignerPages), ex);
            answer = default!;
            return false;
        }
    }

    /// <summary>
    /// The library <paramref name="instance"/>'s class says it's in, or null for a class without the contract, one in no
    /// library, or a throw. The caller still checks that the registry lists the library as offering the class.
    /// </summary>
    internal static IControlLibrary? LibraryOf(ComponentInstance instance)
    {
        if (instance.BaseClass is not IDesignerLibraryControl control)
            return null;
        try
        {
            return control.LibraryOf(instance);
        }
        catch (Exception ex)
        {
            Failed(control, nameof(IDesignerLibraryControl), ex);
            return null;
        }
    }

    /// <summary>Tells <paramref name="instance"/>'s class which library's toolbox entry it was placed from.</summary>
    internal static void PlacedFrom(ComponentInstance instance, IControlLibrary library)
    {
        if (instance.BaseClass is not IDesignerLibraryControl control)
            return;
        try
        {
            control.PlacedFrom(instance, library);
        }
        catch (Exception ex)
        {
            Failed(control, nameof(IDesignerLibraryControl), ex);
        }
    }

    /// <summary>
    /// Logs that <paramref name="component"/> named a library the registry doesn't list as offering it, which declares
    /// nothing. The library isn't asked its name: it may be the class's own object.
    /// </summary>
    internal static void UnofferedLibrary(IComponentClass component) =>
        Failed(component, nameof(IDesignerLibraryControl), null);

    // What the designer can place and save: finite, no negative size, and each within a whole number of twips, as a
    // class's added size is (IDesignerComponent.AddedSize).
    private static bool Usable(DesignerBounds bounds) =>
        Within(bounds.Left) && Within(bounds.Top) && Within(bounds.Width) && Within(bounds.Height)
        && bounds.Width >= 0 && bounds.Height >= 0;

    /// <summary>
    /// The units VB6's Properties window shows a position in, as a reply names them (#1636). An answer naming any other unit
    /// isn't used, because the unit goes into automation's replies as the IDE's own text.
    /// </summary>
    internal static readonly IReadOnlyList<string> PositionUnits =
        ["twips", "points", "pixels", "characters", "inches", "millimetres", "centimetres", "user units"];

    /// <summary>
    /// Whether <paramref name="property"/> is a control's Left, Top, Width or Height, which the Properties window shows in
    /// VB6's units rather than the model's pixels.
    /// </summary>
    internal static bool IsPosition(PropertyClass property) =>
        ReferenceEquals(property, VBProperties.LeftProperty) || ReferenceEquals(property, VBProperties.TopProperty)
        || IsSize(property);

    /// <summary>
    /// <paramref name="property"/>'s value and unit as the Properties window shows them, from the model's
    /// <paramref name="pixels"/>. Null with no positions registered, after a throw, or for an answer that isn't a finite value
    /// in one of <see cref="PositionUnits"/>: the caller then shows the model's value, as with no form.
    /// </summary>
    internal static (double Value, string Unit)? ShownPosition(FormDefinition form, ComponentInstance component,
        PropertyClass property, double pixels)
    {
        if (Positions is not { } positions)
            return null;
        try
        {
            var shown = positions.Shown(form, component, property, pixels);
            if (double.IsFinite(shown.Value) && PositionUnits.Contains(shown.Unit, StringComparer.Ordinal))
                return shown;
            Failed(positions, nameof(IDesignPositions), null);
        }
        catch (Exception ex)
        {
            Failed(positions, nameof(IDesignPositions), ex);
        }
        return null;
    }

    /// <summary>
    /// The model's pixels for <paramref name="value"/>, typed in the unit <paramref name="property"/> is shown in. Null for
    /// a value no position can hold, and null with <paramref name="failed"/> set with no positions registered, after a throw,
    /// or for an answer that isn't finite or is a Width or Height below zero. Either way the caller changes nothing.
    /// </summary>
    internal static double? PositionPixels(FormDefinition form, ComponentInstance component, PropertyClass property,
        double value, out bool failed)
    {
        failed = true;
        if (Positions is not { } positions)
            return null;
        try
        {
            if (positions.ToPixels(form, component, property, value) is not { } pixels)
            {
                failed = false;
                return null;
            }
            if (double.IsFinite(pixels) && !(IsSize(property) && pixels < 0))
            {
                failed = false;
                return pixels;
            }
            Failed(positions, nameof(IDesignPositions), null);
        }
        catch (Exception ex)
        {
            Failed(positions, nameof(IDesignPositions), ex);
        }
        return null;
    }

    /// <summary>
    /// How many twips one unit of <paramref name="line"/>'s container is, across and down, in <paramref name="units"/>.
    /// True with the provider's usable answer. False with twips for a scale it can't run (#5759), when no positions are
    /// registered, after a throw, or for an answer that isn't finite and above zero. The designer lays the Line out and
    /// draws it in twips then and leaves it where it is, since a move written in twips would be wrong in any other scale.
    /// </summary>
    internal static bool LineTwipsPerUnit(FormDefinition form, ComponentInstance line, out (double X, double Y) units)
    {
        units = (1, 1);
        if (Positions is not { } positions)
            return false;
        try
        {
            if (positions.LineTwipsPerUnit(form, line) is not { } answer)
                return false;
            if (double.IsFinite(answer.X) && double.IsFinite(answer.Y) && answer.X > 0 && answer.Y > 0)
            {
                units = answer;
                return true;
            }
            Failed(positions, nameof(IDesignPositions), null);
        }
        catch (Exception ex)
        {
            Failed(positions, nameof(IDesignPositions), ex);
        }
        return false;
    }

    private static IDesignPositions? Positions => Addins.AddinDesignPositionService.For(Avalonia.Application.Current);

    private static bool IsSize(PropertyClass property) =>
        ReferenceEquals(property, VBProperties.WidthProperty) || ReferenceEquals(property, VBProperties.HeightProperty);

    private static bool Within(double pixels) =>
        double.IsFinite(pixels) && Math.Abs(pixels * VBScaleModeExtensions.PixelToTwips) <= int.MaxValue;

    private static void Failed(object component, string contract, Exception? ex)
    {
        var name = component is IComponentClass known ? known.VBTypeName : component.GetType().FullName;
        if (Reported.TryAdd((component.GetType(), contract), true))
            Log.Warning(ex, "Designer: {Class} failed its {Contract}; the designer answers as though it had none, and later "
                + "failures are logged at debug", name, contract);
        else
            Log.Debug(ex, "Designer: {Class} failed its {Contract} again", name, contract);
    }

    /// <summary>Forgets which failures were reported, so a test sees the first again.</summary>
    internal static void ForgetReported() => Reported.Clear();
}
