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
    public async Task ReloadAsync(WatchedFileTarget t)
    {
        // Read BEFORE the model is replaced: a reload adopts the file's header along with the rest of the
        // fidelity state, so by the time the reload returns the old one is gone. Read off the model rather
        // than off the code window, because a document with no window open still has marks to move.
        var identity = t.Form is not null ? DocumentIdentity.For(t.Form)
            : t.Module is not null ? DocumentIdentity.For(t.Module)
            : null;
        var oldPrefix = t.Form is not null ? FormCodeText.Prefix(t.Form)
            : t.Module is not null ? FormCodeText.Prefix(t.Module)
            : "";

        bool ok;
        if (t.Form is not null)
            ok = await projectService.ReloadFormFromDisk(t.Form);
        else if (t.Module is not null)
            ok = await projectService.ReloadModuleFromDisk(t.Module);
        else
            return;

        if (!ok)
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

        eventBus.Publish(new FileReloadedFromDiskEvent(t.SourcePath));

        var name = t.Form?.Name ?? t.Module?.Name ?? Path.GetFileName(t.SourcePath);
        statusBar.SetTemporaryMessage(
            string.Format(localization.GetString("Str.StatusBar.FileReloaded"), name),
            TimeSpan.FromSeconds(4));

        Log.Information("FileWatcher: reloaded {Path} from disk", t.SourcePath);
    }
}
