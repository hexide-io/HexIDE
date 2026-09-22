using System;
using System.Collections.Generic;
using System.Linq;
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
            return "Name can't be empty";

        // A commit that does not change the name collides with nothing, and that is the case this guard
        // kept rejecting. The property grid commits Name on every focus change, so it fired constantly;
        // and it fired on real VB6 data, because a control ARRAY shares one name across its elements —
        // Options Dialog.frm has four sibling controls all called picOptions, and Treeview Listview
        // Splitter.frm has two lblTitle inside one picTitles — so the moment one of those was touched,
        // "unique in form" flagged it as a duplicate of its own siblings.
        if (string.Equals(oldName, proposed, StringComparison.Ordinal))
            return null;

        // For a genuine rename the check stands, and it stays strict on purpose. VB6's real rule is
        // uniqueness per name AND Index; Index is not modelled yet, so a rename that would JOIN an
        // existing array cannot be told apart from a collision, and refusing is the recoverable answer.
        if (formComponents.Any(c => !ReferenceEquals(c, target)
                                    && c.GetPropertyOrDefault(VBProperties.NameProperty) == proposed))
            return "Name must be unique in form";

        // Renaming the ROOT renames the document, so the project's rules apply on top of the form's.
        // A form and a module of one project may not share a name -- VB6 gives them one namespace --
        // and the name is part of the only identifier a document with no file can be given to a
        // language server, so it has to be a name rather than merely a string.
        if (isRoot && document is not null)
        {
            if (!ProjectNaming.IsValidName(proposed))
                return string.Format(localization.GetString("Str.Naming.Msg.NotAVb6Name"), proposed);

            if (ProjectNaming.IsNameTaken(document.Owner, proposed, DocumentIdentity.For(document)))
                return string.Format(localization.GetString("Str.Naming.Msg.DocumentNameTaken"), proposed);
        }

        return null;
    }
}
