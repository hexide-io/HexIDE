using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Components.InBox;

/// <summary>
/// The TreeView's hierarchy (#1086), independent of drawing and persistence. Index order and sibling order are separate.
/// Behaviour is measured in the oracle's "In-box controls: the TreeView". The interpreter proxies share these identities; the control owns the same model.
/// </summary>
internal sealed partial class TreeNodes(Action? sortPending = null)
{
    private readonly List<TreeNode> items = [];
    internal readonly List<TreeNode> Roots = [];
    private readonly Dictionary<string, TreeNode> byKey = new(StringComparer.Ordinal);
    private TreeNode? selectedItem;
    private bool checkboxes;
    private bool sorted;
    private bool pendingSort;

    internal event Action? Changed;
    internal event Action<TreeNode, bool>? ExpansionChanged;
    internal void NotifyExpansion(TreeNode node, bool expanded) => ExpansionChanged?.Invoke(node, expanded);
    internal void NotifyChanged() => Changed?.Invoke();

    public short Count => unchecked((short)items.Count);
    public string PathSeparator { get; set; } = "\\";

    public bool Sorted
    {
        get => sorted;
        set
        {
            if (value) Sort(Roots);
            sorted = value;
            pendingSort = false;
            NotifyChanged();
        }
    }

    // The control binding calls this at the next message pump, not during Add.
    public void ProcessPendingSort()
    {
        if (!pendingSort) return;
        pendingSort = false;
        if (sorted) Sort(Roots);
        NotifyChanged();
    }

    internal static void Sort(List<TreeNode> siblings)
    {
        foreach (var node in siblings) RequireMeasuredSortText(node.Text);
        var ordered = siblings.OrderBy(n => n.Text, VbTextCompare.Comparer).ToArray();
        siblings.Clear();
        siblings.AddRange(ordered);
    }

    // Reuse the oracle's en-GB word weights, without its unmeasured Unicode fallback.
    private static void RequireMeasuredSortText(string text)
    {
        if (!VbTextCompare.HasMeasuredWeights(text)) throw new NotImplementedException();
    }

    public TreeNode? SelectedItem
    {
        get => selectedItem;
        set
        {
            if (value is not null) RequireOwned(value);
            if (selectedItem is not null) selectedItem.IsSelected = false;
            selectedItem = value;
            if (value is not null) value.IsSelected = true;
            NotifyChanged();
        }
    }

    public bool Checkboxes
    {
        get => checkboxes;
        set
        {
            checkboxes = value;
            if (value)
                foreach (var node in items) node.Checked = false;
            NotifyChanged();
        }
    }

    public TreeNode Add(object? relative = null, int? relationship = null, string key = "", string text = "")
    {
        var relation = relationship ?? 2;
        if (relation is < 0 or > 4) throw Error(380);
        var other = relative is null ? null : relative is TreeNode node ? RequireOwned(node) : Item(relative);
        ValidateKey(key, null);
        var parent = other is null ? null : relation == 4 ? other : other.ParentNode;
        var siblings = parent?.ChildNodes ?? Roots;
        var at = other is null || relation is 1 or 4 ? siblings.Count
            : relation == 0 ? 0
            : siblings.IndexOf(other) + (relation == 2 ? 1 : 0);
        var added = new TreeNode(this, items.Count + 1, key, text) { ParentNode = parent };
        if (parent?.Sorted == true)
        {
            RequireMeasuredSortText(text);
            foreach (var sibling in siblings) RequireMeasuredSortText(sibling.Text);
            at = siblings.FindIndex(n => VbTextCompare.Compare(n.Text, text) > 0);
            if (at < 0) at = siblings.Count;
        }
        siblings.Insert(at, added);
        if (parent is null && other is null && sorted && !pendingSort)
        {
            pendingSort = true;
            sortPending?.Invoke();
        }
        items.Add(added);
        if (key.Length > 0) byKey.Add(key, added);
        NotifyChanged();
        return added;
    }

    public TreeNode Item(object index)
    {
        if (index is string key)
        {
            if (key.Length == 0) throw Error(35603);
            return byKey.TryGetValue(key, out var node) ? node : throw Error(35601);
        }
        // Only measured numeric lookup forms. Automation conversion belongs to the future control binding.
        var number = index switch
        {
            short n => n,
            int n => n,
            long n => n,
            double n => Math.Round(n, MidpointRounding.ToEven),
            _ => throw new NotImplementedException(),
        };
        return number >= 1 && number <= items.Count ? items[(int)number - 1] : throw Error(35600);
    }

    public void Remove(object index)
    {
        var node = Item(index);
        var removed = new HashSet<TreeNode>();
        var pending = new Stack<TreeNode>();
        pending.Push(node);
        while (pending.TryPop(out var next))
        {
            removed.Add(next);
            foreach (var child in next.ChildNodes) pending.Push(child);
        }
        node.Siblings.Remove(node);
        if (selectedItem is not null && removed.Contains(selectedItem))
            SelectedItem = node.ParentNode ?? Roots.FirstOrDefault();
        foreach (var old in removed)
        {
            if (old.NodeKey.Length > 0) byKey.Remove(old.NodeKey);
            old.Alive = false;
        }
        items.RemoveAll(removed.Contains);
        for (var i = 0; i < items.Count; i++) items[i].Position = i + 1;
        NotifyChanged();
    }

    public void Clear()
    {
        SelectedItem = null;
        pendingSort = false;
        foreach (var node in items) node.Alive = false;
        items.Clear();
        Roots.Clear();
        byKey.Clear();
        NotifyChanged();
    }

