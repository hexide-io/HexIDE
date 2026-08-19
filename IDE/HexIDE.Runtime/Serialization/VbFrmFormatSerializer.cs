using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.Serialization;

public class VbFrmFormatSerializer
{
    private readonly StringWriter builder = new StringWriter();
    private readonly Stack<string> elementStack = new Stack<string>();
    private readonly int indentSize;
    private int indentLevel = 0;

    /// <summary>
    /// VB6 writes the property name in a sixteen-character field, then <c>=</c>, then three spaces —
    /// measured across every designer property line in the Template tree, without exception.
    ///
    /// A name of sixteen characters or more gets no padding and the <c>=</c> follows immediately:
    /// <c>EditAtDesignTime=   -1  'True</c> is the corpus's worked example, and that is exactly what
    /// PadRight already does when the string is long enough.
    ///
    /// The field is relative to the indent, not to the margin — a control's properties three levels in
    /// are padded to sixteen from their own indent.
    /// </summary>
    private const int NameFieldWidth = 16;

    private static string NameField(string property) => property.PadRight(NameFieldWidth);

    /// <summary>
    /// A boolean's value sits in a two-character field before its comment, because the pair it belongs to
    /// is <c>0</c> and <c>-1</c> and VB6 aligns both comments: <c>0   'False</c> against <c>-1  'True</c>,
    /// each four wide. An enum has no such pair and takes its own width — <c>3  'Pixel</c> is three
    /// characters, <c>99  'Custom</c> is four.
    /// </summary>
    private const int BooleanValueWidth = 2;

    private const int EnumValueWidth = 1;

    /// <summary>
    /// The value and the <c>'Name</c> comment VB6 writes beside an enum or a boolean, two spaces after the
    /// value has been padded to its field. A null comment writes the bare value, which is what an enum
    /// HexIDE has no VB6 name for gets — no comment rather than a wrong one.
    /// </summary>
    private static string WithComment(string value, string? comment, int valueWidth) =>
        comment is null ? value : $"{value.PadRight(valueWidth)}  '{comment}";

    /// <summary>
    /// The properties of the component currently being written, held back so they can go out in VB6's
    /// order rather than in the order the writer happens to produce them.
    ///
    /// VB6 sorts a component's properties by name — ClientHeight, ClientLeft, ClientTop, ClientWidth,
    /// LinkTopic, ScaleHeight, ScaleWidth, StartUpPosition. HexIDE's order was the order of three
    /// unrelated things: the component class's own property list, then LockControls, then the form
    /// measurements. That is how every line of a file could match VB6's and the file still differ on
    /// every line, which is exactly where the corpus sat.
    ///
    /// Null when no component's property block is open, in which case writes go straight out.
    /// </summary>
    private List<(string Name, string Text)>? pendingProperties;

    /// <summary>Where one property's text accumulates while it is being captured for the sort.</summary>
    private StringWriter? capture;

    private TextWriter Sink => capture ?? (TextWriter)builder;

    public VbFrmFormatSerializer(int indentSize = 3, bool includeVersionHeader = true)
    {
        builder.NewLine = "\r\n";
        this.indentSize = indentSize;
        if (includeVersionHeader)
        {
            builder.WriteLine("VERSION 5.00");
        }
    }

    public void WriteCode(string code)
    {
        builder.Write(code);
    }

    /// <summary>
    /// Opens a Begin block. Note the trailing space: VB6 puts one after the name on every <c>Begin</c> and
    /// <c>BeginProperty</c> line and on none of the <c>End</c>/<c>EndProperty</c> ones. It is not
    /// decoration — it is what VB6 writes, so omitting it differs from the original on the second line of
    /// every designer file and on every nested control after that.
    /// </summary>
    public void Begin(string type, string name)
    {
        WriteIndent();
        builder.WriteLine($"Begin {type} {name} ");
        elementStack.Push(name);
        indentLevel++;
    }

    public void End()
    {
        if (elementStack.Count > 0)
        {
            indentLevel--;
            WriteIndent();
            builder.WriteLine("End");
            elementStack.Pop();
        }
        else
        {
            throw new InvalidOperationException("No open elements to close.");
        }
    }

    /// <summary>
    /// Starts collecting this component's properties instead of writing them out. Everything written
    /// between here and <see cref="EndSortedProperties"/> is held with its name and emitted in name order.
    /// </summary>
    public void BeginSortedProperties()
    {
        if (pendingProperties is not null)
            throw new InvalidOperationException("A property block is already open — they do not nest.");
        pendingProperties = [];
    }

    /// <summary>
    /// Emits the collected properties in VB6's order. Ordinal, not culture-aware: the order has to come
    /// out the same on every machine that saves the file, and a culture-sensitive comparison does not.
    /// </summary>
    public void EndSortedProperties()
    {
        if (pendingProperties is null)
            throw new InvalidOperationException("No property block is open.");

        foreach (var (_, text) in pendingProperties.OrderBy(p => p.Name, StringComparer.Ordinal))
            builder.Write(text);

        pendingProperties = null;
    }

