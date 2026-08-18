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

    /// <summary>
    /// The open <c>BeginProperty</c> blocks, innermost last. A stack rather than three scalar fields
    /// because these nest: an ImageList persists <c>BeginProperty Images</c> containing a
    /// <c>BeginProperty ListImage1</c> per image. With scalars, the inner <c>EndProperty</c> cleared the
    /// shared state and the outer one dereferenced null, so every form hosting a control that uses a
    /// property bag failed to load.
    /// </summary>
    private sealed record OpenBlock(string PropertyName, List<string> Lines, Dictionary<string, object> Fields);

    private readonly Stack<OpenBlock> _openBlocks = new();

    public string Code => _codeBuilder.ToString();

    /// <summary>
    /// Lines between the VERSION line and the root <c>Begin</c>, kept verbatim. Almost always OCX
    /// <c>Object =</c> declarations.
    /// </summary>
    public List<string> HeaderLines { get; } = new();

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
                // Anything before the root Begin is a file header — in practice the OCX declarations a
                // form needs: Object = "{831FDD16-...}#2.0#0"; "mscomctl.ocx". These contain '=', so
                // without this they fell through to the scalar-property branch and Peek()'d an empty
                // stack, throwing "Stack empty" and taking out every form hosting an ActiveX control.
                // Captured verbatim and re-emitted on save: HexIDE cannot host the control, but it must
                // not corrupt the declaration of one. The .vbp side already works this way.
                else if (componentStack.Count == 0 && rootComponent == null && !line.StartsWith("Begin"))
                {
                    HeaderLines.Add(rawLine);
                    continue;
                }
                else if (line.StartsWith("BeginProperty"))
                {
                    var spaceIdx = line.IndexOf(' ');
                    var property = spaceIdx >= 0 && spaceIdx < line.Length - 1
                        ? line[(spaceIdx + 1)..].Trim()
                        : "";
                    // Strip version GUID suffix if present (e.g. "Font {0BE35203-8F91-11CE-9DE3-00AA004BB851}")
                    var braceIdx = property.IndexOf('{');
                    if (braceIdx > 0)
                        property = property[..braceIdx].TrimEnd();

                    var block = new OpenBlock(property, new List<string> { rawLine }, new Dictionary<string, object>());

                    // Nest into the enclosing bag when there is one, so an inner block does not overwrite
                    // its parent's entry on the component.
                    if (_openBlocks.Count > 0)
                        _openBlocks.Peek().Fields[property] = block.Fields;
                    else
                        componentStack.Peek().Properties[property] = block.Fields;

                    _openBlocks.Push(block);
                }
                else if (line.StartsWith("EndProperty"))
                {
                    if (_openBlocks.Count == 0)
                        continue; // unbalanced EndProperty — malformed input, not worth throwing over

                    var block = _openBlocks.Pop();
                    block.Lines.Add(rawLine);

                    // Verbatim text belongs to the enclosing block if there is one, so the parent's
                    // round-trip capture includes its children.
                    if (_openBlocks.Count > 0)
                        _openBlocks.Peek().Lines.AddRange(block.Lines);
                    else
                        componentStack.Peek().OrderedRawProperties.Add((block.PropertyName, block.Lines));
                }
                else if (_openBlocks.Count > 0)
                {
                    // Inside a BeginProperty block — accumulate raw line and parse into the innermost bag
                    var current = _openBlocks.Peek();
                    current.Lines.Add(rawLine);
                    var parts = line.Split(['='], 2);
                    if (parts.Length == 2)
                    {
                        var k = parts[0].Trim();
                        var v = parts[1].Trim();
                        if (!string.IsNullOrEmpty(k) && !string.IsNullOrEmpty(v))
                            current.Fields[k] = ParseValue(v);
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
