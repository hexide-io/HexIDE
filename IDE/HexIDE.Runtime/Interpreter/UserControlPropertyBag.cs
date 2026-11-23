using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.Interpreter;

// F5 reads the placed instance's saved text, not a standalone bag's binary stream (#4741, 2026-11-23).
internal sealed class UserControlPropertyBag(IReadOnlyList<ComponentInstance.UnknownRawProperty> properties)
    : ICSharpAsyncProxy, ICSharpPropertyBag
{
    private static readonly HashSet<string> ExtenderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Height", "Left", "Top", "Width", "TabIndex", "Index", "Tag", "Visible", "ToolTipText", "TabStop",
        "CausesValidation", "DragMode", "HelpContextID", "WhatsThisHelpID", "Name",
    };

    public void Call(string method, List<Vb6Value> args) => throw new InvalidOperationException("PropertyBag calls must be awaited");

    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        switch (method.ToLowerInvariant())
        {
            case "readproperty":
                Count(args, 1, 2);
                if (args[0].Type != Vb6Value.ValueType.String) throw Unmeasured();
                var name = (string)args[0].Value!;
                if (!ExtenderNames.Contains(name))
                    foreach (var property in properties)
                        if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            if (Read(property) is { } value) return Task.FromResult<Vb6Value?>(value);
                            break;
                        }
                if (args.Count == 2) return Task.FromResult<Vb6Value?>(args[1]);
                throw new VBRunTimeException(new VBStandardError(327, $"Data value named '{name}' not found"));
            case "writeproperty":
                Count(args, 2, 3);
                if (args[0].Type != Vb6Value.ValueType.String) throw Unmeasured();
                throw new VBRunTimeException(VBStandardError.InvalidObjectUse);
            case "contents":
                Count(args, 0, 0);
                throw new VBRunTimeException(VBStandardError.PermissionDenied);
            default:
                throw new VBCompileErrorException("Method or data member not found");
        }
    }

    private static void Count(List<Vb6Value> args, int min, int max)
    {
        if (args.Count < min || args.Count > max) throw Unmeasured();
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = default;
        if (name.Equals("Contents", StringComparison.OrdinalIgnoreCase))
            throw new VBRunTimeException(VBStandardError.PermissionDenied);
        throw new VBCompileErrorException("Method or data member not found");
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        if (name.Equals("Contents", StringComparison.OrdinalIgnoreCase))
            throw new VBRunTimeException(VBStandardError.PermissionDenied);
        throw new VBCompileErrorException("Method or data member not found");
    }

    private static Vb6Value? Read(ComponentInstance.UnknownRawProperty property)
    {
        if (property.Lines.Count == 0) return null;
        if (property.Lines[0].TrimStart().StartsWith("BeginProperty", StringComparison.OrdinalIgnoreCase))
            return ReadFont(property);
        var line = property.Lines[0];
        var equals = line.IndexOf('=');
        return equals < 0 ? null : Scalar(property.EditedValue ?? line[(equals + 1)..]);
    }

    private static Vb6Value ReadFont(ComponentInstance.UnknownRawProperty property)
    {
        if (!property.Name.Equals("Font", StringComparison.OrdinalIgnoreCase) || property.Lines.Count != 9
            || !property.Lines[0].Trim().Equals("BeginProperty Font {0BE35203-8F91-11CE-9DE3-00AA004BB851}", StringComparison.OrdinalIgnoreCase)
            || !property.Lines[^1].Trim().Equals("EndProperty", StringComparison.OrdinalIgnoreCase))
            throw Unmeasured();
        var fields = new Dictionary<string, Vb6Value>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in property.Lines.Skip(1).Take(7))
        {
            var equals = line.IndexOf('=');
            if (equals < 0 || Scalar(line[(equals + 1)..]) is not { } value
                || !fields.TryAdd(line[..equals].Trim(), value)) throw Unmeasured();
        }
        if (fields.Count != 7 || !fields.TryGetValue("Name", out var name) || name.Type != Vb6Value.ValueType.String
            || !fields.ContainsKey("Size") || !fields.ContainsKey("Charset") || !fields.ContainsKey("Weight")
            || !fields.ContainsKey("Underline") || !fields.ContainsKey("Italic") || !fields.ContainsKey("Strikethrough"))
            throw Unmeasured();
        PropertyBagFont stream;
        try
        {
            var size = Number("Size") * 10000;
            var weight = Number("Weight");
            var charset = Number("Charset");
            if (decimal.Truncate(size) != size || decimal.Truncate(weight) != weight || decimal.Truncate(charset) != charset)
                throw Unmeasured();
            stream = new PropertyBagFont((string)name.Value!, checked((int)size),
                checked((ushort)weight), checked((ushort)charset),
                (byte)((Number("Italic") != 0 ? 2 : 0) | (Number("Underline") != 0 ? 4 : 0)
                    | (Number("Strikethrough") != 0 ? 8 : 0)));
        }
        catch (Exception e) when (e is OverflowException or InvalidCastException) { throw Unmeasured(); }
        // The stream table knows only fonts measured through a standalone bag's bytes; a saved block's fields are
        // explicit, and building the Font from them is #5533.
        try { return new Vb6Value(ControlFont.FromPersistence(stream)); }
        catch (NotImplementedException)
        {
            throw new NotImplementedException("A UserControl's saved Font with these fields is not implemented (#5533)");
        }

        decimal Number(string key)
        {
            var value = fields[key];
            if (value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long
                && value.Type != Vb6Value.ValueType.Currency && value.Type != Vb6Value.ValueType.Decimal
                && value.Type != Vb6Value.ValueType.Double && value.Type != Vb6Value.ValueType.Single) throw Unmeasured();
            return Convert.ToDecimal(value.Value, CultureInfo.InvariantCulture);
        }
    }

    private static Vb6Value? Scalar(string text)
    {
        text = text.Trim();
        if (text.StartsWith('"'))
        {
            var result = new StringBuilder();
            for (var i = 1; i < text.Length; i++)
            {
                if (text[i] != '"') { result.Append(text[i]); continue; }
                if (i + 1 < text.Length && text[i + 1] == '"') { result.Append('"'); i++; continue; }
                if (text[(i + 1)..].TrimStart().StartsWith(':')) throw Unmeasured();
                return new Vb6Value(result.ToString());
            }
            return null;
        }
        text = text.Split('\'')[0].TrimEnd();
        if (Regex.IsMatch(text, @"^&[Hh][0-9A-Fa-f]+&?$|^&[Oo][0-7]+&?$"))
        {
            var digits = text[2..].TrimEnd('&');
            BigInteger number = 0;
            foreach (var digit in digits)
                number = number * (char.ToUpperInvariant(text[1]) == 'H' ? 16 : 8)
                    + (digit <= '9' ? digit - '0' : char.ToUpperInvariant(digit) - 'A' + 10);
            if (number > uint.MaxValue) return null;
            var signed = unchecked((int)(uint)number);
            return signed is >= short.MinValue and <= short.MaxValue ? new Vb6Value(signed) : new Vb6Value((long)signed);
        }
        if (!Regex.IsMatch(text, @"^-?[0-9]+(?:\.[0-9]*)?(?:[EeDd][+-]?[0-9]+)?$")) return null;
        var normalized = text.Replace('D', 'E').Replace('d', 'E');
        var real = text.IndexOfAny(['.', 'E', 'e', 'D', 'd']) >= 0;
        if (!real && int.TryParse(text, CultureInfo.InvariantCulture, out var integer))
            return integer is >= short.MinValue and <= short.MaxValue ? new Vb6Value(integer) : new Vb6Value((long)integer);
        var parts = normalized.Split(['E', 'e']);
        var point = parts[0].IndexOf('.');
        var coefficient = BigInteger.Parse(parts[0].Replace(".", ""), CultureInfo.InvariantCulture);
        if (!int.TryParse(parts.Length == 2 ? parts[1] : "0", CultureInfo.InvariantCulture, out var exponent)) throw Unmeasured();
        var scale = (point < 0 ? 0 : parts[0].Length - point - 1) - (long)exponent;
        while (scale > 0 && coefficient != 0 && coefficient % 10 == 0) { coefficient /= 10; scale--; }
        var fraction = coefficient != 0 && scale > 0;
        var zeroCurrency = coefficient == 0 && point >= 0 && point < parts[0].Length - 1;
        if (zeroCurrency) return Vb6Value.NewCurrency(0m);
        var floating = double.Parse(normalized, CultureInfo.InvariantCulture);
        if (!double.IsFinite(floating) || floating == 0 && coefficient != 0) throw Unmeasured();
        if (!fraction)
        {
            var magnitude = Math.Abs(floating);
            if (real && magnitude < 10000000) return new Vb6Value((float)floating);
            if (magnitude < 1000000000000000) return new Vb6Value(floating);
        }
        // Check the exact decimal before parsing: Decimal.TryParse rounds tiny or over-precise fractions.
        if (scale is >= -28 and <= 28)
        {
            var scaled = scale < 0 ? coefficient * BigInteger.Pow(10, (int)-scale) : coefficient;
            if (BigInteger.Abs(scaled) <= new BigInteger(decimal.MaxValue))
            {
                var number = (decimal)scaled;
                if (scale > 0) for (var i = 0; i < scale; i++) number /= 10m;
                if (fraction && scale <= 4 && number >= -922337203685477.5808m && number <= 922337203685477.5807m)
                    return Vb6Value.NewCurrency(number);
                return Vb6Value.NewDecimal(number);
            }
        }
        return new Vb6Value(floating);
    }

    private static NotImplementedException Unmeasured() => new("UserControl PropertyBag shape or conversion is unmeasured (#4741)");
}
