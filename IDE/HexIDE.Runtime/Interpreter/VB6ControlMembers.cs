using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia.Controls;

namespace HexIDE.Runtime.Interpreter;

// #1433, oracle PR #5193: the corpus is the measured inventory, including hidden members.
internal static class VB6ControlMembers
{
    private static readonly Lazy<Dictionary<string, HashSet<string>>> members = new(Load);

    internal static void RefuseUnresolved(Vb6Value receiver, string name)
    {
        if (!(receiver.HeldAsVariant
              || string.Equals(receiver.DeclaredAs, "Object", StringComparison.OrdinalIgnoreCase)
              || string.Equals(receiver.DeclaredAs, "Control", StringComparison.OrdinalIgnoreCase))
            || receiver.Value is not Control control)
            return;
        var className = VB6BuiltIns.ControlTypeName(control);
        if (!members.Value.TryGetValue(className, out var names)) return;
        if (!names.Contains(name))
            throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        throw new NotImplementedException($"{className}.{name} is not implemented (#1433)");
    }

    private static Dictionary<string, HashSet<string>> Load()
    {
        using var stream = typeof(VB6ControlMembers).Assembly.GetManifestResourceStream("HexIDE.Runtime.VB6-members.jsonl")
                           ?? throw new InvalidOperationException("Missing VB6 control member inventory");
        using var reader = new StreamReader(stream);
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            using var row = JsonDocument.Parse(line);
            var entry = row.RootElement;
            if (entry.GetProperty("kind").GetString() != "MEMBER") continue;
            var className = entry.GetProperty("class").GetString()!;
            if (!result.TryGetValue(className, out var names))
                result[className] = names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            names.Add(entry.GetProperty("name").GetString()!);
        }
        return result;
    }
}
