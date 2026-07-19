using System;
using System.IO;
using System.Threading.Tasks;
using HexIDE.Events;
using HexIDE.Localization;
using HexIDE.Projects;
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
    ILocalizationService localization)
{
    public async Task ReloadAsync(WatchedFileTarget t)
    {
        bool ok;
        if (t.Form is not null)
            ok = await projectService.ReloadFormFromDisk(t.Form);
        else if (t.Module is not null)
            ok = await projectService.ReloadModuleFromDisk(t.Module);
        else
            return;

        if (!ok)
            return;

        // Refresh open views from the freshly-updated model.
        if (t.CodeEditor is not null)
            t.CodeEditor.ReloadFrom(t.Form?.Code ?? t.Module?.Code ?? string.Empty);
        t.Designer?.ReloadFromModel();

        eventBus.Publish(new FileReloadedFromDiskEvent(t.SourcePath));

        var name = t.Form?.Name ?? t.Module?.Name ?? Path.GetFileName(t.SourcePath);
        statusBar.SetTemporaryMessage(
            string.Format(localization.GetString("Str.StatusBar.FileReloaded"), name),
            TimeSpan.FromSeconds(4));

        Log.Information("FileWatcher: reloaded {Path} from disk", t.SourcePath);
    }
}
