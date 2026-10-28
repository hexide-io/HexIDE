namespace HexIDE.Runtime.Interpreter;

// #1092 part A: the owner model.
internal sealed class ImageComboModel : ICSharpAsyncProxy, ICSharpPropertyBag
{
    internal ImageComboItems Items { get; }
    private readonly EventPump events;
    private readonly Func<Task> change;
    private string text = "";
    private ImageComboItem? selected;
    private bool locked;
    internal int Indentation { get; private set; }

    internal ImageComboModel(EventPump events, Func<Task> change)
    {
        this.events = events;
        this.change = change;
        Items = new ImageComboItems(this);
    }

    internal void LoadText(string value) => text = value;
    internal void LoadIndentation(int value) => Indentation = value;

    internal bool IsSelected(ImageComboItem item) => ReferenceEquals(selected, item);
    internal void ItemTextChanged(ImageComboItem item)
    {
        if (IsSelected(item)) SetText(item.Text);
    }
    internal void Removed(ImageComboItem item)
    {
        if (IsSelected(item)) selected = null;
    }
    private void SetText(string value)
    {
        text = value;
        using var posting = events.Posting();
        events.Raise("ImageCombo_Change", change);
    }
    internal void Select(ImageComboItem? item)
    {
        if (item is not null && Items.Index(item) == 0) throw ImageComboItems.Unmeasured();
        selected = item;
        if (item is not null) SetText(item.Text);
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = name.ToLowerInvariant() switch
        {
            "comboitems" => new Vb6Value(Items),
            "text" => new Vb6Value(text),
            "selecteditem" => selected is null ? Vb6Value.Nothing : new Vb6Value(selected),
            "indentation" => new Vb6Value(Indentation),
            "locked" => new Vb6Value(locked),
            _ => throw ImageComboItems.Unmeasured(),
        };
        return true;
    }
    public bool TrySetProperty(string name, Vb6Value value)
    {
        switch (name.ToLowerInvariant())
        {
            case "text":
                if (selected is not null) throw ImageComboItems.Unmeasured();
                SetText(ImageComboItems.Text(value));
                break;
            case "selecteditem":
                if (value == Vb6Value.Nothing) Select(null);
                else if (value.Value is ImageComboItem item) Select(item);
                else if (value.Type == Vb6Value.ValueType.Integer && (int)value.Value! == 1)
                    throw new VBRunTimeException(VBStandardError.TypeMismatch);
                else throw ImageComboItems.Unmeasured();
                break;
            case "indentation":
                if (value.Type != Vb6Value.ValueType.Integer || (int)value.Value! != 2) throw ImageComboItems.Unmeasured();
                Indentation = 2;
                break;
            case "locked":
                if (value.Type == Vb6Value.ValueType.Boolean) locked = (bool)value.Value!;
                else if (value.Type == Vb6Value.ValueType.Integer && (int)value.Value! is 1 or 2 or -1)
                    locked = true;
                else throw ImageComboItems.Unmeasured();
                break;
            default: throw ImageComboItems.Unmeasured();
        }
        return true;
    }
    public void Call(string method, List<Vb6Value> args) => throw new InvalidOperationException("ImageCombo calls must be awaited");
    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        if (method.Equals("ComboItems", StringComparison.OrdinalIgnoreCase) && args.Count == 1)
            return Task.FromResult<Vb6Value?>(new Vb6Value(Items.Item(args[0])));
        if (args.Count != 0) throw ImageComboItems.Unmeasured();
        TryGetProperty(method, out var value);
        return Task.FromResult<Vb6Value?>(value);
    }
}
