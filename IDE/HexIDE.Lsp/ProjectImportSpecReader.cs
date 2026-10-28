using System.Text.Json;

namespace HexIDE.Lsp;

public static class ProjectImportSpecReader
{
    public static bool TryRead(string entryId, ProjectImportEntry? declared,
        out ProjectImportSpec? spec, out IReadOnlyList<LanguageServerConfigProblem> problems)
    {
        spec = null;
        var found = new List<LanguageServerConfigProblem>();
        problems = found;
        if (declared is null) return false;

        if (declared.Unrecognized is { Count: > 0 })
            found.Add(new(entryId,
                $"Unrecognised fields in projectImport: {string.Join(", ", declared.Unrecognized.Keys.Order(StringComparer.Ordinal))}.",
                false, LanguageServerConfigProblemKind.UnrecognisedField));

        if (string.IsNullOrWhiteSpace(declared.Command))
            found.Add(new(entryId, "projectImport requires a non-empty command. No import will be sent.", false));
        if (declared.Arguments is not { ValueKind: JsonValueKind.Array })
            found.Add(new(entryId, "projectImport arguments must be a JSON array. No import will be sent.", false));
        if (found.Any(p => p.Kind == LanguageServerConfigProblemKind.Configuration)) return false;

        spec = new(declared.Command!.Trim(), declared.Arguments!.Value.Clone());
        return true;
    }
}
