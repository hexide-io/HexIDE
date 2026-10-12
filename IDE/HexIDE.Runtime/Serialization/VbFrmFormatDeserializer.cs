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

    /// <summary>Where the code body begins in the input, or -1 if the root <c>End</c> was never reached.</summary>
    private int _codeStart = -1;

    /// <summary>
    /// The open <c>BeginProperty</c> blocks, innermost last. A stack rather than three scalar fields
    /// because these nest: an ImageList persists <c>BeginProperty Images</c> containing a
    /// <c>BeginProperty ListImage1</c> per image. With scalars, the inner <c>EndProperty</c> cleared the
    /// shared state and the outer one dereferenced null, so every form hosting a control that uses a
    /// property bag failed to load.
    /// </summary>
    private sealed record OpenBlock(string PropertyName, List<string> Lines, Dictionary<string, object> Fields);

    private readonly Stack<OpenBlock> _openBlocks = new();

    /// <summary>
    /// The code body: everything after the root <c>End</c>, <b>sliced from the input</b> rather than
    /// rebuilt.
    /// </summary>
    /// <remarks>
    /// <b>This used to be accumulated with <c>StringBuilder.AppendLine</c>, which re-terminated every line
    /// with <c>Environment.NewLine</c>.</b> On Windows that is indistinguishable from the CRLF the file
    /// already had; on Linux it silently rewrote a whole VB6-authored code body to LF, and the save path
    /// writes <c>Code</c> back byte-verbatim while the designer half is pinned to CRLF — so a form merely
    /// opened and saved on a Linux host came back with mixed terminators. `build-ide` runs on
    /// `ubuntu-latest`, and the round-trip corpus gate passes vacuously without
    /// <c>HEXIDE_ROUNDTRIP_CORPUS</c>, which is why it was never caught.
    ///
    /// <para>
    /// It also always ended in a newline, whether or not the file did, because every line was appended
    /// with one. Slicing preserves the file's own terminators and its own ending, which is what phase 3
    /// needs: the editor's buffer is about to become <see cref="DesignerText"/> + this, and that
    /// composition has to equal the file byte for byte or the invariant the whole phase rests on is false.
    /// </para>
    /// </remarks>
    public string Code { get; private set; } = "";

    /// <summary>
    /// The designer half, verbatim: the first line through the root <c>End</c> inclusive.
    /// </summary>
    /// <remarks>
    /// Kept as text as well as parsed into components, because the two answer different questions. The
    /// components are what the designer edits and what a save re-renders; this is what the file actually
    /// said, and it is what the code window puts in front of the developer until something changes it
    /// (hexide-io/HexIDE#273 task 3.1). The <c>VERSION</c> line and the block's own <c>Begin</c>/<c>End</c>
    /// are otherwise unrecoverable — the first is dropped at parse and regenerated from a literal, the
    /// second is rebuilt at a computed indent.
    /// </remarks>
    public string DesignerText { get; private set; } = "";

    /// <summary>
    /// Lines between the VERSION line and the root <c>Begin</c>, kept verbatim. Almost always OCX
    /// <c>Object =</c> declarations.
    /// </summary>
    public List<string> HeaderLines { get; } = new();

    public (VBSerializedComponent, string) Deserialize(string input)
    {
        {
            VBSerializedComponent? rootComponent = null;

            // Walked with offsets rather than a StringReader, so the boundary between the two halves is a
            // character position in the ORIGINAL text. Both halves are then slices of the input and every
            // terminator is the file's own. See the remarks on Code.
            foreach (var (rawLine, lineEnd) in LinesWithEnds(input))
            {
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

                    // Its fields in any case, as VB6 reads them: a Font block written `name = "Arial"` and
                    // `size = 12` loads as Arial 12 (measured, #819).
                    var block = new OpenBlock(property, new List<string> { rawLine },
                        new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase));

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
                    {
                        // The root End closes the designer half. Everything from here is the code body, so
                        // there is nothing left to scan — the two slices are taken below.
                        _codeStart = lineEnd;
                        break;
                    }
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

            // A file whose root End is never reached is all designer and no code, which is what the
            // old accumulator produced too (it never flipped, so nothing was appended).
            DesignerText = _codeStart >= 0 ? input[.._codeStart] : input;
            Code = _codeStart >= 0 ? input[_codeStart..] : "";

            return (rootComponent ?? throw new InvalidOperationException("No root component found in input."), Code);
        }
    }

    /// <summary>
    /// Each line of <paramref name="input"/> with the offset just past its terminator.
    /// </summary>
    /// <remarks>
    /// Follows <see cref="System.IO.TextReader.ReadLine"/>'s rules exactly, because it replaces a
    /// <c>StringReader</c> and any divergence would change which text is parsed: <c>\r\n</c> is one
    /// terminator, and a lone <c>\r</c> or a lone <c>\n</c> is one. A final line with no terminator is
    /// yielded, and its end is the end of the input.
    /// </remarks>
    private static IEnumerable<(string Line, int End)> LinesWithEnds(string input)
    {
        var i = 0;
        while (i < input.Length)
        {
            var start = i;
            while (i < input.Length && input[i] is not ('\r' or '\n')) i++;
            var textEnd = i;

            if (i < input.Length)
            {
                i += input[i] == '\r' && i + 1 < input.Length && input[i + 1] == '\n' ? 2 : 1;
            }

            yield return (input[start..textEnd], i);
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
