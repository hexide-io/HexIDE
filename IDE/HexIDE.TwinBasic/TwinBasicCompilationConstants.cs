using System.Text.Json;

namespace HexIDE.TwinBasic;

/// <summary>
/// twinBASIC's built-in compiler constants, measured on beta 1024 from the diagnostics the stock IDE's compiler reported for
/// a probe of <c>#If</c> conditions (#1691); the compiler was only launched and watched. The values are plain: a number, a
/// string, or null for one the project does not record, since this assembly does not know the interpreter's Variant.
/// <para>
/// Its <c>#If</c> differs from vb6.exe's in two ways the code window has to follow: a call to one of VB's functions, such as
/// <c>CBool</c>, <c>Len</c> or <c>VarType</c>, reads 0, so the documented <c>#If Not CBool(Win64)</c> holds at 64-bit too;
/// and a <c>#Const</c> lasts to the end of its file, not its module. Strings compare as text and <c>Like</c> works, as in
/// vb6.exe.
/// </para>
/// </summary>
public static class TwinBasicCompilationConstants
{
    /// <summary>
    /// The built-ins for one build. <c>Win16</c> and <c>MAC</c> are 0; <c>Win32</c>, <c>VBA6</c>, <c>VBA7</c> and
    /// <c>TWINBASIC</c> are 1 at either bitness, and <c>Win64</c> is 1 only at 64-bit. <c>TWINBASIC_BUILD</c> is the beta's
    /// number and <c>TWINBASIC_BUILD_TYPE</c> the project's build type, such as <c>"Standard EXE"</c>, <c>"ActiveX DLL"</c>,
    /// <c>"Standard DLL"</c> or <c>"ActiveX Control"</c>. Either is null when it isn't known, and a condition on it is then
    /// left undetermined rather than guessed.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> BuiltIns(bool win64, int? build, string? buildType) =>
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Win16"] = 0,
            ["Win32"] = 1,
            ["Win64"] = win64 ? 1 : 0,
            ["VBA6"] = 1,
            ["VBA7"] = 1,
            ["MAC"] = 0,
            ["TWINBASIC"] = 1,
            ["TWINBASIC_BUILD"] = build,
            ["TWINBASIC_BUILD_TYPE"] = buildType,
        };

    /// <summary>
    /// The build type a project's <c>Settings</c> name in <c>project.buildType</c>, or null when it names none, has no
    /// <c>Settings</c>, or they are not JSON.
    /// </summary>
    public static string? BuildTypeOf(TwinBasicContainer project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var settings = project.Root.Children.OfType<TwinBasicFile>().FirstOrDefault(f => f.Name == "Settings");
        if (settings is null) return null;
        try
        {
            using var json = JsonDocument.Parse(settings.Content.AsMemory(),
                new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 64 });
            return json.RootElement.ValueKind == JsonValueKind.Object
                   && json.RootElement.TryGetProperty("project.buildType", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException) { return null; }
    }
}
