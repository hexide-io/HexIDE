using System.Globalization;
using Avalonia.Controls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// The CommonDialog control, <c>MSComDlg.CommonDialog</c> from COMDLG32.OCX (#1084): <c>ShowOpen</c>, <c>ShowSave</c> and
/// <c>ShowFont</c>, with the dialogs the host's own. It is invisible at run time, and lives only there: in the designer and
/// in the file it stays the block the form was read with, so it saves byte for byte.
/// </summary>
/// <remarks>
/// Measured on vb6.exe as p-code on Windows 10 (the oracle's "The CommonDialog control"):
/// <list type="bullet">
/// <item>The types: FileName, FileTitle, Filter, DialogTitle, DefaultExt, InitDir and FontName are Strings, Flags and Color
/// Longs, FilterIndex an Integer, CancelError a Boolean, FontSize a Single. FontSize starts at 8 and the rest empty or 0.</item>
/// <item>Cancel with CancelError True is 32755, "Cancel was selected.", and with it False no error. Either way every
/// property stays as it was: FileName keeps what the program put there, and FileTitle stays empty.</item>
/// <item>OK sets FileName to the full path and FileTitle to the file's name. A name typed with no extension in Save As is
/// given DefaultExt, even under an "All Files (*.*)" filter. FilterIndex is the filter it was chosen under.</item>
/// <item>Open with cdlOFNFileMustExist adds cdlOFNPathMustExist to Flags, as Windows does. Save As left Flags 0 alone.</item>
/// <item>ShowFont with no Flags shows the dialog, on Windows 10, rather than raise "No fonts exist". Its OK sets the Font
/// properties and leaves Flags alone; its Cancel leaves them as they were.</item>
/// </list>
/// Which filter the user chose in an Open dialog is something Avalonia's picker doesn't report. Where the program offered
/// more than one, FilterIndex is therefore refused as not implemented after such a dialog, rather than read as the one
/// the program set, which may not be the one chosen.
/// </remarks>
public sealed class VBCommonDialog : Control, ICSharpPropertyBag
{
    private static readonly VBStandardError Cancelled = new(32755, "Cancel was selected.");
    private const int OverwritePrompt = 0x2;       // cdlOFNOverwritePrompt
    private const int PathMustExist = 0x800;       // cdlOFNPathMustExist
    private const int FileMustExist = 0x1000;      // cdlOFNFileMustExist

    // Members VB6's CommonDialog has that this doesn't model: refused as not implemented, since 438 would say it has none.
    private static readonly HashSet<string> NotModelled = new(StringComparer.OrdinalIgnoreCase)
    {
        "Action", "Copies", "FromPage", "ToPage", "Min", "Max", "hDC", "HelpCommand", "HelpContext", "HelpFile", "HelpKey",
        "PrinterDefault", "Orientation", "FontUnderline", "FontStrikethru",
    };

    private readonly IBasicStandardLibrary host;
    private string fileName = "", fileTitle = "", filter = "", dialogTitle = "", defaultExt = "", initDir = "", fontName = "";
    private short filterIndex;
    private int flags, color;
    private bool cancelError, fontBold, fontItalic;
    private float fontSize = 8;
    private bool filterIndexUnknown;

    public VBCommonDialog(IBasicStandardLibrary host)
    {
        this.host = host;
        IsVisible = false;
    }

