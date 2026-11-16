using System;
using System.Globalization;
using System.Text.Json;

namespace HexIDE.Runtime.Interpreter;

// Default NLS data measured by the generator on the Windows build recorded in the table (#3928).
internal static class WindowsDateTable
{
    private static readonly Lazy<JsonElement> Table = new(() =>
    {
        using var stream = typeof(WindowsDateTable).Assembly.GetManifestResourceStream(
            "HexIDE.Runtime.Interpreter.Data.windows-date-locales.json")
            ?? throw new InvalidOperationException("Missing Windows date locale table.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    });
    private static readonly WindowsDateLocale.CalendarCache Calendars = new((name, _) =>
        TryGet(name, out var entry) ? entry.GetProperty("calendar").GetString()! : "");

    internal static JsonElement Data => Table.Value;

    private static bool TryGet(string name, out JsonElement entry) =>
        Data.GetProperty("locales").TryGetProperty(name, out entry);

    internal static bool AppliesTo(CultureInfo culture) => Calendars.AppliesTo(culture);

    internal static void Apply(CultureInfo culture)
    {
        if (TryGet(culture.Name, out var entry))
            WindowsDateLocale.Apply(culture.DateTimeFormat, type => Read(entry, type),
                () => entry.GetProperty("era").GetString()!);
    }

    internal static string Read(JsonElement entry, uint type)
    {
        if (type is 0x1d or 0x1f or 0x20)
            return entry.GetProperty(type switch
            {
                0x1d => "dateSeparator", 0x1f => "shortDate", _ => "longDate"
            }).GetString()!;
        var (first, field, fallback) = type switch
        {
            >= 0x10000044 and < 0x10000050 => (0x10000044u, "abbreviatedGenitiveMonths", "abbreviatedMonths"),
            >= 0x10000038 and < 0x10000044 => (0x10000038u, "genitiveMonths", "months"),
            >= 0x44 and < 0x50 => (0x44u, "abbreviatedMonths", "abbreviatedMonths"),
            >= 0x38 and < 0x44 => (0x38u, "months", "months"),
            >= 0x31 and < 0x38 => (0x31u, "abbreviatedDays", "abbreviatedDays"),
            >= 0x2a and < 0x31 => (0x2au, "days", "days"),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        var names = entry.TryGetProperty(field, out var value) ? value : entry.GetProperty(fallback);
        return names[(int)(type - first)].GetString()!;
    }
}