    internal TreeNode RequireOwned(TreeNode node)
    {
        node.RequireAlive();
        if (!ReferenceEquals(node.Owner, this)) throw new NotImplementedException();
        return node;
    }

    internal void ChangeKey(TreeNode node, string key)
    {
        ValidateKey(key, node);
        if (node.NodeKey.Length > 0) byKey.Remove(node.NodeKey);
        if (key.Length > 0) byKey.Add(key, node);
        node.NodeKey = key;
    }

    private void ValidateKey(string key, TreeNode? owner)
    {
        if (key.Length == 0) return;
        if (key.All(char.IsAsciiDigit)) throw Error(35603);
        if (byKey.TryGetValue(key, out var node) && !ReferenceEquals(node, owner)) throw Error(35602);
    }

    // The model carries the measured number only. Automation error descriptions await the member binding.
    internal static VBRunTimeException Error(int number) => new(new VBStandardError(number, ""));
}

/// <summary>A node remains an identity when moved or reindexed, and becomes invalid when removed.</summary>
internal sealed partial class TreeNode
{
    internal readonly TreeNodes Owner;
    internal readonly List<TreeNode> ChildNodes = [];
    internal TreeNode? ParentNode;
    internal string NodeKey;
    internal int Position;
    internal bool Alive = true;
    internal bool IsSelected;
    private string text;
    private bool expanded;
    private bool isChecked;
    private bool bold;
    internal bool Bold { get => bold; set { bold = value; Owner.NotifyChanged(); } }
    private bool sorted;

    internal TreeNode(TreeNodes owner, int position, string key, string text)
    {
        Owner = owner;
        Position = position;
        NodeKey = key;
        this.text = text;
    }

    internal void RequireAlive()
    {
        if (!Alive) throw TreeNodes.Error(35605);
    }

    internal List<TreeNode> Siblings => ParentNode?.ChildNodes ?? Owner.Roots;

    public short Index { get { RequireAlive(); return unchecked((short)Position); } }
    public short Children { get { RequireAlive(); return unchecked((short)ChildNodes.Count); } }
    public string Text
    {
        get { RequireAlive(); return text; }
        set { RequireAlive(); text = value; Owner.NotifyChanged(); }
    }
    public string Key
    {
        get { RequireAlive(); return NodeKey; }
        set { RequireAlive(); Owner.ChangeKey(this, value); }
    }
    public TreeNode? Parent
    {
        get { RequireAlive(); return ParentNode; }
        set
        {
            RequireAlive();
            if (value is null) throw TreeNodes.Error(35610);
            Owner.RequireOwned(value);
            for (var ancestor = value; ancestor is not null; ancestor = ancestor.ParentNode)
                if (ReferenceEquals(ancestor, this)) throw TreeNodes.Error(35614);
            Siblings.Remove(this);
            ParentNode = value;
            value.ChildNodes.Insert(0, this);
            Owner.NotifyChanged();
        }
    }
    public TreeNode? Child { get { RequireAlive(); return ChildNodes.FirstOrDefault(); } }
    public TreeNode Root { get { RequireAlive(); return Owner.Roots[0]; } }
    public TreeNode FirstSibling { get { RequireAlive(); return Siblings[0]; } }
    public TreeNode LastSibling { get { RequireAlive(); return Siblings[^1]; } }
    public TreeNode? Next
    {
        get
        {
            RequireAlive();
            var at = Siblings.IndexOf(this) + 1;
            return at < Siblings.Count ? Siblings[at] : null;
        }
    }
    public TreeNode? Previous
    {
        get
        {
            RequireAlive();
            var at = Siblings.IndexOf(this) - 1;
            return at >= 0 ? Siblings[at] : null;
        }
    }
    public string FullPath
    {
        get
        {
            RequireAlive();
            var parts = new Stack<string>();
            for (var node = this; node is not null; node = node.ParentNode) parts.Push(node.text);
            return string.Join(Owner.PathSeparator, parts);
        }
    }
    public bool Visible
    {
        get
        {
            RequireAlive();
            for (var node = ParentNode; node is not null; node = node.ParentNode)
                if (!node.expanded) return false;
            return true;
        }
    }
    public bool Expanded
    {
        get { RequireAlive(); return expanded; }
        set
        {
            RequireAlive();
            var changed = expanded != value;
            expanded = value;
            if (!value)
                for (var node = Owner.SelectedItem?.ParentNode; node is not null; node = node.ParentNode)
                    if (ReferenceEquals(node, this)) { Owner.SelectedItem = this; break; }
            Owner.NotifyChanged();
            if (changed) Owner.NotifyExpansion(this, value);
        }
    }
    public bool Selected
    {
        get { RequireAlive(); return IsSelected; }
        set
        {
            RequireAlive();
            if (value) Owner.SelectedItem = this;
            else IsSelected = false;
            Owner.NotifyChanged();
        }
    }
    public bool Checked
    {
        get { RequireAlive(); return isChecked; }
        set { RequireAlive(); isChecked = value; Owner.NotifyChanged(); }
    }
    public bool Sorted
    {
        get { RequireAlive(); return sorted; }
        set
        {
            RequireAlive();
            if (value) TreeNodes.Sort(ChildNodes);
            sorted = value;
            Owner.NotifyChanged();
        }
    }
    public bool EnsureVisible()
    {
        RequireAlive();
        for (var node = ParentNode; node is not null; node = node.ParentNode)
            if (!node.expanded) node.Expanded = true;
        Owner.NotifyChanged();
        return false;
    }
}
