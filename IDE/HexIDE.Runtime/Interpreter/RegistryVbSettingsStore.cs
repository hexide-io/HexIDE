using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using HexIDE.IDE;
using Microsoft.Win32;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// Settings in the registry, where vb6.exe keeps them (#1562, measured): a value of type REG_SZ named by its key, under
/// <c>HKEY_CURRENT_USER\Software\VB and VBA Program Settings\</c> the program's name and its section. A program run in
/// HexIDE and the same program compiled by VB6 so share their settings. Deleting a key is not recursive, as vb6.exe's is not.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryVbSettingsStore(string root = RegistryVbSettingsStore.Vb6Root) : IVbSettingsStore
{
    public const string Vb6Root = @"Software\VB and VBA Program Settings";

    private string KeyName(IReadOnlyList<string> path) => string.Join('\\', path.Prepend(root));

    public IReadOnlyList<VbSettingValue>? Values(IReadOnlyList<string> path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyName(path));
        if (key is null)
            return null;
        return key.GetValueNames().Select(name => new VbSettingValue(name, key.GetValueKind(name) == RegistryValueKind.String
            ? key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string
            : null)).ToArray();
    }

    public IReadOnlyList<string>? SubKeys(IReadOnlyList<string> path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyName(path));
        return key?.GetSubKeyNames();
    }

    public void SetValue(IReadOnlyList<string> path, string name, string text)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyName(path), writable: true);
        key.SetValue(name, text, RegistryValueKind.String);
    }

    public bool DeleteValue(IReadOnlyList<string> path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyName(path), writable: true);
        if (key is null || !key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
            return false;
        key.DeleteValue(name);
        return true;
    }

    public bool DeleteKey(IReadOnlyList<string> path)
    {
        if (path.Count == 0)
            return false;
        using var parent = Registry.CurrentUser.OpenSubKey(KeyName(path.Take(path.Count - 1).ToArray()), writable: true);
        if (parent is null || !parent.GetSubKeyNames().Contains(path[^1], StringComparer.OrdinalIgnoreCase))
            return false;
        parent.DeleteSubKey(path[^1]);
        return true;
    }
}

/// <summary>The store a program run by HexIDE keeps its settings in (#1562).</summary>
public static class VbSettingsStores
{
    /// <summary>
    /// On Windows, the registry key vb6.exe itself uses. Elsewhere, a file in HexIDE's user-data directory, which
    /// <c>--user-data-dir</c> moves, so a demo or test run never touches the user's own settings.
    /// </summary>
    public static IVbSettingsStore ForThisUser() =>
        OperatingSystem.IsWindows() ? new RegistryVbSettingsStore() : new FileVbSettingsStore(UserDataPath.For("program-settings.json"));
}
