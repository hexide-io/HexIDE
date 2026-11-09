using System.Collections.Generic;

namespace HexIDE.Keymaps;

public interface IKeymapService
{
    string ActiveKeymap { get; }
    bool HasProfileKeymap { get; }
    IReadOnlyList<string> AvailableKeymaps { get; }
    void Apply(string keymapName);
    IReadOnlyDictionary<string, string> LoadOverrides();
    IReadOnlyDictionary<string, IReadOnlyList<Avalonia.Input.KeyGesture>> GetPackBindings(string keymapName);
    void RefreshOverrides();
    void SaveOverrides(IReadOnlyDictionary<string, string> bindings);
}
