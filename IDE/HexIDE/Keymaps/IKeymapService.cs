using System.Collections.Generic;

namespace HexIDE.Keymaps;

public interface IKeymapService
{
    string ActiveKeymap { get; }
    IReadOnlyList<string> AvailableKeymaps { get; }
    void Apply(string keymapName);
}
