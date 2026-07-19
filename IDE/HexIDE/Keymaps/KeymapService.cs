using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia.Input;
using Avalonia.Platform;
using Serilog;

namespace HexIDE.Keymaps;

public sealed class KeymapService : IKeymapService
{
    private readonly Dictionary<string, IReadOnlyList<KeyGesture>> _defaults;

    public string ActiveKeymap { get; private set; } = "Default";

    public IReadOnlyList<string> AvailableKeymaps { get; } = ["Default", "VB6"];

    public KeymapService()
    {
        // Snapshot factory defaults before any Apply() call.
        // Must happen before the first Apply(), guaranteed by DI construction order.
        _defaults = new Dictionary<string, IReadOnlyList<KeyGesture>>(CommandKeyMapping.Table.Count);
        foreach (var (name, cmd) in CommandKeyMapping.Table)
            _defaults[name] = [..cmd.Gestures];
    }

    public void Apply(string keymapName)
    {
        ActiveKeymap = keymapName;

        // Step 1: Reset all commands to factory defaults.
        foreach (var (name, cmd) in CommandKeyMapping.Table)
        {
            cmd.Gestures.Clear();
            foreach (var g in _defaults[name])
                cmd.Gestures.Add(g);
        }

        if (keymapName == "Default")
        {
            Log.Debug("KeymapService: Applied Default keymap (factory gestures restored)");
            return;
        }

        try
        {
            var uri = new Uri($"avares://HexIDE/Keymaps/Packs/{keymapName}.json");
            using var stream = AssetLoader.Open(uri);
            var pack = JsonSerializer.Deserialize(stream, KeymapJsonContext.Default.KeymapPackRecord)
                       ?? throw new InvalidOperationException($"Keymap pack '{keymapName}' deserialized to null.");

            if (pack.Bindings is not null)
            {
                foreach (var (cmdName, gestureStr) in pack.Bindings)
                {
                    if (!CommandKeyMapping.Table.TryGetValue(cmdName, out var cmd))
                    {
                        Log.Warning("KeymapService: Unknown command '{Name}' in pack '{Pack}'", cmdName, keymapName);
                        continue;
                    }

                    cmd.Gestures.Clear();
                    if (!string.IsNullOrEmpty(gestureStr))
                        cmd.Gestures.Add(KeyGesture.Parse(gestureStr));
                }
            }

            // Step 2: Conflict scan — warn if any gesture is claimed by more than one command.
            var conflicts = CommandKeyMapping.Table
                .Where(kvp => kvp.Value.Gestures.Count > 0)
                .SelectMany(kvp => kvp.Value.Gestures.Select(g => (Gesture: g.ToString(), Command: kvp.Key)))
                .GroupBy(x => x.Gesture)
                .Where(g => g.Count() > 1);

            foreach (var conflict in conflicts)
                Log.Warning("KeymapService: Gesture '{Gesture}' is bound to multiple commands: {Commands}",
                    conflict.Key, string.Join(", ", conflict.Select(x => x.Command)));

            Log.Debug("KeymapService: Applied keymap '{Name}'", keymapName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "KeymapService: Failed to apply keymap '{Name}', reverting to Default", keymapName);
            ActiveKeymap = "Default";
            Apply("Default");
        }
    }
}
