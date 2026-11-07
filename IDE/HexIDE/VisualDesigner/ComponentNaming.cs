using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.VisualDesigner;

/// <summary>
/// The rules a rename of a control or form must pass, in one place, so every route that renames applies the same
/// ones: the Properties window through the designer, and <c>set_control_property</c> with or without a designer
/// open (#494).
/// </summary>
public static class ComponentNaming
{
    /// <summary>
    /// Why <paramref name="target"/> may not be renamed from <paramref name="oldName"/> to
    /// <paramref name="proposed"/>, or null when it may.
    /// </summary>
    /// <param name="formComponents">Every component of the form, the root included.</param>
    /// <param name="document">The form the components belong to; null for a designer with no document.</param>
    /// <param name="isRoot">Whether <paramref name="target"/> is the form itself, whose name is the document's.</param>
    public static string? RefusalFor(
        ComponentInstance target, string? oldName, string? proposed,
        IEnumerable<ComponentInstance> formComponents, FormDefinition? document, bool isRoot,
        ILocalizationService localization)
    {
        if (string.IsNullOrEmpty(proposed))
            return localization.GetString("Str.Naming.Msg.EmptyName");

        // A commit that does not change the name collides with nothing, and that is the case this guard
        // kept rejecting. The property grid commits Name on every focus change, so it fired constantly;
        // and it fired on real VB6 data, because a control ARRAY shares one name across its elements —
        // Options Dialog.frm has four sibling controls all called picOptions, and Treeview Listview
        // Splitter.frm has two lblTitle inside one picTitles — so the moment one of those was touched,
        // "unique in form" flagged it as a duplicate of its own siblings.
        if (string.Equals(oldName, proposed, StringComparison.Ordinal))
            return null;

        // Every name is a VB6 identifier, a control's as much as the form's. Only the form was checked, so a
        // control could be called "My Button" and the .frm written as `Begin VB.CommandButton My Button` (#628).
        // A control array shares one name, but a shared name is still a valid one.
        if (!ProjectNaming.IsValidName(proposed))
            return string.Format(localization.GetString("Str.Naming.Msg.NotAVb6Name"), proposed);

        // Every name on the form counts, a preserved control's too, in any case (#854). The target's own is left out, so
        // a change of case alone passes. VB6's rule is uniqueness by name AND Index, so a name already held is taken only
        // where the rename would not join an array: an element may take an array's name when its Index is free there
        // (#1276), which is how an element renamed out of its array is renamed back.
        if (NamesInUse(formComponents, except: target).Contains(proposed) && !JoinsAnArray(target, proposed, formComponents))
            return localization.GetString("Str.Naming.Msg.NameNotUniqueInForm");

        // Renaming the ROOT renames the document, so the project's rule applies on top of the form's: a form
        // and a module of one project may not share a name, because VB6 gives them one namespace.
        if (isRoot && document is not null
            && ProjectNaming.IsNameTaken(document.Owner, proposed, DocumentIdentity.For(document)))
            return string.Format(localization.GetString("Str.Naming.Msg.DocumentNameTaken"), proposed);

        return null;
    }

    /// <summary>
    /// Whether naming <paramref name="target"/> <paramref name="proposed"/> makes it an element of an array of that name:
    /// it has an Index, every control already holding the name has one, and none holds its Index (#1276).
    /// </summary>
    /// <remarks>
    /// A preserved control holding the name refuses it: its Index is in text HexIDE does not read. A control with no Index
    /// does not join an array, and an array does not take a control with none: in VB6 that offers to make a new array,
    /// which is not measured, so it stays a refusal.
    /// </remarks>
    private static bool JoinsAnArray(ComponentInstance target, string proposed, IEnumerable<ComponentInstance> formComponents)
    {
        if (PropertyText.ArrayIndexOf(target) is not { } index)
            return false;
        if (PreservedNames(formComponents).Contains(proposed, StringComparer.OrdinalIgnoreCase))
            return false;
        var holders = formComponents.Where(c => !ReferenceEquals(c, target)
            && string.Equals(c.GetPropertyOrDefault(VBProperties.NameProperty), proposed, StringComparison.OrdinalIgnoreCase)).ToList();
        return holders.Count > 0 && holders.All(c => PropertyText.ArrayIndexOf(c) is { } held && held != index);
    }

    /// <summary>
    /// The names in use on a form, compared without regard to case: every component's, and every control the form keeps
    /// as preserved text (#854). <paramref name="except"/>'s own name is left out; its preserved children still count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Without regard to case</b>, because vb6.exe loads a form that way: <c>Begin VB.TextBox timer1</c> after a
    /// Timer1 is logged as "control timer1 already exists", and the program is built without either control (measured,
    /// the oracle's "Two controls of one name").
    /// </para>
    /// <para>
    /// <b>Preserved controls</b> have no <see cref="ComponentInstance"/> (#157). Their names are read from the
    /// <c>Begin</c> lines of the text kept on their container, nested ones included, which the deserializer writes in one
    /// shape. <c>BeginProperty</c> is not a control.
    /// </para>
    /// </remarks>
    public static HashSet<string> NamesInUse(IEnumerable<ComponentInstance> formComponents, ComponentInstance? except = null)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in formComponents)
        {
            if (!ReferenceEquals(component, except)
                && component.GetPropertyOrDefault(VBProperties.NameProperty) is { Length: > 0 } name)
                names.Add(name);
            foreach (var preserved in component.PreservedChildSubtrees)
                names.UnionWith(NamesDeclaredBy(preserved.Text));
        }
        return names;
    }

    /// <summary>The names on the <c>Begin</c> lines of every block preserved on <paramref name="formComponents"/>.</summary>
    private static IEnumerable<string> PreservedNames(IEnumerable<ComponentInstance> formComponents) =>
        formComponents.SelectMany(c => c.PreservedChildSubtrees).SelectMany(preserved => NamesDeclaredBy(preserved.Text));

    /// <summary>The names of the controls a preserved block declares: its own, and those nested in it.</summary>
    public static IEnumerable<string> NamesDeclaredBy(string preservedText) =>
        BeginLine.Matches(preservedText).Select(begin => begin.Groups["name"].Value);

    private static readonly Regex BeginLine = new(@"^\s*Begin\s+\S+\s+(?<name>\S+)", RegexOptions.Multiline);
}
