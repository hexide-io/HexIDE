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
    Task<string?> GetRtf();
    Task SetRtf(string text);
    Task<bool> HasOtherFormats();
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
                var readFormat = TextFormat(args, 0);
                if (readFormat == -16640)
                {
                    await RequireOwnFormats("GetText");
                    return new Vb6Value("");
                }
                return new Vb6Value(AnsiText((readFormat == -16639
                    ? await host.Clipboard.GetRtf() : await host.Clipboard.GetText()) ?? ""));
            case "settext":
                Count(args, 1, 2);
                var text = ObjectAsValue.Scalar(args[0], null);
                if (text.IsNull) throw new VBRunTimeException(VBStandardError.InvalidUseOfNull);
                if (text.Type.IsArray || text.Value is VbUdt)
                    throw new VBRunTimeException(VBStandardError.TypeMismatch);
                var converted = AnsiText(VbDisplay.ToText(text));
                var writeFormat = TextFormat(args, 1);
                if (writeFormat == -16640) throw new VBRunTimeException(VBStandardError.InvalidDdeLinkFormat);
                if (writeFormat == -16639) await host.Clipboard.SetRtf(converted);
                else await host.Clipboard.SetText(converted);
                return null;
            case "getformat":
                Count(args, 1, 1);
                var format = Format(args[0], false);
                if (format is 1 or 7 or 13 or 16)
                    return new Vb6Value(await host.Clipboard.GetText() is not null);
                if (format == -16639)
                    return new Vb6Value(await host.Clipboard.GetRtf() is not null);
                await RequireOwnFormats("GetFormat");
                return new Vb6Value(false);
            case "getdata":
            case "setdata":
                throw new NotImplementedException("Clipboard." + method + " is not implemented (#4682)");
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

    private static int TextFormat(List<Vb6Value> args, int index)
    {
        if (args.Count <= index || args[index].IsMissing) return 1;
        var format = Format(args[index], true);
        if (format is not (1 or -16639 or -16640))
            throw new VBRunTimeException(VBStandardError.InvalidClipboardFormat);
        return format;
    }

    private async Task RequireOwnFormats(string method)
    {
        if (await host.Clipboard.HasOtherFormats())
            throw new NotImplementedException("Clipboard." + method + " of a format another application put on the clipboard is unmeasured (#5075)");
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
