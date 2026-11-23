using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.IO;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Projects;

public partial class ProjectService
{
    // A path's watcher baseline follows the latest write. A project's copy must keep its own earlier
    // disk and render baselines after No to the update question (#2756).
    private sealed record CopyBaseline(string Render, string? CompanionRender, string? Disk, string? CompanionDisk);
    private readonly ConditionalWeakTable<object, CopyBaseline> copyBaselines = new();

    private static string? MemberPath(object member) => member switch
    {
        FormDefinition form => form.AbsolutePath,
        ModuleDefinition module => module.AbsolutePath,
        _ => null,
    };

    private static ProjectDefinition MemberProject(object member) => member switch
    {
        FormDefinition form => form.Owner,
        ModuleDefinition module => module.Owner,
        _ => throw new ArgumentException("Expected a form or module.", nameof(member)),
    };

    /// <summary>Every loaded copy of a member file, using normalized host paths rather than .vbp spellings.</summary>
    private List<object> SharedCopies(string path) => projectManager.LoadedProjects
        .SelectMany(project => project.Forms.Cast<object>().Concat(project.Modules))
        .Where(member => MemberPath(member) is { } memberPath && HostFilePath.Equals(memberPath, path))
        .ToList();

    private bool MemberDirty(object member) => member is FormDefinition form ? IsDirty(form) : IsDirty((ModuleDefinition)member);

    internal bool ActivateOtherSharedCode(object member, IDocumentDockService dock)
    {
        if (MemberPath(member) is not { } path) return false;
        var copies = SharedCopies(path).Where(copy => !ReferenceEquals(copy, member)).ToList();
        var open = dock.OpenDocuments.OfType<CodeEditorViewModel>()
            .FirstOrDefault(editor => copies.Contains((object?)editor.ModuleDefinition ?? editor.FormDefinition!));
        if (open is null) return false;
        ShowAlreadyOpen().ListenErrors();
        return true;

        async Task ShowAlreadyOpen()
        {
            await windowManager.MessageBox(localization.GetString("Str.Dialog.SharedFile.AlreadyOpen"));
            dock.TryActivate<CodeEditorViewModel>(editor => ReferenceEquals(editor, open));
        }
    }

