using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaEdit.Document;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Utils;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

public sealed class RecentEditHistory
{
    private readonly List<RecentEditLocation> entries = new();
    private readonly HashSet<ProjectDefinition> projects = new();
    public IReadOnlyList<RecentEditLocation> Entries => entries;
    public event Action? Changed;

    public RecentEditHistory(IEventBus eventBus)
    {
        eventBus.Subscribe<ProjectUnloadedEvent>(e =>
        {
            RemoveWhere(entry => entry.Document.IsIn(e.Project));
            if (!projects.Remove(e.Project)) return;
            e.Project.FormDeleted -= FormDeleted;
            e.Project.ModuleDeleted -= ModuleDeleted;
        });
    }

    private void FormDeleted(ProjectDefinition project, FormDefinition form) =>
        RemoveWhere(entry => entry.Document == DocumentIdentity.For(form));
    private void ModuleDeleted(ProjectDefinition project, ModuleDefinition module) =>
        RemoveWhere(entry => entry.Document == DocumentIdentity.For(module));

    private void RemoveWhere(Predicate<RecentEditLocation> predicate)
    {
        if (entries.RemoveAll(predicate) > 0) Changed?.Invoke();
    }

    public IDisposable Track(DocumentIdentity identity, TextDocument text)
    {
        if (projects.Add(identity.Project))
        {
            identity.Project.FormDeleted += FormDeleted;
            identity.Project.ModuleDeleted += ModuleDeleted;
        }
        foreach (var entry in entries.Where(entry => entry.Document == identity))
            entry.Anchor = AnchorAt(text, text.GetLineByNumber(Math.Min(entry.Line, text.LineCount)).Offset);

        int? editOffset = null;
        RecentEditLocation? replacedEntry = null;
        void Changing(object? sender, DocumentChangeEventArgs e)
        {
            var editLine = text.GetLineByOffset(e.Offset).LineNumber;
            replacedEntry = entries.Count > 0 && entries[0].Document == identity && Math.Abs(entries[0].Line - editLine) <= 10
                ? entries[0] : null;
            // Drop a location only when its whole line goes away, not when its first character is replaced.
            RemoveWhere(entry => entry.Document == identity && entry.Anchor is { IsDeleted: false } anchor &&
                text.GetLineByOffset(anchor.Offset) is { } line && e.RemovalLength > 0 &&
                e.Offset <= line.Offset && e.Offset + e.RemovalLength >= line.Offset + line.TotalLength);
            editOffset = e.Offset;
        }
        void TextChanged(object? sender, EventArgs e)
        {
            foreach (var entry in entries.Where(entry => entry.Document == identity))
                entry.Refresh(text);
            if (editOffset is not { } offset || !projects.Contains(identity.Project)) return;
            if (identity.Module is { } module && !identity.Project.Modules.Contains(module)) return;
            if (identity.Form is { } form && !identity.Project.Forms.Contains(form)) return;
            editOffset = null;
            var line = text.GetLineByOffset(Math.Min(offset, text.TextLength));
            if (replacedEntry is not null) entries.Remove(replacedEntry);
            replacedEntry = null;
            var location = new RecentEditLocation(identity) { Anchor = AnchorAt(text, line.Offset) };
            location.Refresh(text);
            entries.Insert(0, location);
            if (entries.Count > 50) entries.RemoveAt(50);
            Changed?.Invoke();
        }
        text.Changing += Changing;
        text.TextChanged += TextChanged;
        return new ActionDisposable(() =>
        {
            text.Changing -= Changing;
            text.TextChanged -= TextChanged;
            foreach (var entry in entries.Where(entry => entry.Document == identity)) entry.Anchor = null;
        });
    }

    private static TextAnchor AnchorAt(TextDocument text, int offset)
    {
        var anchor = text.CreateAnchor(offset);
        anchor.MovementType = AnchorMovementType.AfterInsertion;
        anchor.SurviveDeletion = true;
        return anchor;
    }
}

public sealed partial class RecentEditLocation(DocumentIdentity document)
{
    public DocumentIdentity Document { get; } = document;
    public string Module => Document.Display;
    [Notify] private int line = 1;
    [Notify] private string preview = "";
    internal TextAnchor? Anchor { get; set; }

    internal void Refresh(TextDocument text)
    {
        if (Anchor is null) return;
        var line = text.GetLineByOffset(Anchor.Offset);
        Line = line.LineNumber;
        Preview = text.GetText(line.Offset, line.Length).Trim();
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Module)));
    }

    public override string ToString() => $"{Module}: {Line} — {Preview}";
}
