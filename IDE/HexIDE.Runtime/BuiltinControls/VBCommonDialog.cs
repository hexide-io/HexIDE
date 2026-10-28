using System.Globalization;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// The CommonDialog control, <c>MSComDlg.CommonDialog</c> from COMDLG32.OCX (#1084): <c>ShowOpen</c>, <c>ShowSave</c> and
/// <c>ShowFont</c>, plus the measured ShowColor cases (#2299), with the dialogs the host's own. It is invisible at run
/// time; its component carries the own file properties verbatim.
/// </summary>
/// <remarks>
/// Measured on vb6.exe as p-code on Windows 10 (the oracle's "The CommonDialog control"):
/// <list type="bullet">
/// <item>The types: FileName, FileTitle, Filter, DialogTitle, DefaultExt, InitDir and FontName are Strings, Flags and Color
/// Longs, FilterIndex an Integer, CancelError a Boolean, FontSize a Single. FontSize starts at 8 and the rest empty or 0.</item>
/// <item>Cancel with CancelError True is 32755, "Cancel was selected.", and with it False no error. Either way every
/// file property stays as it was: FileName keeps what the program put there, and FileTitle keeps the last pick.</item>
/// <item>OK sets FileName to the full path and FileTitle to the file's name. A name typed with no extension in Save As is
/// given DefaultExt, even under an "All Files (*.*)" filter. FilterIndex is the filter it was chosen under.</item>
/// <item>Open with cdlOFNFileMustExist adds cdlOFNPathMustExist even on Cancel. A picked extension differing from
/// DefaultExt sets cdlOFNExtensionDifferent, including when DefaultExt is empty.</item>
/// <item>ShowFont with no Flags shows the dialog, on Windows 10, rather than raise "No fonts exist". Its OK sets the Font
/// properties and leaves Flags alone; its Cancel leaves them as they were.</item>
/// </list>
/// Which filter the user chose in an Open dialog is something Avalonia's picker doesn't report. Where the program offered
/// more than one, FilterIndex is therefore refused as not implemented after such a dialog, rather than read as the one
/// the program set, which may not be the one chosen.
/// </remarks>
public sealed class VBCommonDialog : Control, ICSharpPropertyBag, ICSharpAsyncPropertyBag
{
    private static readonly VBStandardError Cancelled = new(32755, "Cancel was selected.");
    private const int AllowMultiselect = 0x200;   // cdlOFNAllowMultiselect
    private const int Explorer = 0x80000;        // cdlOFNExplorer
    private const int OverwritePrompt = 0x2;       // cdlOFNOverwritePrompt
    private const int PathMustExist = 0x800;       // cdlOFNPathMustExist
    private const int FileMustExist = 0x1000;      // cdlOFNFileMustExist
    private const int ExtensionDifferent = 0x400; // cdlOFNExtensionDifferent

    // Members VB6's CommonDialog has that this doesn't model: refused as not implemented, since 438 would say it has none.
    private static readonly HashSet<string> NotModelled = new(StringComparer.OrdinalIgnoreCase)
    {
        "Action", "hDC", "HelpContext",
    };

    internal IBasicStandardLibrary host;
    private string fileName = "", fileTitle = "", filter = "", dialogTitle = "", defaultExt = "", initDir = "", fontName = "";
    private short filterIndex;
    private short maxFileSize = 260, copies = 1, fromPage, toPage, min, max, orientation = 1, helpCommand;
    private string helpFile = "", helpKey = "";
    private bool printerDefault = true, fontUnderline, fontStrikethru;
    private int flags, color;
    private bool cancelError, fontBold, fontItalic;
    private float fontSize = 8;
    private bool filterIndexUnknown, printAccepted;

    public VBCommonDialog(IBasicStandardLibrary host)
    {
        this.host = host;
        IsVisible = false;
    }