    internal async Task SaveSharedCodeOnClose(CodeEditorViewModel editor)
    {
        var member = (object?)editor.ModuleDefinition ?? editor.FormDefinition;
        if (member is null || MemberPath(member) is not { } path || SharedCopies(path).Count < 2) return;
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());
        if (!MemberDirty(member)
            || await SharedQuestion("Str.Dialog.SharedFile.SaveOnClose", MemberName(member),
                MemberProject(member).Name) != MessageBoxResult.Yes)
            return;
        if (member is FormDefinition form) await SaveForm(form, false);
        else await SaveModule((ModuleDefinition)member, false);
    }

    private void RememberCopy(object member, string path, string rendered, byte[]? companion)
    {
        var companionPath = CompanionPathOf(MemberProject(member), path);
        copyBaselines.AddOrUpdate(member, new CopyBaseline(FileHasher.Hash(rendered),
            companion is { Length: > 0 } ? FileHasher.Hash(companion) : null,
            baselineStore.TryGet(path)?.Hash,
            companionPath is null ? null : baselineStore.TryGet(companionPath)?.Hash));
    }

    private bool CopyMatches(object member, string rendered, byte[]? companion) =>
        copyBaselines.TryGetValue(member, out var baseline)
        && baseline.Render == FileHasher.Hash(rendered)
        && baseline.CompanionRender == (companion is { Length: > 0 } ? FileHasher.Hash(companion) : null);

    private static string? DiskHash(string path) => File.Exists(path) ? FileHasher.Hash(File.ReadAllBytes(path)) : null;

    private bool CopyDiskChanged(object member, string path)
    {
        if (!copyBaselines.TryGetValue(member, out var baseline))
            return true;
        try
        {
            return baseline.Disk != DiskHash(path)
                   || (CompanionPathOf(MemberProject(member), path) is { } companionPath
                       && baseline.CompanionDisk != DiskHash(companionPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private Task<MessageBoxResult> SharedQuestion(string key, params object[] values) => windowManager.MessageBox(
        string.Format(localization.GetString(key), values), buttons: MessageBoxButtons.YesNo,
        icon: MessageBoxIcon.Warning, layout: MessageBoxLayout.SecondButtonDefault);

    private sealed class SharedFileSaveRefusal(string message) : InvalidOperationException(message);

    private async Task ResolveSharedSaves(List<Destination> destinations, bool interactive)
    {
        foreach (var group in destinations.Where(d => d.Document is FormDefinition or ModuleDefinition)
                     .GroupBy(d => Path.GetFullPath(d.Path), HostFilePath.Comparer).ToArray())
        {
            var candidates = group.ToList();
            if (candidates.Count < 2 && !SharedCopies(group.Key).Any(member => !ReferenceEquals(member, candidates[0].Document)))
                continue;
            if (candidates.Count > 1)
            {
                var changed = candidates.Where(d => MemberDirty(d.Document)).ToList();
                Destination? chosen = changed.Count == 1 ? changed[0] : null;
                if (changed.Count > 1)
                {
                    if (!interactive)
                        throw new SharedFileSaveRefusal($"Cannot save shared file '{group.Key}': multiple modified copies need a choice and consent.");
                    var vm = new SaveChangesViewModel();
                    foreach (var destination in changed)
                    {
                        var label = $"{Path.GetFileName(destination.Path)} ({MemberProject(destination.Document).Name})";
                        var row = destination.Document is FormDefinition form
                            ? new ChangedFileViewModel(form, label)
                            : new ChangedFileViewModel((ModuleDefinition)destination.Document, label);
                        vm.ChangedFiles.Add(row);
                        vm.SelectedFiles.Add(row);
                    }
                    while (true)
                    {
                        await windowManager.MessageBox(
                            string.Format(localization.GetString("Str.Dialog.SharedFile.MultipleModified"), group.Key),
                            icon: MessageBoxIcon.Warning);
                        if (!await windowManager.ShowDialog(vm) || !vm.SaveChanges || vm.SelectedFiles.Count == 0)
                            break;
                        if (vm.SelectedFiles.Count != 1)
                            continue;
                        var row = vm.SelectedFiles[0];
                        chosen = changed.Single(d => ReferenceEquals(d.Document, (object?)row.Form ?? row.Module));
                        break;
                    }
                }
                destinations.RemoveAll(d => candidates.Contains(d) && d != chosen);
                if (chosen is null)
                    continue;
                candidates = [chosen.Value];
            }
            foreach (var destination in candidates)
            {
                if (!interactive)
                    throw new SharedFileSaveRefusal($"Cannot save shared file '{destination.Path}': updating other projects' copies requires consent.");
                if (CopyDiskChanged(destination.Document, destination.Path)
                    && await SharedQuestion("Str.Dialog.SharedFile.DiskChanged", Path.GetFullPath(destination.Path),
                        MemberProject(destination.Document).Name) != MessageBoxResult.Yes)
                    destinations.Remove(destination);
            }
        }
    }

    private async Task UpdateSharedCopies(Destination written)
    {
        var copies = SharedCopies(written.Path).Where(member => !ReferenceEquals(member, written.Document)).ToList();
        if (copies.Count == 0
            || await SharedQuestion("Str.Dialog.SharedFile.Update", MemberName(written.Document),
                MemberProject(written.Document).Name) != MessageBoxResult.Yes)
            return;

        // Flush once more after the save's awaits before asking whether another copy's edits may go.
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());
        // Unannounced replacements refresh the views without using a status bar.
        var reloader = new FileReloader(this, eventBus, null!, localization, headerRefresher);
        foreach (var member in copies)
        {
            if (MemberDirty(member)
                && await SharedQuestion("Str.Dialog.SharedFile.DiscardChanges", MemberName(written.Document),
                    MemberProject(member).Name) != MessageBoxResult.Yes)
                continue;
            var form = member as FormDefinition;
            var module = member as ModuleDefinition;
            var target = WatchedFileTarget.For(MemberPath(member)!, form, module,
                documents?.OpenDocuments ?? []);
            await reloader.ReplaceAsync(target,
                () => form is not null ? ReloadFormFromDisk(form) : ReloadModuleFromDisk(module!), announce: false);
        }
    }
}
