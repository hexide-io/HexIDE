using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.Serialization;

public class VbFrmFormatDeserializer
{
    private readonly Stack<VBSerializedComponent> componentStack = new Stack<VBSerializedComponent>();
    private readonly StringBuilder _codeBuilder = new StringBuilder();
    private bool parsingCode = false;

    // Tracks the BeginProperty block currently being accumulated
    private string? _currentBlockPropertyName;
    private List<string>? _currentBlockLines;
    private Dictionary<string, object>? nestedField;

    public string Code => _codeBuilder.ToString();

    public (VBSerializedComponent, string) Deserialize(string input)
    {
        using (var reader = new StringReader(input))
        {
            string? rawLine;
            VBSerializedComponent? rootComponent = null;

            while ((rawLine = reader.ReadLine()) != null)
            {
                if (parsingCode)
                {
                    _codeBuilder.AppendLine(rawLine);
                    continue;
                }

                var line = rawLine.Trim();

                if (line.StartsWith("VERSION"))
                {
                    continue;
                }
                else if (line.StartsWith("BeginProperty"))
                {
                    nestedField = new();
                    var spaceIdx = line.IndexOf(' ');
                    var property = spaceIdx >= 0 && spaceIdx < line.Length - 1
                        ? line[(spaceIdx + 1)..].Trim()
                        : "";
                    // Strip version GUID suffix if present (e.g. "Font {0BE35203-8F91-11CE-9DE3-00AA004BB851}")
                    var braceIdx = property.IndexOf('{');
                    if (braceIdx > 0)
                        property = property[..braceIdx].TrimEnd();
                    _currentBlockPropertyName = property;
                    _currentBlockLines = new List<string> { rawLine };
                    componentStack.Peek().Properties[property] = nestedField;
                }
                else if (line.StartsWith("EndProperty"))
                {
                    _currentBlockLines!.Add(rawLine);
                    componentStack.Peek().OrderedRawProperties.Add((_currentBlockPropertyName!, _currentBlockLines));
                    nestedField = null;
                    _currentBlockPropertyName = null;
                    _currentBlockLines = null;
                }
                else if (nestedField != null)
                {
                    // Inside a BeginProperty block — accumulate raw line and parse into the nested dict
                    _currentBlockLines!.Add(rawLine);
                    var parts = line.Split(['='], 2);
                    if (parts.Length == 2)
                    {
                        var k = parts[0].Trim();
                        var v = parts[1].Trim();
                        if (!string.IsNullOrEmpty(k) && !string.IsNullOrEmpty(v))
                            nestedField[k] = ParseValue(v);
                    }
                }
                else if (line.StartsWith("Begin"))
                {
                    var component = ParseBegin(line);
                    if (componentStack.Count == 0)
                        rootComponent = component;
                    else
                        componentStack.Peek().SubComponents.Add(component);
                    componentStack.Push(component);
                }
                else if (line.StartsWith("End"))
                {
                    componentStack.Pop();
                    if (componentStack.Count == 0)
                        parsingCode = true;
                }
                else
                {
                    // Scalar property — record raw line before parsing
                    var parts = line.Split(['='], 2);
                    if (parts.Length == 2)
                    {
                        var propName = parts[0].Trim();
                        if (!string.IsNullOrEmpty(propName))
                            componentStack.Peek().OrderedRawProperties.Add((propName, new List<string> { rawLine }));
                    }
                    ParseProperty(line, componentStack.Peek());
                }
            }

            return (rootComponent ?? throw new InvalidOperationException("No root component found in input."), Code);
        }
    }

    private VBSerializedComponent ParseBegin(string line)
    {
        var tokens = line.Split(' ', 3);
        if (tokens.Length < 3)
            throw new FormatException("Invalid Begin line format.");

        return new VBSerializedComponent
        {
            Type = tokens[1],
            Name = tokens[2]
        };
    }

    private void ParseProperty(string line, VBSerializedComponent serializedComponent)
    {
        var parts = line.Split(['='], 2);
        if (parts.Length != 2)
            return; // Skip lines that aren't property assignments (e.g. empty, malformed)

        var propertyName = parts[0].Trim();
        var valueText = parts[1].Trim();
        if (string.IsNullOrEmpty(propertyName) || string.IsNullOrEmpty(valueText))
            return;

        serializedComponent.Properties[propertyName] = ParseValue(valueText);
    }

    private object ParseValue(string valueText)
    {
        // Strip VB6 inline comments (e.g. "0  'Flat") — but not inside strings.
        if (!valueText.StartsWith("\""))
        {
            var commentIdx = valueText.IndexOf('\'');
            if (commentIdx >= 0)
                valueText = valueText[..commentIdx].TrimEnd();
        }

        if (VBColor.TryParse(valueText, out var vbColor))
        {
            return vbColor;
        }
        else if (valueText.StartsWith("\"") && valueText.EndsWith("\""))
        {
            return valueText.Substring(1, valueText.Length - 2);
        }
        else if (valueText.Equals("True", StringComparison.OrdinalIgnoreCase))
        {
            return -1; // VB6 True = -1
        }
        else if (valueText.Equals("False", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        else
        {
            // Strip VB6 Long type suffix (e.g. "12345&")
            var numText = valueText.EndsWith("&") ? valueText[..^1] : valueText;

            if (int.TryParse(numText, out var intValue))
                return intValue;
            if (double.TryParse(numText, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleValue))
                return doubleValue;
        }

        // Return raw string for anything we don't recognize (named constants, etc.)
        // rather than crashing the entire form load.
        return valueText;
    }
}
