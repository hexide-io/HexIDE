using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using HexIDE.Addins;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Lsp;

namespace HexIDE.IDE;

public sealed class WorkspaceArtifactConsent : IWorkspaceArtifactConsent
{
    internal static ConsentStore Store { get; } = new(Path.Combine(UserDataPath.Directory, "lsp-artifact-consent.json"));
    private readonly Dictionary<string, bool> _session = new(StringComparer.Ordinal);

    private readonly ConsentStore _store;
    private readonly Func<string, string, Task<WorkspaceArtifactDecision>> _prompt;

    public WorkspaceArtifactConsent(IWindowManager windows, ILocalizationService localization)
    {
        _store = Store;
        _prompt = (server, path) => Dispatcher.UIThread.InvokeAsync<WorkspaceArtifactDecision>(async () =>
        {
            var dialog = new WorkspaceArtifactConsentViewModel(server, path, localization);
            await windows.ShowDialog(dialog);
            return dialog.Decision;
        });
    }

    internal WorkspaceArtifactConsent(ConsentStore store,
        Func<string, string, Task<WorkspaceArtifactDecision>> prompt)
    {
        _store = store;
        _prompt = prompt;
    }

    internal static string Key(string serverId, string workspace) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(serverId + "\0" + (OperatingSystem.IsWindows()
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace)).ToUpperInvariant()
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace))))));

    public async Task<bool> AllowAsync(string serverId, string serverName, string workspace, string path)
    {
        var key = Key(serverId, workspace);
        if (_store.GetDecision(key) == ConsentDecisions.Allow) return true;
        if (_session.TryGetValue(key, out var allowed)) return allowed;
        var decision = await _prompt(serverName, path);
        _session[key] = decision != WorkspaceArtifactDecision.Deny;
        if (decision == WorkspaceArtifactDecision.AllowAlways)
            _store.RecordWorkspace(serverId, workspace, key);
        return _session[key];
    }
}
