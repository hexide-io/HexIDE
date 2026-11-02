using System.Collections.Generic;
using System.Globalization;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public sealed partial class VBListView
{
    // Keep the file reader separate from runtime setters: their conversions and defaults differ (#1085).
    internal void LoadProperties(ComponentInstance instance)
    {
        if (instance.GetPropertyOrDefault(ListViewComponentClass.FilePropertiesProperty) is not { } component)
            return;
        var fields = new Dictionary<string, object>(component.Properties, StringComparer.OrdinalIgnoreCase);
        static long Number(object value) => value switch
        {
            VBColor color => color.ToOle(),
            int number => number,
            _ => throw new NotImplementedException("ListView file numeric conversion is unmeasured (#1085)"),
        };
        static string Text(object? value) => value switch
        {
            null => "",
            string text => text,
            _ => throw new NotImplementedException("ListView file text conversion is unmeasured (#1085)"),
        };
        long ReadNumber(string name, long fallback) => fields.TryGetValue(name, out var value) ? Number(value) : fallback;
        bool ReadFlag(string name, bool fallback) => ReadNumber(name, fallback ? -1 : 0) switch
        {
            0 => false,
            -1 => true,
            _ => throw new NotImplementedException("ListView file Boolean conversion is unmeasured (#1085)"),
        };

        foreach (var name in new[] { "Icons", "SmallIcons", "ColHdrIcons", "Picture", "MouseIcon" })
            if (fields.ContainsKey(name))
                throw new NotImplementedException($"ListView file {name} is not implemented (#1085)");

        foreach (var name in fields.Keys)
        {
            if (ListViewComponentClass.Instance.Member(name) is not null
                && !properties.ContainsKey(name)
                && !name.Equals("Checkboxes", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("MultiSelect", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("Enabled", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("ColumnHeaders", StringComparison.OrdinalIgnoreCase))
                throw new NotImplementedException($"ListView file {name} is unmeasured (#1085)");
            if (name.Equals("DropHighlight", StringComparison.OrdinalIgnoreCase) || name.Equals("TabStop", StringComparison.OrdinalIgnoreCase))
                throw new NotImplementedException($"ListView file {name} is unmeasured (#1085)");
        }

        properties["BorderStyle"] = new(ReadNumber("BorderStyle", 0));
        properties["BackColor"] = new(ReadNumber("BackColor", unchecked((int)0x8000000f)));
        properties["ForeColor"] = new(ReadNumber("ForeColor", unchecked((int)0x80000012)));
        foreach (var (name, maximum) in new[] { ("View", 3), ("Arrange", 2), ("LabelEdit", 1), ("PictureAlignment", 5),
                     ("Appearance", 1), ("MousePointer", 2), ("OLEDragMode", 1), ("OLEDropMode", 1) })
        {
            var number = ReadNumber(name, (long)properties[name].Value!);
            if (number < 0 || number > maximum || name == "MousePointer" && number == 1)
                throw new NotImplementedException($"ListView file {name} value is unmeasured (#1085)");
            properties[name] = new(number);
        }
        if ((long)properties["Appearance"].Value! == 1 && (long)properties["BorderStyle"].Value! == 0)
            throw new NotImplementedException("ListView Appearance 1 with BorderStyle 0 is unmeasured (#1085)");
        foreach (var name in new[] { "SortOrder", "TextBackground" })
            properties[name] = new(ReadNumber(name, 0) switch
            {
                0 => 0L,
                -1 or 1 => 1L,
                _ => throw new NotImplementedException("ListView file enum conversion is unmeasured (#1085)"),
            });
        foreach (var name in new[] { "LabelWrap", "HideSelection", "HideColumnHeaders", "FlatScrollBar", "FullRowSelect", "GridLines", "HotTracking", "HoverSelection" })
            properties[name] = new(ReadFlag(name, (bool)properties[name].Value!));
        properties["AllowColumnReorder"] = new(ReadFlag("AllowReorder", false));
        Checkboxes = ReadFlag("Checkboxes", false);
        MultiSelect = ReadFlag("MultiSelect", false);
        IsEnabled = ReadFlag("Enabled", true);
        if (fields.TryGetValue("Font", out var font))
        {
            var fontFields = (Dictionary<string, object>)font;
            if (!fontFields.TryGetValue("Name", out var family) || family is not string
                || !fontFields.TryGetValue("Size", out var size) || size is not (int or double))
                throw new NotImplementedException("ListView partial file Font is unmeasured (#1085)");
            properties["Font"] = new(new ListViewFont((string)family, Convert.ToDouble(size, CultureInfo.InvariantCulture)));
        }

        for (var index = 1; fields.TryGetValue($"ColumnHeader({index})", out var block); index++)
        {
            var columnFields = (Dictionary<string, object>)block;
            object? Field(string name, string? alias = null) => columnFields.TryGetValue(name, out var value) ? value
                : alias is not null && columnFields.TryGetValue(alias, out value) ? value : null;
            if (Field("ImageIndex") is { } icon && Number(icon) != 0)
                throw new NotImplementedException("ListView file column ImageIndex awaits ImageList binding (#1085)");
            if (Number(Field("SubItemIndex") ?? 0) != index - 1)
                throw new NotImplementedException("ListView file nonsequential SubItemIndex is unmeasured (#1085)");
            var alignment = Number(Field("Alignment") ?? 0);
            var width = Number(Field("Object.Width", "Width") ?? 1440);
            if (alignment is < 0 or > 2 || width < 0)
                throw new NotImplementedException("ListView file column value is unmeasured (#1085)");
            var column = new ListViewColumn(Columns)
            {
                Key = Text(Field("Key")),
                Text = Text(Field("Text")),
                Alignment = (int)alignment,
            };
            column.LoadProperties((float)(width * 1440d / 2540),
                Text(Field("Object.Tag", "Tag")));
            Columns.Entries.Add(column);
            Columns.DisplayOrder.Add(column);
        }
        if (ReadNumber("NumItems", 0) != Columns.Entries.Count)
            throw new NotImplementedException("ListView file column count mismatch is unmeasured (#1085)");
        var sortKey = ReadNumber("SortKey", 0);
        if (sortKey < 0 || sortKey > Math.Max(0, Columns.Entries.Count - 1))
            throw new NotImplementedException("ListView file SortKey value is unmeasured (#1085)");
        properties["SortKey"] = new((int)sortKey);
        properties["Sorted"] = new(ReadFlag("Sorted", false));
        UpdateClientInset();
    }
}
