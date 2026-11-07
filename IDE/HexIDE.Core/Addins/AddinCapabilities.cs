using System;
using System.Collections.Generic;
using System.Text.Json;

namespace HexIDE.Addins;

public static class AddinCapabilities
{
    public static bool IsKnown(string? capability) => capability is
        "project.read" or "project.write" or "program.run" or "ui.contribute" or "languageServer.contribute";

    public static bool IsValid(JsonElement? declaration)
    {
        if (declaration is not { ValueKind: JsonValueKind.Array } array)
            return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !IsKnown(item.GetString()) || !seen.Add(item.GetString()!))
                return false;
        }
        return true;
    }
}
