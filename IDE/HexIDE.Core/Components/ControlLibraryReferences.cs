using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Components;

/// <summary>Which control libraries a project references, and in what order (#5610), over any <see cref="IControlLibrary"/>.</summary>
internal static class ControlLibraryReferences
{
    /// <summary>
    /// The libraries of <paramref name="libraries"/> that <paramref name="project"/> names on an <c>Object=</c> line, then any
    /// a form's header names that the project doesn't, each once, in that order.
    /// </summary>
    /// <remarks>VB6 writes both lines for a form that holds the control, and a form copied in from another project may
    /// bring its header line alone; the CommonDialog's reference is read the same way (#1084).</remarks>
    internal static IReadOnlyList<T> NamedByObjectLines<T>(ProjectDefinition project, IReadOnlyList<T> libraries)
        where T : class, IControlLibrary
    {
        var referenced = new List<T>();
        foreach (var reference in project.ObjectReferences.Concat(project.Forms.SelectMany(f => f.ObjectReferences)))
            if (libraries.FirstOrDefault(l => l.IsNamedBy(reference)) is { } library && !referenced.Contains(library))
                referenced.Add(library);
        return referenced;
    }

    /// <summary>
    /// The libraries <paramref name="project"/> references, in its <c>.vbp</c>'s order: <paramref name="references"/> it
    /// names on a <c>Reference=</c> line and <paramref name="controls"/> on an unread line before its sections, each at its
    /// slot, then <see cref="NamedByObjectLines"/>'s, each once.
    /// </summary>
    internal static IReadOnlyList<T> ReferencedBy<T>(ProjectDefinition project, IReadOnlyList<T> references,
        IReadOnlyList<T> controls) where T : class, IControlLibrary
    {
        var result = new List<T>();
        var entries = new List<(int Slot, int Kind, T Library)>();
        var referenceSlots = project.KnownLineKeys.Select((key, slot) => (key, slot))
            .Where(entry => entry.key.Equals("Reference", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.slot).ToList();
        for (var i = 0; i < project.References.Count; i++)
            foreach (var library in references)
                if (library.IsNamedBy(project.References[i]))
                    entries.Add((i < referenceSlots.Count ? referenceSlots[i] : -1, 1, library));
        var afterForms = project.KnownLineKeys.FindLastIndex(key => key.Equals("Form", StringComparison.OrdinalIgnoreCase)) + 1;
        foreach (var (hint, line) in project.UnknownPreSectionLines)
            if (ObjectReference.TryParse(line, out var reference) && controls.FirstOrDefault(l => l.IsNamedBy(reference)) is { } library)
                entries.Add((hint == ProjectDefinition.AfterFormsHint ? afterForms : hint, 0, library));
        // Unknown lines precede the recognised key at their slot; ties retain their read order (#2265).
        foreach (var entry in entries.OrderBy(entry => entry.Slot).ThenBy(entry => entry.Kind))
            if (!result.Contains(entry.Library)) result.Add(entry.Library);
        foreach (var library in NamedByObjectLines(project, controls))
            if (!result.Contains(library)) result.Add(library);
        return result;
    }
}
