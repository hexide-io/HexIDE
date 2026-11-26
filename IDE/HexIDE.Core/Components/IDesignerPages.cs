using System.Collections.Generic;

namespace HexIDE.Runtime.Components;

/// <summary>
/// A container whose children sit on pages, only one of which the form designer shows at a time, as VB6's SSTab's do
/// (#5742). Optional: the designer and automation ask a container's class for its pages only through this.
/// </summary>
/// <remarks>
/// <para>
/// Only a class whose <see cref="IComponentClass.IsContainer"/> is true is asked. A plain control's class that implements
/// this is never asked for pages.
/// </para>
/// <para>
/// The designer asks only while the form can be saved faithfully: a form it opens read-only is shown as it was read, and
/// its pages are neither loaded nor recorded.
/// </para>
/// <para>
/// <b>Each call is contained on its own. A throw is logged, and that call answers as a container without pages would:</b>
/// its pages aren't loaded or recorded, a click doesn't switch its page, and automation reports no page for a child, and
/// no shown page or page count. When a check or switch that automation asks for throws, automation refuses to switch the
/// page rather than switch it without the class's check, and doesn't save the form.
/// </para>
/// </remarks>
public interface IDesignerPages
{
    /// <summary>
    /// Reads which page each of <paramref name="instance"/>'s children is on, and which page it shows, when the designer
    /// opens its form.
    /// </summary>
    void LoadPages(ComponentInstance instance);

    /// <summary>
    /// Records which page each of <paramref name="children"/> is on, before the designer's components go into the model:
    /// a child added or moved onto the page shown joins it.
    /// </summary>
    /// <param name="instance">The container.</param>
    /// <param name="children">The children it holds now, in the order they're saved.</param>
    void SynchronizePages(ComponentInstance instance, IReadOnlyList<ComponentInstance> children);

    /// <summary>The page <paramref name="instance"/> shows, from 0.</summary>
    int ShownPage(ComponentInstance instance);

    /// <summary>How many pages <paramref name="instance"/> has.</summary>
    int PageCount(ComponentInstance instance);

    /// <summary>The page the child named <paramref name="childName"/> is on, or null when no page names it.</summary>
    int? PageOf(ComponentInstance instance, string childName);

    /// <summary>
    /// Why <paramref name="instance"/>'s page can't be switched in the designer, in a sentence, or null when it can.
    /// Automation quotes it in its reply, saying that the class refused and that nothing was changed.
    /// </summary>
    string? PageSwitchRefusal(ComponentInstance instance);

    /// <summary>
    /// Shows the page whose tab is under a click at (<paramref name="x"/>, <paramref name="y"/>), in the designer's
    /// pixels from <paramref name="instance"/>'s top-left corner, with the container
    /// <paramref name="width"/> by <paramref name="height"/> pixels.
    /// </summary>
    /// <returns>Whether a tab was under the click. When none was, nothing changes.</returns>
    bool ShowPageAt(ComponentInstance instance, double x, double y, double width, double height,
        IReadOnlyList<ComponentInstance> children);

    /// <summary>
    /// Shows page <paramref name="page"/>, from 0 to one less than <see cref="PageCount"/>, moving
    /// <paramref name="children"/> on and off the designer's surface to match.
    /// </summary>
    void ShowPage(ComponentInstance instance, int page, IReadOnlyList<ComponentInstance> children);
}
