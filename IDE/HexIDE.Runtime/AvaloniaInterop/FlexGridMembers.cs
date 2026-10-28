using System.Threading.Tasks;
using Avalonia.LogicalTree;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

internal static class FlexGridMembers
{
    internal static Vb6Value Read(VBMSFlexGrid grid, string name, IReadOnlyList<Vb6Value> args)
    {
        if (args.Count != 0 && !IsIndexed(name)) throw Refused(name);
        var model = grid.Model;
        if (IsCellFormat(name))
        {
            var value = model.GetCellFormat(name.ToLowerInvariant());
            return value switch
            {
                int n => new Vb6Value(n),
                long n => new Vb6Value(n),
                double n => new Vb6Value(n),
                bool b => new Vb6Value(b),
                string text => new Vb6Value(text),
                _ => throw Refused(name),
            };
        }
        if (model.Appearance.TryGetValue(name.ToLowerInvariant(), out var appearance))
            return name.Equals("WordWrap", System.StringComparison.OrdinalIgnoreCase) ? new Vb6Value(appearance != 0) : new Vb6Value(appearance);
        return name.ToLowerInvariant() switch
        {
            "toprow" when model.Rows > 0 && model.Cols > 0 => new Vb6Value(model.TopRow),
            "leftcol" when model.Rows > 0 && model.Cols > 0 => new Vb6Value(model.LeftCol),
            "rows" => new Vb6Value(model.Rows),
            "cols" => new Vb6Value(model.Cols),
            "row" => new Vb6Value(model.Row),
            "col" => new Vb6Value(model.Col),
            "fixedrows" => new Vb6Value(model.FixedRows),
            "fixedcols" => new Vb6Value(model.FixedCols),
            "rowsel" => new Vb6Value(model.RowSel),
            "colsel" => new Vb6Value(model.ColSel),
            "selectionmode" => new Vb6Value(model.SelectionMode),
            "fillstyle" => new Vb6Value(model.FillStyle),
            "allowbigselection" => new Vb6Value(model.AllowBigSelection),
            "clip" => new Vb6Value(model.Clip),
            "textarray" when args.Count == 1 => new Vb6Value(model.GetTextArray(Index(args[0]))),
            "colwidth" when args.Count == 1 => new Vb6Value(model.GetColWidth(Index(args[0]))),
            "rowheight" when args.Count == 1 => new Vb6Value(model.GetRowHeight(Index(args[0]))),
            "colalignment" when args.Count == 1 => new Vb6Value(model.GetAlignment(Index(args[0]))),
            "fixedalignment" when args.Count == 1 => new Vb6Value(model.GetAlignment(Index(args[0]), true)),
            "text" => new Vb6Value(model.Text),
            "textmatrix" when args.Count == 2 => new Vb6Value(model.GetText(Index(args[0]), Index(args[1]))),
            "formatstring" => new Vb6Value(model.FormatString),
            _ => throw Refused(name),
        };
    }

    internal static void Write(VBMSFlexGrid grid, string name, IReadOnlyList<Vb6Value> args, Vb6Value value)
    {
        if (args.Count != 0 && !IsIndexed(name)) throw Refused(name);
        var model = grid.Model;
        name = name.ToLowerInvariant();
        try
        {
            if (model.Appearance.ContainsKey(name))
            {
                if (name == "wordwrap")
                    model.Appearance[name] = value.Value is bool wrap ? (wrap ? 1 : 0) : throw Refused(name);
                else if (name.Contains("color")) model.Appearance[name] = Colour(value);
                else
                {
                    var n = Index(value);
                    if (name == "gridlinewidth")
                    {
                        if (n is 0 or -1) throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
                        if (n < -1) throw Refused(name);
                        model.Appearance[name] = n;
                    }
                    else if (n >= 0 && n <= (name is "highlight" or "focusrect" ? 2 : 3)) model.Appearance[name] = n;
                }
                return;
            }
            if (IsCellFormat(name))
            {
                var member = name.ToLowerInvariant();
                object format = member switch
                {
                    "cellalignment" => Index(value),
                    "cellbackcolor" or "cellforecolor" => Colour(value),
                    "cellfontname" => Text(value),
                    "cellfontsize" => FontSize(value),
                    _ => value.Value is bool b ? b : throw Refused("font Boolean conversion"),
                };
                model.SetCellFormat(member, format);
                return;
            }
            switch (name.ToLowerInvariant())
            {
                case "toprow": grid.SetFirstCell(Index(value), model.LeftCol); break;
                case "leftcol": grid.SetFirstCell(model.TopRow, Index(value)); break;
                case "rows": model.Rows = Index(value); break;
                case "cols": model.Cols = Index(value); break;
                case "row": model.Row = Index(value); break;
                case "col": model.Col = Index(value); break;
                case "fixedrows": model.FixedRows = Index(value); break;
                case "fixedcols": model.FixedCols = Index(value); break;
                case "rowsel": model.RowSel = Index(value); break;
                case "colsel": model.ColSel = Index(value); break;
                case "selectionmode": model.SelectionMode = Index(value); break;
                case "fillstyle": model.FillStyle = Index(value); break;
                case "allowbigselection": model.AllowBigSelection = value.Value is bool b ? b : throw Refused(name); break;
                case "clip": model.Clip = Text(value); break;
                case "textarray" when args.Count == 1: model.SetTextArray(Index(args[0]), Text(value)); break;
                case "colwidth" when args.Count == 1: model.SetColWidth(Index(args[0]), Index(value)); break;
                case "rowheight" when args.Count == 1: model.SetRowHeight(Index(args[0]), Index(value)); break;
                case "rowheightmin": model.SetRowHeightMin(Index(value)); break;
                case "colalignment" when args.Count == 1: model.SetColAlignment(Index(args[0]), Index(value)); break;
                case "text": model.Text = Text(value); break;
                case "textmatrix" when args.Count == 2: model.SetText(Index(args[0]), Index(args[1]), Text(value)); break;
                case "formatstring": model.FormatString = Text(value); break;
                default: throw Refused(name);
            }
        }
        finally { grid.RefreshView(); }
    }