    /// <summary>Sets a property the form's file gives it, as code would set it.</summary>
    internal void SetDesignTime(string name, Vb6Value value)
    {
        // Loading an unavailable face from a file is unmeasured; keep the previous loader behavior.
        if (name.Equals("FontName", StringComparison.OrdinalIgnoreCase))
        {
            fontName = Text(value);
            return;
        }
        if (!TrySetProperty(name, value))
            throw new NotImplementedException($"A CommonDialog's {name}, set in the form's file, is not implemented (#1084)");
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        Vb6Value? found = name.ToLowerInvariant() switch
        {
            "hdc" when printAccepted => new Vb6Value(0L),
            "action" => throw new VBRunTimeException(new VBStandardError(394, "Get not supported (write-only property)")),
            "left" or "top" => throw new VBRunTimeException(new VBStandardError(393, "Get not supported at runtime")),
            "visible" or "width" or "height" or "enabled" or "hwnd" =>
                throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod),
            "maxfilesize" => new Vb6Value((int)maxFileSize),
            "copies" => new Vb6Value((int)copies),
            "frompage" => new Vb6Value((int)fromPage),
            "topage" => new Vb6Value((int)toPage),
            "min" => new Vb6Value((int)min),
            "max" => new Vb6Value((int)max),
            "orientation" => new Vb6Value((int)orientation),
            "helpcommand" => new Vb6Value((int)helpCommand),
            "helpfile" => new Vb6Value(helpFile),
            "helpkey" => new Vb6Value(helpKey),
            "printerdefault" => new Vb6Value(printerDefault),
            "fontunderline" => new Vb6Value(fontUnderline),
            "fontstrikethru" => new Vb6Value(fontStrikethru),
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
            "color" => Vb6Value.NewAutomationUnsignedLong(unchecked((uint)color)),
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
            case "left":
            case "top":
                throw new VBRunTimeException(new VBStandardError(382, "Set not supported at runtime"));
            case "maxfilesize": maxFileSize = Integer(value, n => n > 0); return true;
            case "copies": copies = Integer(value, n => n > 0); return true;
            case "frompage": fromPage = Integer(value, n => n >= 0); return true;
            case "topage": toPage = Integer(value); return true;
            case "min": min = Integer(value); return true;
            case "max": max = Integer(value); return true;
            case "orientation": orientation = Integer(value, n => n is 1 or 2); return true;
            case "helpcommand": helpCommand = Integer(value); return true;
            case "helpfile": helpFile = Text(value); return true;
            case "helpkey": helpKey = Text(value); return true;
            case "printerdefault": printerDefault = Boolean(value); return true;
            case "fontunderline": fontUnderline = Boolean(value); return true;
            case "fontstrikethru": fontStrikethru = Boolean(value); return true;
            case "filename": fileName = Text(value); return true;
            case "filter": filter = Text(value); return true;
            case "filterindex":
                filterIndex = Integer(value, n => n >= 0);
                filterIndexUnknown = false;
                return true;
            case "flags": flags = (int)Number(value, Vb6Value.ValueType.Long); return true;
            case "cancelerror": cancelError = (bool)VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Boolean, null).Value!; return true;
            case "dialogtitle": dialogTitle = Text(value); return true;
            case "defaultext": defaultExt = Text(value); return true;
            case "initdir": initDir = Text(value); return true;
            case "fontname":
                var asked = Text(value);
                if (asked.Length == 0 || VBFontExtensions.IsNameable(asked))
                    fontName = asked;
                return true;
            case "fontsize": fontSize = (float)Number(value, Vb6Value.ValueType.Single); return true;
            case "fontbold": fontBold = (bool)VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Boolean, null).Value!; return true;
            case "fontitalic": fontItalic = (bool)VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Boolean, null).Value!; return true;
            case "color":
                var assigned = (int)Number(value, Vb6Value.ValueType.Long);
                if (assigned == -1 && (value.Type == Vb6Value.ValueType.Integer || value.Type == Vb6Value.ValueType.Long))
                    throw new VBRunTimeException(VBStandardError.Overflow);
                color = assigned;
                return true;
            case "filetitle":
            case "hdc":
                throw new VBRunTimeException(VBStandardError.PropertySetCantBeUsedWithAReadOnlyProperty);
        }
        if (NotModelled.Contains(name))
            throw new NotImplementedException($"A CommonDialog's {name} is not implemented (#1084)");
        return false;
    }

    public async ValueTask<bool> TrySetPropertyAsync(string name, Vb6Value value)
    {
        if (!name.Equals("Action", StringComparison.OrdinalIgnoreCase))
            return false;
        // The dialog mappings and these invalid integral values have been measured.
        if (value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long)
            throw new NotImplementedException("CommonDialog Action assignment conversion is not measured (#1084)");
        switch (Convert.ToInt64(value.Value, CultureInfo.InvariantCulture))
        {
            case >= 1 and <= 6:
                var action = Convert.ToInt32(value.Value, CultureInfo.InvariantCulture);
                await CallAsync(action switch
                {
                    1 => "ShowOpen",
                    2 => "ShowSave",
                    3 => "ShowColor",
                    4 => "ShowFont",
                    5 => "ShowPrinter",
                    _ => "ShowHelp",
                }, []);
                return true;
            case 0 or 7 or 8 or -1:
                throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
            default:
                throw new NotImplementedException("This CommonDialog Action value is not measured (#1084)");
        }
    }

    private static short Integer(Vb6Value value, Func<short, bool>? accepts = null)
    {
        var number = (short)Number(value, Vb6Value.ValueType.Integer);
        if (accepts is not null && !accepts(number))
            throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        return number;
    }

    private static bool Boolean(Vb6Value value) =>
        (bool)VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Boolean, null).Value!;

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
            case "showcolor":
                await ShowColor();
                break;
            case "showprinter":
                await ShowPrinter();
                break;
            case "showfont":
                await ShowFont();
                break;
            case "showhelp":
                if (helpCommand is not (0 or 2 or 3))
                    throw new NotImplementedException("This CommonDialog ShowHelp command is not measured (#1084)");
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
            (flags & OverwritePrompt) != 0, !save && (flags & AllowMultiselect) != 0));
        if (!save && (flags & FileMustExist) != 0)
            flags |= PathMustExist;
        if (answer is null)
        {
            if (cancelError)
                throw new VBRunTimeException(Cancelled);
            return;
        }

        var path = answer.Path;
        var multiple = answer.AdditionalPaths is { Count: > 0 };
        if (multiple)
            path = MultipleFileName(answer);
        var title = multiple ? "" : TitleOf(path);
        if (!multiple && !title.Contains('.'))
        {
            var extension = defaultExt;
            if (save && extension.Length == 0 && filters.Count > 0)
            {
                var index = answer.FilterIndex ?? (filters.Count == 1 ? 1 : 0);
                if (index < 1 || index > filters.Count)
                    throw new NotImplementedException("A CommonDialog's Save extension needs the selected filter, which the host did not report (#1084)");
                var pattern = filters[index - 1].Patterns;
                if (!pattern.StartsWith("*.", StringComparison.Ordinal) || pattern.Length <= 2
                    || pattern[2..].IndexOfAny(['*', '?', ';', '.']) >= 0)
                    throw new NotImplementedException("A CommonDialog's Save extension for this filter pattern is not measured (#1084)");
                extension = pattern[2..];
            }
            if (extension.Length > 0)
            {
                path += "." + extension;
                title += "." + extension;
            }
        }
        if (!save && path.Length > maxFileSize)
            throw new VBRunTimeException(new VBStandardError(20476,
                "The FileName buffer is too small to store the selected file name(s). (Increase MaxFileSize)"));
        var dot = title.LastIndexOf('.');
        if (dot >= 0 && !title[(dot + 1)..].Equals(defaultExt, StringComparison.OrdinalIgnoreCase))
            flags |= ExtensionDifferent;
        fileName = path;
        fileTitle = title;
        if (answer.FilterIndex is { } chosen)
        {
            filterIndex = (short)chosen;
            filterIndexUnknown = false;
        }
        else if (filters.Count > 1)
            filterIndexUnknown = true;
    }

    private string MultipleFileName(VbFileDialogAnswer answer)
    {
        var explorer = (flags & Explorer) != 0;
        var split = answer.Path.LastIndexOfAny(['\\', '/']);
        if ((flags & AllowMultiselect) == 0 || split < 0)
            throw new NotImplementedException("This CommonDialog multi-select result is not measured (#1084)");
        var folder = answer.Path[..(split + 1)];
        var names = new List<string> { TitleOf(answer.Path) };
        foreach (var path in answer.AdditionalPaths!)
        {
            var separator = path.LastIndexOfAny(['\\', '/']);
            if (separator < 0 || path[..(separator + 1)] != folder)
                throw new NotImplementedException("CommonDialog multi-select across folders is not measured (#1084)");
            names.Add(TitleOf(path));
        }
        if (!explorer && (folder.Contains(' ') || names.Any(name => name.Contains(' '))))
            throw new NotImplementedException("CommonDialog old multi-select paths containing spaces are not measured (#1084)");
        return explorer
            ? folder[..^1] + "\0" + string.Join("\0", names)
            : folder + " " + string.Join(" ", names);
    }

    private async Task ShowColor()
    {
        // Only Flags 0 and cdlCCRGBInit have measured dialog behaviour (#2299).
        if (flags is not (0 or 1) || (flags == 1 && (color < 0 || color > 0xFFFFFF)))
            throw new NotImplementedException("This CommonDialog color flag or system color is not implemented (#2299)");
        var chosen = await host.ShowColorDialog(flags == 1 ? color : 0);
        if (chosen is { } rgb)
            color = rgb;
        else if (cancelError)
            throw new VBRunTimeException(Cancelled);
    }

    private async Task ShowPrinter()
    {
        if (flags != 0 || copies is not (1 or 3) || orientation != 1 || fromPage != 0 || toPage != 0 || min != 0 || max != 0)
            throw new NotImplementedException("This CommonDialog print configuration is not measured (#2299)");
        if (!await host.ShowPrintDialog())
        {
            if (cancelError)
                throw new VBRunTimeException(Cancelled);
            return;
        }
        copies = 1;
        orientation = 1;
        printAccepted = true;
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