    /// <summary>Sets a property the form's file gives it, as code would set it.</summary>
    internal void SetDesignTime(string name, Vb6Value value)
    {
        if (!TrySetProperty(name, value))
            throw new NotImplementedException($"A CommonDialog's {name}, set in the form's file, is not implemented (#1084)");
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        Vb6Value? found = name.ToLowerInvariant() switch
        {
            "filename" => new Vb6Value(fileName),
            "filetitle" => new Vb6Value(fileTitle),
            "filter" => new Vb6Value(filter),
            "filterindex" => filterIndexUnknown
                ? throw new NotImplementedException(
                    "Reading a CommonDialog's FilterIndex after an Open dialog that offered more than one filter is not "
                    + "implemented: which one the user chose isn't reported to HexIDE (#1084)")
                : new Vb6Value((int)filterIndex),
            "flags" => new Vb6Value((long)flags),
            "cancelerror" => new Vb6Value(cancelError),
            "dialogtitle" => new Vb6Value(dialogTitle),
            "defaultext" => new Vb6Value(defaultExt),
            "initdir" => new Vb6Value(initDir),
            "fontname" => new Vb6Value(fontName),
            "fontsize" => new Vb6Value(fontSize),
            "fontbold" => new Vb6Value(fontBold),
            "fontitalic" => new Vb6Value(fontItalic),
            "color" => new Vb6Value((long)color),
            _ when NotModelled.Contains(name) => throw new NotImplementedException($"A CommonDialog's {name} is not implemented (#1084)"),
            _ => (Vb6Value?)null,
        };
        value = found ?? default;
        return found is not null;
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        switch (name.ToLowerInvariant())
        {
            case "filename": fileName = Text(value); return true;
            case "filter": filter = Text(value); return true;
            case "filterindex":
                filterIndex = (short)Number(value, Vb6Value.ValueType.Integer);
                filterIndexUnknown = false;
                return true;
            case "flags": flags = (int)Number(value, Vb6Value.ValueType.Long); return true;
            case "cancelerror": cancelError = (bool)VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Boolean, null).Value!; return true;
            case "dialogtitle": dialogTitle = Text(value); return true;
            case "defaultext": defaultExt = Text(value); return true;
            case "initdir": initDir = Text(value); return true;
            case "fontname": fontName = Text(value); return true;
            case "fontsize": fontSize = (float)Number(value, Vb6Value.ValueType.Single); return true;
            case "fontbold": fontBold = (bool)VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Boolean, null).Value!; return true;
            case "fontitalic": fontItalic = (bool)VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Boolean, null).Value!; return true;
            case "color": color = (int)Number(value, Vb6Value.ValueType.Long); return true;
            case "filetitle":
                throw new NotImplementedException("Setting a CommonDialog's FileTitle is not implemented (#1084)");
        }
        if (NotModelled.Contains(name))
            throw new NotImplementedException($"A CommonDialog's {name} is not implemented (#1084)");
        return false;
    }

    private static string Text(Vb6Value value) => (string)VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.String, null).Value!;

    private static double Number(Vb6Value value, Vb6Value.ValueType type) =>
        Convert.ToDouble(VbNumeric.CoerceOnStore(value, type, null).Value, CultureInfo.InvariantCulture);

    /// <summary>Whether <paramref name="method"/> is one of this control's own; any other is the control's as for any.</summary>
    public static bool Handles(string method) => method.ToLowerInvariant() is
        "showopen" or "showsave" or "showfont" or "showcolor" or "showprinter" or "showhelp" or "aboutbox";

    public async ValueTask<Vb6Value> CallAsync(string method, IReadOnlyList<Vb6Value> args)
    {
        if (args.Count > 0)
            throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
        switch (method.ToLowerInvariant())
        {
            case "showopen":
                await ShowFile(save: false);
                break;
            case "showsave":
                await ShowFile(save: true);
                break;
            case "showfont":
                await ShowFont();
                break;
            default:
                throw new NotImplementedException($"A CommonDialog's {method} is not implemented (#1084)");
        }
        return Vb6Value.Nothing;
    }

    private async Task ShowFile(bool save)
    {
        var filters = Filters(filter);
        var answer = await host.ShowFileDialog(new VbFileDialogRequest(
            save, dialogTitle.Length == 0 ? null : dialogTitle, filters, filterIndex, initDir, fileName, defaultExt,
            (flags & OverwritePrompt) != 0));
        if (answer is null)
        {
            if (cancelError)
                throw new VBRunTimeException(Cancelled);
            return;
        }

        var path = answer.Path;
        var title = TitleOf(path);
        if (save && defaultExt.Length > 0 && !title.Contains('.'))
        {
            path += "." + defaultExt;
            title += "." + defaultExt;
        }
        fileName = path;
        fileTitle = title;
        if (answer.FilterIndex is { } chosen)
        {
            filterIndex = (short)chosen;
            filterIndexUnknown = false;
        }
        else if (filters.Count > 1)
            filterIndexUnknown = true;
        if (!save && (flags & FileMustExist) != 0)
            flags |= PathMustExist;
    }

    private async Task ShowFont()
    {
        var chosen = await host.ShowFontDialog(new VbFontChoice(fontName, fontSize, fontBold, fontItalic));
        if (chosen is null)
        {
            if (cancelError)
                throw new VBRunTimeException(Cancelled);
            return;
        }
        fontName = chosen.Name;
        fontSize = chosen.Size;
        fontBold = chosen.Bold;
        fontItalic = chosen.Italic;
    }

    // The file's name, after the last separator of either kind: the path is the host's.
    private static string TitleOf(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    // "Text Documents (*.txt)|*.txt|All Files (*.*)|*.*" as its pairs. A description left without patterns is dropped.
    internal static IReadOnlyList<(string Description, string Patterns)> Filters(string filter)
    {
        var parts = filter.Split('|');
        var pairs = new List<(string, string)>();
        for (var i = 0; i + 1 < parts.Length; i += 2)
            pairs.Add((parts[i], parts[i + 1]));
        return pairs;
    }
}