    /// <summary>Captures whatever <paramref name="write"/> emits, filed under <paramref name="name"/>.</summary>
    private void Collect(string name, Action write)
    {
        if (pendingProperties is null)
        {
            write();
            return;
        }

        capture = new StringWriter { NewLine = "\r\n" };
        try
        {
            write();
            pendingProperties.Add((name, capture.ToString()));
        }
        finally
        {
            capture = null;
        }
    }

    private void WriteSimpleType(string property, Type type, object value, string? comment = null)
    {
        // A Variant-typed property (Tag) carries whatever the file had. Re-dispatch on the value's runtime
        // type before anything is written, so a string comes back quoted and a number bare. Doing this
        // first rather than inside the switch keeps the indent from being written twice.
        if (type == typeof(object))
        {
            WriteSimpleType(property, value.GetType(), value, comment);
            return;
        }

        WriteIndent();
        var field = NameField(property);
        if (type == typeof(VBColor))
        {
            Sink.WriteLine($"{field}=   {(VBColor)value}");
        }
        else
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.String:
                    Sink.WriteLine($"{field}=   \"{value}\"");
                    break;
                case TypeCode.Int32:
                    if (type.IsEnum)
                        Sink.WriteLine($"{field}=   {WithComment(((int)value).ToString(), comment, EnumValueWidth)}");
                    else
                        Sink.WriteLine($"{field}=   {value}");
                    break;
                case TypeCode.Double:
                case TypeCode.Single:
                    Sink.WriteLine($"{field}=   {value}");
                    break;
                case TypeCode.Boolean:
                    var boolVal = (bool)value ? -1 : 0;
                    Sink.WriteLine(
                        $"{field}=   {WithComment(boolVal.ToString(), (bool)value ? "True" : "False", BooleanValueWidth)}");
                    break;
                default:
                    if (type.IsEnum)
                    {
                        Sink.WriteLine($"{field}=   {WithComment(((int)value).ToString(), comment, EnumValueWidth)}");
                    }
                    else
                    {
                        throw new Exception("Property type not supported: " + type);
                    }
                    break;
            }
        }
    }

    public void WriteProperty(string property, Type type, object? value, string? comment = null)
    {
        if (value == null)
            throw new NotImplementedException("I don't know if VB has a concept of null, please confirm");

        Collect(property, () =>
        {
            if (type == typeof(VBFont))
            {
                WriteIndent();
                Sink.WriteLine($"BeginProperty {property} ");
                indentLevel++;
                var font = (VBFont)value;
                // NOT alphabetical, and not an oversight: VB6 writes a Font block's members in this fixed
                // order, unchanged in every one of them across the corpus. Only the block itself takes its
                // place in the enclosing component's name order, under "Font".
                // Every value comes from the font now. Charset was a hardcoded 2, Underline and
                // Strikethrough hardcoded false, and Weight derived from a bool — so a save rewrote three
                // fields it had never read and rounded a fourth.
                WriteSimpleType("Name", typeof(string), font.FontFamilyName);
                WriteSimpleType("Size", typeof(double), font.Size);
                WriteSimpleType("Charset", typeof(int), font.Charset);
                WriteSimpleType("Weight", typeof(int), font.Weight);
                WriteSimpleType("Underline", typeof(bool), font.Underline);
                WriteSimpleType("Italic", typeof(bool), font.Italic);
                WriteSimpleType("Strikethrough", typeof(bool), font.Strikethrough);
                indentLevel--;
                WriteIndent();
                Sink.WriteLine($"EndProperty");
            }
            else
            {
                WriteSimpleType(property, type, value, comment);
            }
        });
    }

    public void WriteRawProperty(string property, string rawValue) =>
        Collect(property, () =>
        {
            WriteIndent();
            Sink.WriteLine($"{NameField(property)}=   {rawValue}");
        });

    /// <summary>
    /// A property HexIDE does not model, replayed exactly as it was read but taking its place in the name
    /// order like any other. The lines carry the indentation they were read with, so they go out as they are.
    /// </summary>
    public void WriteVerbatimProperty(string property, IReadOnlyList<string> lines) =>
        Collect(property, () =>
        {
            foreach (var line in lines)
                Sink.WriteLine(line);
        });

    public void WriteVerbatimLine(string line)
    {
        Sink.WriteLine(line);
    }

    public string GetOutput()
    {
        if (elementStack.Count > 0)
        {
            throw new InvalidOperationException("Not all elements have been closed.");
        }
        if (pendingProperties is not null)
        {
            throw new InvalidOperationException(
                "A component's property block was left open — its properties would be silently dropped.");
        }

        return builder.ToString();
    }

    private void WriteIndent()
    {
        Sink.Write(new string(' ', indentLevel * indentSize));
    }
}
