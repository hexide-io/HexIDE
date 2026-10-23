using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// Settings kept in one file, where there is no registry (#1562): the key tree <see cref="InMemoryVbSettingsStore"/> keeps,
/// read for each statement and written after each change, so two runs, or two programs, see each other's settings as
/// they would through the registry. One file for every program, rather than one each: a program's name can hold any
/// character, ".." included, so a name never becomes part of a path.
/// </summary>
public sealed class FileVbSettingsStore(string path) : IVbSettingsStore
{
    public string FilePath { get; } = path;

    public IReadOnlyList<VbSettingValue>? Values(IReadOnlyList<string> keyPath) => Load().Values(keyPath);

    public IReadOnlyList<string>? SubKeys(IReadOnlyList<string> keyPath) => Load().SubKeys(keyPath);

    public void SetValue(IReadOnlyList<string> keyPath, string name, string text)
    {
        var store = Load();
        store.SetValue(keyPath, name, text);
        Save(store);
    }

    public bool DeleteValue(IReadOnlyList<string> keyPath, string name)
    {
        var store = Load();
        if (!store.DeleteValue(keyPath, name))
            return false;
        Save(store);
        return true;
    }

    public bool DeleteKey(IReadOnlyList<string> keyPath)
    {
        var store = Load();
        if (!store.DeleteKey(keyPath))
            return false;
        Save(store);
        return true;
    }

    private InMemoryVbSettingsStore Load()
    {
        var store = new InMemoryVbSettingsStore();
        if (!File.Exists(FilePath))
            return store;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject root)
                Read(root, store.Root);
            return store;
        }
        catch (JsonException e)
        {
            throw new IOException($"The program settings in {FilePath} could not be read: {e.Message}", e);
        }
    }

    private static void Read(JsonObject node, InMemoryVbSettingsStore.Key key)
    {
        if (node["values"] is JsonArray values)
            foreach (var value in values)
                if (value is JsonObject v && v["name"]?.GetValue<string>() is { } name)
                    key.Values.Add(new VbSettingValue(name, v["text"]?.GetValue<string>()));
        if (node["keys"] is JsonArray keys)
            foreach (var sub in keys)
                if (sub is JsonObject k && k["name"]?.GetValue<string>() is { } name)
                {
                    var child = new InMemoryVbSettingsStore.Key(name);
                    Read(k, child);
                    key.Keys.Add(child);
                }
    }

    private static JsonObject Write(InMemoryVbSettingsStore.Key key)
    {
        var values = new JsonArray();
        foreach (var value in key.Values)
            values.Add(new JsonObject { ["name"] = value.Name, ["text"] = value.Text });
        var keys = new JsonArray();
        foreach (var sub in key.Keys)
        {
            var node = Write(sub);
            node.Insert(0, "name", sub.Name);
            keys.Add(node);
        }
        return new JsonObject { ["values"] = values, ["keys"] = keys };
    }

    // Written beside the file and moved over it, so a program stopped mid-write leaves the settings it had, not half a file.
    private void Save(InMemoryVbSettingsStore store)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, Write(store.Root).ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
