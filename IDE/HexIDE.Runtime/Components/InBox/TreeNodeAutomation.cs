using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Components.InBox;

internal sealed partial class TreeNodes : ICSharpAsyncProxy, ICSharpPropertyBag, INamedParameterSource
{
    void ICSharpProxy.Call(string method, List<Vb6Value> args) =>
        throw new InvalidOperationException();

    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        Vb6Value? result;
        switch (method.ToLowerInvariant())
        {
            case "add":
                if (args.Count > 6) throw new NotImplementedException();
                var relative = Argument(args, 0);
                var relationship = Argument(args, 1);
                int? relation = relationship.IsMissing ? null : Relationship(relationship);
                // There is no bound ImageList in this slice. VB6 refuses any supplied image in that state.
                if (!Argument(args, 4).IsMissing) throw Error(35613);
                if (!Argument(args, 5).IsMissing) throw new NotImplementedException();
                result = Wrap(Add(relative.IsMissing ? null : LookupValue(relative), relation,
                    OptionalText(Argument(args, 2)), OptionalText(Argument(args, 3))));
                break;
            case "item":
                RequireArguments(args, 1);
                result = Default(args);
                break;
            case "remove":
                RequireArguments(args, 1);
                Remove(LookupValue(args[0]));
                result = null;
                break;
            case "clear":
                RequireArguments(args, 0);
                Clear();
                result = null;
                break;
            case "count":
                RequireArguments(args, 0);
                result = new Vb6Value(Count);
                break;
            default:
                throw new NotImplementedException();
        }
        return Task.FromResult(result);
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        if (!name.Equals("Count", StringComparison.OrdinalIgnoreCase)) throw new NotImplementedException();
        value = new Vb6Value(Count);
        return true;
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        if (name.Equals("Count", StringComparison.OrdinalIgnoreCase)) throw Error(383);
        throw new NotImplementedException();
    }

    internal Vb6Value Default(IReadOnlyList<Vb6Value> args)
    {
        RequireArguments(args, 1);
        return Wrap(Item(LookupValue(args[0])));
    }

    internal static Vb6Value Wrap(TreeNode? node) => node is null
        ? Vb6Value.Nothing : new Vb6Value(node);

    internal static object LookupValue(Vb6Value value) => value.Value switch
    {
        TreeNode node => node,
        string text => text,
        short n => n,
        int n => n,
        long n => n,
        double n => n,
        _ => throw new NotImplementedException(),
    };

    private static int Relationship(Vb6Value value)
    {
        if (value.Value is string text && !double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            throw Error(13);
        var number = LookupValue(value);
        return number switch
        {
            short n => n,
            int n => n,
            long n when n is >= int.MinValue and <= int.MaxValue => (int)n,
            _ => throw new NotImplementedException(),
        };
    }

    internal static string TextValue(Vb6Value value) => value.Value is string text
        ? text : throw new NotImplementedException();

    internal static bool BooleanValue(Vb6Value value) => value.Value is bool flag
        ? flag : throw new NotImplementedException();

    private static string OptionalText(Vb6Value value) => value.IsMissing ? "" : TextValue(value);
    private static Vb6Value Argument(IReadOnlyList<Vb6Value> args, int index) =>
        index < args.Count ? args[index] : Vb6Value.Missing;

    internal static void RequireArguments(IReadOnlyList<Vb6Value> args, int count)
    {
        if (args.Count != count || args.Any(a => a.IsMissing)) throw new NotImplementedException();
    }

    bool INamedParameterSource.TryGetParameters(string member, out IReadOnlyList<string> names, out int required)
    {
        (names, required) = member.ToLowerInvariant() switch
        {
            "add" => (new string[] { "Relative", "Relationship", "Key", "Text", "Image", "SelectedImage" }, 0),
            "item" or "remove" => (new string[] { "Index" }, 1),
            _ => (Array.Empty<string>(), 0),
        };
        return member.ToLowerInvariant() is "add" or "item" or "remove" or "clear" or "count";
    }
}

internal sealed partial class TreeNode : ICSharpAsyncProxy, ICSharpPropertyBag
{
    void ICSharpProxy.Call(string method, List<Vb6Value> args) =>
        throw new InvalidOperationException();

    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        RequireAlive();
        TreeNodes.RequireArguments(args, 0);
        if (method.Equals("EnsureVisible", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<Vb6Value?>(new Vb6Value(EnsureVisible()));
        if (TryGetProperty(method, out var value)) return Task.FromResult<Vb6Value?>(value);
        throw new NotImplementedException();
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        RequireAlive();
        value = name.ToLowerInvariant() switch
        {
            "text" => new(Text),
            "key" => new(Key),
            "tag" => new(""),
            "bold" => new(Bold),
            "index" => new(Index),
            "children" => new(Children),
            "fullpath" => new(FullPath),
            "visible" => new(Visible),
            "expanded" => new(Expanded),
            "selected" => new(Selected),
            "checked" => new(Checked),
            "sorted" => new(Sorted),
            "parent" => TreeNodes.Wrap(Parent),
            "child" => TreeNodes.Wrap(Child),
            "root" => TreeNodes.Wrap(Root),
            "firstsibling" => TreeNodes.Wrap(FirstSibling),
            "lastsibling" => TreeNodes.Wrap(LastSibling),
            "next" => TreeNodes.Wrap(Next),
            "previous" => TreeNodes.Wrap(Previous),
            _ => throw new NotImplementedException(),
        };
        return true;
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        RequireAlive();
        switch (name.ToLowerInvariant())
        {
            case "text":
                Text = value.Type == Vb6Value.ValueType.Integer ? VbDisplay.ToText(value) : TreeNodes.TextValue(value);
                break;
            case "key": Key = TreeNodes.TextValue(value); break;
            case "expanded": Expanded = TreeNodes.BooleanValue(value); break;
            case "selected": Selected = TreeNodes.BooleanValue(value); break;
            case "bold": Bold = TreeNodes.BooleanValue(value); break;
            case "checked": Checked = TreeNodes.BooleanValue(value); break;
            case "sorted": Sorted = TreeNodes.BooleanValue(value); break;
            case "parent": throw TreeNodes.Error(438);
            case "index" or "children" or "fullpath" or "visible" or "child" or "root"
                or "firstsibling" or "lastsibling" or "next" or "previous": throw TreeNodes.Error(383);
            default: throw new NotImplementedException();
        }
        return true;
    }

    internal void SetObjectProperty(string name, Vb6Value value)
    {
        RequireAlive();
        switch (name.ToLowerInvariant())
        {
            case "tag" when value.Value is Avalonia.Controls.Control: throw TreeNodes.Error(35700);
            case "parent":
                if (value.Value is not null && value.Value is not TreeNode) throw new NotImplementedException();
                Parent = (TreeNode?)value.Value;
                break;
            case "child" or "root" or "firstsibling" or "lastsibling" or "next" or "previous": throw TreeNodes.Error(383);
            default: throw new NotImplementedException();
        }
    }
}
