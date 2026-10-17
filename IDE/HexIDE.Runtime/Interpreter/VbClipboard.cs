using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using HexIDE.IDE;

namespace HexIDE.Runtime.Interpreter;

/// <summary>The host's text clipboard. Null means no text format; an empty string is present text.</summary>
public interface ITextClipboard
{
    Task Clear();
    Task<string?> GetText();
    Task SetText(string text);
}

/// <summary>VB6's text Clipboard surface, measured native and p-code (#1156).</summary>
public sealed class VbClipboard(IBasicStandardLibrary host) : ICSharpAsyncProxy
{
    void ICSharpProxy.Call(string method, List<Vb6Value> args) =>
        throw new InvalidOperationException("Clipboard calls must be awaited");

    public async Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        switch (method.ToLowerInvariant())
        {
            case "clear":
                Count(args, 0, 0);
                await host.Clipboard.Clear();
                return null;
            case "gettext":
                Count(args, 0, 1);
                TextFormat(args, 0);
                return new Vb6Value(AnsiText(await host.Clipboard.GetText() ?? ""));
            case "settext":
                Count(args, 1, 2);
                var text = ObjectAsValue.Scalar(args[0], null);
                if (text.IsNull) throw new VBRunTimeException(VBStandardError.InvalidUseOfNull);
                if (text.Type.IsArray || text.Value is VbUdt)
                    throw new VBRunTimeException(VBStandardError.TypeMismatch);
                var converted = AnsiText(VbDisplay.ToText(text));
                TextFormat(args, 1);
                await host.Clipboard.SetText(converted);
                return null;
            case "getformat":
                Count(args, 1, 1);
                var format = Format(args[0], false);
                // We cannot report False for an image or RTF which may actually be on the system clipboard.
                if (format != 1)
                    throw new NotImplementedException("Clipboard.GetFormat for non-text formats is not implemented (#1156)");
                return new Vb6Value(await host.Clipboard.GetText() is not null);
            case "getdata":
            case "setdata":
                throw new NotImplementedException("Clipboard." + method + " is not implemented (#1156)");
            default:
                throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod, "Clipboard." + method);
        }
    }

    private static void Count(List<Vb6Value> args, int min, int max)
    {
        if (args.Count < min || args.Count > max)
            throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
    }

    private static int Format(Vb6Value value, bool optional)
    {
        value = ObjectAsValue.Scalar(value, null);
        if (optional && value.IsNull)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        return (int)VB6BuiltIns.NarrowNum(value, Vb6Value.ValueType.Integer).Value!;
    }

    private static void TextFormat(List<Vb6Value> args, int index)
    {
        if (args.Count <= index || args[index].IsMissing) return;
        var format = Format(args[index], true);
        if (format is -16639 or -16640)
            throw new NotImplementedException("Clipboard RTF and Link text formats are not implemented (#1156)");
        if (format != 1) throw new VBRunTimeException(VBStandardError.InvalidClipboardFormat);
    }

    private static string AnsiText(string text)
    {
        if (text.IndexOf('\0') is var nul && nul >= 0) text = text[..nul];
        // The default code-page encoder uses Windows best-fit mappings (e.g. U+0100 -> A), measured in VB6.
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(Vb6TextEncoding.Ansi.CodePage)
            ?? throw new NotImplementedException("The clipboard ANSI code page is unavailable");
        return encoding.GetString(encoding.GetBytes(text));
    }
}