    internal static async Task SortAsync(VBMSFlexGrid grid, IReadOnlyList<Vb6Value> args, Vb6Value value)
    {
        if (args.Count != 0) throw Refused("Sort arguments");
        await grid.Model.SortAsync(Index(value), async (first, second) =>
        {
            var cmp = 0;
            var handled = false;
            foreach (var handler in ControlEvents.Handlers(grid, grid.FindLogicalAncestorOfType<IModuleExecutionRoot>(), "Compare"))
            {
                if (!handler.Root.Handles(handler.Name)) continue;
                if (handler.Own && VBProps.GetIndex(grid) is not null) throw Refused("Compare on a control array");
                handled = true;
                cmp = await handler.Root.CompareRowsAsync(handler.Name, first, second, cmp);
            }
            return handled ? cmp : throw Refused("Sort without a Compare handler");
        });
        grid.RefreshView();
    }

    internal static Vb6Value Call(VBMSFlexGrid grid, string name, IReadOnlyList<Vb6Value> args)
    {
        switch (name.ToLowerInvariant())
        {
            case "additem" when args.Count is 1 or 2:
                grid.Model.AddItem(Text(args[0]), args.Count == 2 ? Index(args[1]) : null);
                break;
            case "removeitem" when args.Count == 1:
                grid.Model.RemoveItem(Index(args[0]));
                break;
            default: throw Refused(name);
        }
        grid.RefreshView();
        return Vb6Value.Nothing;
    }

    private static bool IsCellFormat(string name) => name.ToLowerInvariant() is
        "cellbackcolor" or "cellforecolor" or "cellalignment" or "cellfontname" or "cellfontsize" or "cellfontbold" or "cellfontitalic" or
        "cellfontunderline" or "cellfontstrikethrough";

    private static long Colour(Vb6Value value)
    {
        var colour = value.Value switch
        {
            int n => (long)n,
            long n => n,
            _ => throw Refused("colour conversion"),
        };
        return colour is >= 0 and <= 0xffffff or 0x80000005 or 0x80000008 or 0x8000000f
            or 0x80000012 or 0x8000000d or 0x8000000e ? colour : throw Refused("colour value");
    }

    private static double FontSize(Vb6Value value)
    {
        var size = value.Value switch
        {
            int n => n,
            long n => (double)n,
            float n => n,
            double n => n,
            _ => throw Refused("font size conversion"),
        };
        return double.IsFinite(size) && size > 0 ? size : throw Refused("font size validation");
    }

    private static bool IsIndexed(string name) => name.ToLowerInvariant() is
        "textmatrix" or "textarray" or "colwidth" or "rowheight" or "colalignment" or "fixedalignment";

    // This slice accepts Integer/Long inputs and strings. Refuse unmeasured conversions.
    private static int Index(Vb6Value value) => value.Value switch
    {
        short n => n,
        int n => n,
        long n when n >= int.MinValue && n <= int.MaxValue => (int)n,
        _ => throw Refused("numeric conversion"),
    };
    private static string Text(Vb6Value value) => value.Value is string s ? s : throw Refused("text conversion");
    internal static NotImplementedException Refused(string name) => new($"MSFlexGrid.{name} is not implemented (#1099)");
}
