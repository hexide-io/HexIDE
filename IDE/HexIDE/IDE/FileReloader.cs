using System;
using System.IO;
using System.Threading.Tasks;
using HexIDE.Events;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using Serilog;

namespace HexIDE.IDE;

/// <summary>
/// Applies a clean reload for a <see cref="WatchedFileTarget"/>: updates the in-memory model from disk
/// (via <see cref="IProjectService"/>), then refreshes any open code editor and/or designer from the
/// updated model, publishes <see cref="FileReloadedFromDiskEvent"/>, and posts a transient status-bar
/// message. Must be called on the UI thread.
/// </summary>
public sealed class FileReloader(
    IProjectService projectService,
    IEventBus eventBus,
    IStatusBarService statusBar,
    ILocalizationService localization,
    IHeaderRefresher headerRefresher)
{
    public Task ReloadAsync(WatchedFileTarget t) => ReplaceAsync(t,
        () => t.Form is not null ? projectService.ReloadFormFromDisk(t.Form) : projectService.ReloadModuleFromDisk(t.Module!),
        announce: true);

    /// <summary>
    /// Replaces <paramref name="t"/>'s model with <paramref name="replace"/>, which says whether it did, and refreshes
    /// its open views from the result, as a reload does. Crash recovery's restore comes through here with its own
    /// replacement (hexide-io/HexIDE#499), and without the reload's announcement, since nothing was read from disk.
    /// </summary>
    public async Task ReplaceAsync(WatchedFileTarget t, Func<Task<bool>> replace, bool announce)
    {
        if (t.Form is null && t.Module is null)
            return;

        // Read BEFORE the model is replaced: a reload adopts the file's header along with the rest of the
        // fidelity state, so by the time the reload returns the old one is gone. Read off the model rather
        // than off the code window, because a document with no window open still has marks to move.
        var identity = t.Form is not null ? DocumentIdentity.For(t.Form)
            : t.Module is not null ? DocumentIdentity.For(t.Module)
            : null;
        var oldPrefix = t.Form is not null ? FormCodeText.Prefix(t.Form)
            : t.Module is not null ? FormCodeText.Prefix(t.Module)
            : "";

        if (!await replace())
            return;

        // The third refresh trigger (#273 task 3.3a), and the one that was left broken by 3.2: this used to
        // push the bare code section into a buffer whose prefix was still the header read when the document
        // was opened. The window lost its header, and — because the split is by the prefix's length — the
        // next flush cut the head off the reloaded code and wrote the remainder back as the document.
        var newPrefix = t.Form is not null ? FormCodeText.Prefix(t.Form)
            : t.Module is not null ? FormCodeText.Prefix(t.Module)
            : "";
        if (identity is not null)
            headerRefresher.ShiftMarks(identity, oldPrefix, newPrefix);

        // Refresh open views from the freshly-updated model.
        t.CodeEditor?.ReloadFrom(newPrefix, t.Form?.Code ?? t.Module?.Code ?? string.Empty);
        t.Designer?.ReloadFromModel();

        if (!announce)
            return;
        eventBus.Publish(new FileReloadedFromDiskEvent(t.SourcePath));

        var name = t.Form?.Name ?? t.Module?.Name ?? Path.GetFileName(t.SourcePath);
        statusBar.SetTemporaryMessage(
            string.Format(localization.GetString("Str.StatusBar.FileReloaded"), name),
            TimeSpan.FromSeconds(4));

        Log.Information("FileWatcher: reloaded {Path} from disk", t.SourcePath);
    }
}
