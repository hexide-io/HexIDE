using HexIDE.Runtime.ProjectElements;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace HexIDE.Debugging;

/// <summary>
/// Which project the current run belongs to.
///
/// <para>
/// The interpreter's debug controller reports a pause by bare module name, and rightly so: a run is one
/// project, and its gate knows nothing of the IDE's documents. But a code editor receiving that name has to
/// decide whether the pause is in <em>its</em> document, and with two projects of a group each holding a
/// <c>Module1</c> the name alone cannot answer. So the editor asks this which project is running, and matches
/// on the document's identity rather than on its name.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// A service of its own rather than a property on the project runner, and a value rather than an event, for
/// two reasons that both come from where it is read.
/// </para>
/// <para>
/// An editor cannot depend on the runner: the runner is reached through the editor factory, so the
/// dependency would close a cycle. It is the same reason the reset prompt is routed through the event bus.
/// </para>
/// <para>
/// And an event alone would not do, because the editor that most needs this is the one opened <em>by</em> a
/// break — the run started before that view model existed, so it never saw the notification. A value can be
/// asked at the moment it matters; a notification can only be heard while you are listening.
/// </para>
/// </remarks>
public interface IRunScope
{
    /// <summary>The project currently running, or null when nothing is.</summary>
    ProjectDefinition? RunningProject { get; }

    /// <summary>Records an editor-buffer edit while a run is active.</summary>
    void RecordEdit(DocumentIdentity document);
}

/// <inheritdoc cref="IRunScope"/>
public sealed class RunScope : IRunScope
{
    /// <summary>Set by the project runner as a run starts and ends. Nothing else writes it.</summary>
    public ProjectDefinition? RunningProject { get; set; }

    private readonly HashSet<DocumentIdentity> changed = [];
    private readonly List<Action> unsubscribe = [];
    private bool active;

    internal void Begin(IEnumerable<ProjectDefinition> projects)
    {
        End();
        active = true;
        foreach (var project in projects)
        {
            foreach (var form in project.Forms) Watch(form, DocumentIdentity.For(form));
            foreach (var module in project.Modules) Watch(module, DocumentIdentity.For(module));
        }
    }

    private void Watch(INotifyPropertyChanged model, DocumentIdentity document)
    {
        void Changed(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "Code") RecordEdit(document);
        }
        model.PropertyChanged += Changed;
        unsubscribe.Add(() => model.PropertyChanged -= Changed);
    }

    public void RecordEdit(DocumentIdentity document)
    {
        if (active) changed.Add(document);
    }

    internal void End()
    {
        active = false;
        foreach (var detach in unsubscribe) detach();
        unsubscribe.Clear();
        changed.Clear();
    }

    internal string? Note(bool paused, bool edit)
    {
        if (!active || (!edit && changed.Count == 0)) return null;
        // Breakpoints are stale while running too, not only while paused: one set on the new text's line 42 never
        // fired, because the running code's line 42 is another procedure (#3191).
        var note = " The project is " + (paused ? "paused" : "running")
            + ". It keeps the code it started with; " + (edit ? "this change takes" : "a change made during the run takes")
            + " effect at the next run: stop_project ends this run, then start it again the same way. "
            + (paused ? "The break location and breakpoints keep" : "Breakpoints keep")
            + " the running code's line numbers, so they may not match the new text until the next run.";
        if (changed.Count > 0)
            note += " Documents changed since the run started: "
                + string.Join(", ", changed.Select(d => d.Project.Name + "." + d.Name).OrderBy(n => n, StringComparer.Ordinal)) + ".";
        return note;
    }
}
