using System;
using System.Collections.Generic;
using System.IO;
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

    private void WriteSimpleType(string property, Type type, object value, string? comment = null)
    {
        WriteIndent();
        var field = NameField(property);
        if (type == typeof(VBColor))
        {
            builder.WriteLine($"{field}=   {(VBColor)value}");
        }
        else
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.String:
                    builder.WriteLine($"{field}=   \"{value}\"");
                    break;
                case TypeCode.Int32:
                    if (type.IsEnum)
                        builder.WriteLine($"{field}=   {WithComment(((int)value).ToString(), comment, EnumValueWidth)}");
                    else
                        builder.WriteLine($"{field}=   {value}");
                    break;
                case TypeCode.Double:
                case TypeCode.Single:
                    builder.WriteLine($"{field}=   {value}");
                    break;
                case TypeCode.Boolean:
                    var boolVal = (bool)value ? -1 : 0;
                    builder.WriteLine(
                        $"{field}=   {WithComment(boolVal.ToString(), (bool)value ? "True" : "False", BooleanValueWidth)}");
                    break;
                default:
                    if (type.IsEnum)
                    {
                        builder.WriteLine($"{field}=   {WithComment(((int)value).ToString(), comment, EnumValueWidth)}");
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

        if (type == typeof(VBFont))
        {
            WriteIndent();
            builder.WriteLine($"BeginProperty {property} ");
            indentLevel++;
            var font = (VBFont)value;
            WriteSimpleType("Name", typeof(string), font.FontFamilyName);
            WriteSimpleType("Size", typeof(float), font.Size);
            WriteSimpleType("Charset", typeof(int), 2);
            WriteSimpleType("Weight", typeof(int), font.Bold ? 700 : 400);
            WriteSimpleType("Underline", typeof(bool), false);
            WriteSimpleType("Italic", typeof(bool), font.Italic);
            WriteSimpleType("Strikethrough", typeof(bool), false);
            indentLevel--;
            WriteIndent();
            builder.WriteLine($"EndProperty");
        }
        else
        {
            WriteSimpleType(property, type, value, comment);
        }
    }

    public void WriteRawProperty(string property, string rawValue)
    {
        WriteIndent();
        builder.WriteLine($"{NameField(property)}=   {rawValue}");
    }

    public void WriteVerbatimLine(string line)
    {
        builder.WriteLine(line);
    }

    public string GetOutput()
    {
        if (elementStack.Count > 0)
        {
            throw new InvalidOperationException("Not all elements have been closed.");
        }

        return builder.ToString();
    }

    private void WriteIndent()
    {
        builder.Write(new string(' ', indentLevel * indentSize));
    }
}
