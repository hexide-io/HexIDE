using System;
using System.Collections.Generic;
using System.Linq;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// Where a program's <c>SaveSetting</c> and <c>GetSetting</c> keep their values (#1562): a tree of keys, as the registry's is
/// under <c>HKEY_CURRENT_USER\Software\VB and VBA Program Settings</c>, which is where vb6.exe keeps them (measured). A key's
/// path is its names from there down, the program's name first. Names compare without case and keep the case they were
/// made with. A store only keeps keys; VB6's rules for the four statements are <see cref="VB6BuiltIns"/>'s.
/// </summary>
public interface IVbSettingsStore
{
    /// <summary>The key's values, in the order the store keeps them, or null where there is no such key.</summary>
    IReadOnlyList<VbSettingValue>? Values(IReadOnlyList<string> path);

    /// <summary>The names of the key's subkeys, or null where there is no such key.</summary>
    IReadOnlyList<string>? SubKeys(IReadOnlyList<string> path);

    /// <summary>
    /// Sets a value as text, making the key, and any above it, where they are missing. A value already there, under its name
    /// in any case, keeps its place and its name.
    /// </summary>
    void SetValue(IReadOnlyList<string> path, string name, string text);

    /// <summary>Deletes a value: false where there is no such key or value.</summary>
    bool DeleteValue(IReadOnlyList<string> path, string name);

    /// <summary>Deletes a key that has no subkeys: false where there is no such key.</summary>
    bool DeleteKey(IReadOnlyList<string> path);
}

/// <summary>A setting's name and its text, or null text for a value that is not text, which another program can write.</summary>
public readonly record struct VbSettingValue(string Name, string? Text);

/// <summary>Settings kept in memory, for the tests, and as the tree <see cref="FileVbSettingsStore"/> reads and writes.</summary>
public class InMemoryVbSettingsStore : IVbSettingsStore
{
    internal sealed class Key(string name)
    {
        public string Name { get; } = name;
        public List<VbSettingValue> Values { get; } = new();
        public List<Key> Keys { get; } = new();

        public Key? Find(string name) => Keys.Find(k => Same(k.Name, name));
    }

    internal Key Root { get; } = new("");

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private Key? Find(IReadOnlyList<string> path)
    {
        var key = Root;
        foreach (var name in path)
        {
            if (key.Find(name) is not { } next)
                return null;
            key = next;
        }
        return key;
    }

    public IReadOnlyList<VbSettingValue>? Values(IReadOnlyList<string> path) => Find(path)?.Values.ToArray();

    public IReadOnlyList<string>? SubKeys(IReadOnlyList<string> path) => Find(path)?.Keys.Select(k => k.Name).ToArray();

    public void SetValue(IReadOnlyList<string> path, string name, string text) => Set(path, name, text);

    /// <summary>A value that is not text, as another program can write one, for a test of what GetSetting makes of it.</summary>
    internal void SetNonText(IReadOnlyList<string> path, string name) => Set(path, name, null);

    private void Set(IReadOnlyList<string> path, string name, string? text)
    {
        var key = Root;
        foreach (var part in path)
        {
            var next = key.Find(part);
            if (next is null)
                key.Keys.Add(next = new Key(part));
            key = next;
        }
        var index = key.Values.FindIndex(v => Same(v.Name, name));
        if (index >= 0)
            key.Values[index] = key.Values[index] with { Text = text };
        else
            key.Values.Add(new VbSettingValue(name, text));
    }

    public bool DeleteValue(IReadOnlyList<string> path, string name) =>
        Find(path) is { } key && key.Values.RemoveAll(v => Same(v.Name, name)) > 0;

    public bool DeleteKey(IReadOnlyList<string> path)
    {
        if (path.Count == 0 || Find(path.Take(path.Count - 1).ToArray()) is not { } parent || parent.Find(path[^1]) is not { } key)
            return false;
        if (key.Keys.Count > 0)
            throw new InvalidOperationException("A key with subkeys is not deleted");
        return parent.Keys.Remove(key);
    }
}
