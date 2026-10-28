using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

internal sealed class ListViewSubItems(Action alive, Action? changed = null) : ListViewTextCollection<ListViewSubItem>
{
    internal override void Changed() => changed?.Invoke();
    internal override void Alive() => alive();
    protected override IReadOnlyList<string> AddParameters => ["Index", "Key", "Text", "ReportIcon", "ToolTipText"];
    protected override ListViewSubItem Create(List<Vb6Value> args, int at)
    {
        if (!ListViewItems.Argument(args, 3).IsMissing)
            throw ListViewItems.Error(35613);
        if (!ListViewItems.Argument(args, 4).IsMissing)
            throw new NotImplementedException("ListSubItems.Add ToolTipText is unmeasured (#1085)");
        return new ListViewSubItem(this);
    }
}

internal sealed class ListViewSubItem(ListViewSubItems items) : ListViewTextObject
{
    private bool bold;
    public override bool TryGetProperty(string name, out Vb6Value value)
    {
        items.Alive();
        if (!items.Entries.Contains(this))
            throw new NotImplementedException("Removed ListSubItem reads are unmeasured (#1085)");
        value = name.ToLowerInvariant() switch
        {
            "text" => new Vb6Value(Text), "key" => new Vb6Value(Key), "tag" => Tag,
            "index" => new Vb6Value((long)items.Entries.IndexOf(this) + 1),
            "reporticon" => Vb6Value.Variant,
            "bold" => new Vb6Value(bold), "tooltiptext" => new Vb6Value(""),
            "forecolor" => throw new NotImplementedException("ListSubItem default ForeColor is unmeasured (#1085)"),
            _ => throw new NotImplementedException($"ListSubItem.{name} is not implemented (#1085)"),
        };
        return true;
    }
    public override bool TrySetProperty(string name, Vb6Value value)
    {
        items.Alive();
        if (!items.Entries.Contains(this))
            throw new NotImplementedException("Removed ListSubItem assignments are unmeasured (#1085)");
        if (SetTextProperty(name, value))
        {
            items.Changed();
            return true;
        }
        switch (name.ToLowerInvariant())
        {
            case "bold": bold = ListViewItems.Boolean(value); break;
            case "tooltiptext": throw new NotImplementedException("ListSubItem.ToolTipText assignment is unmeasured (#1085)");
            case "reporticon": throw ListViewItems.Error(35613);
            case "forecolor": throw new NotImplementedException("ListSubItem ForeColor values are unmeasured in group 1 (#1085)");
            default: throw new NotImplementedException($"ListSubItem.{name} assignment is not implemented (#1085)");
        }
        items.Changed();
        return true;
    }
}
