using System.Text.Json;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The CommonDialog's constants, <c>cdlOFNFileMustExist</c>, <c>cdlCancel</c> and the other 100, from COMDLG32.OCX's own
/// type library (#1084): the embedded <c>Interpreter/Data/comdlg32-constants.json</c>, dumped by
/// <c>scripts/dump-typelib-constants.ps1</c> on the oracle VM.
/// </summary>
/// <remarks>
/// They resolve only in a project that references the control, as in VB6, where without the reference a bare
/// <c>cdlCancel</c> is an undeclared variable. They come after the four in-box libraries, as an <c>Object=</c> reference
/// comes after them in VB6's order. Every one is a Long, as the in-box constants are. Only the bare name resolves:
/// <c>MSComDlg.cdlCancel</c> and <c>FileOpenConstants.cdlOFNReadOnly</c> are not implemented, and are refused rather
/// than read as Empty.
/// </remarks>
internal static class CommonDialogConstants
{
    /// <summary>The control's class id, as an <c>Object=</c> line names it.</summary>
    internal const string Guid = "F9043C88-F6F2-101A-A3C9-08002B2F49FB";

    private static readonly Lazy<Dictionary<string, Vb6Value>> bare = new(Build, isThreadSafe: true);

    private static Dictionary<string, Vb6Value> Build()
    {
        var asm = typeof(CommonDialogConstants).Assembly;
        var name = asm.GetManifestResourceNames().Single(n => n.EndsWith("comdlg32-constants.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var doc = JsonDocument.Parse(stream);
        var members = new Dictionary<string, Vb6Value>(StringComparer.OrdinalIgnoreCase);
        foreach (var container in doc.RootElement.GetProperty("containers").EnumerateObject())
            foreach (var member in container.Value.GetProperty("members").EnumerateObject())
                members.TryAdd(member.Name, new Vb6Value(member.Value.GetInt64()));
        return members;
    }

    internal static bool TryBare(string name, out Vb6Value value) => bare.Value.TryGetValue(name, out value);

    /// <summary>Whether an <c>Object=</c> line names the CommonDialog's library.</summary>
    internal static bool IsReference(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("Object", StringComparison.OrdinalIgnoreCase)
               && trimmed.Contains(Guid, StringComparison.OrdinalIgnoreCase);
    }
}
