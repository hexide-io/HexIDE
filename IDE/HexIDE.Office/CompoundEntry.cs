namespace HexIDE.Office;

/// <summary>A directory entry. Stream bytes are read only on request, while its compound file is open.</summary>
public sealed class CompoundEntry
{
    private readonly CompoundFile owner;
    private readonly List<CompoundEntry> children = [];
    private HashSet<string>? childNames;
    internal uint Left { get; }
    internal uint Right { get; }
    internal uint Child { get; }
    internal uint Start { get; }
    internal byte Type { get; }

    internal CompoundEntry(CompoundFile owner, string name, byte type, ulong length,
        uint left, uint right, uint child, uint start)
    {
        this.owner = owner;
        Name = name;
        Type = type;
        Length = length;
        Left = left;
        Right = right;
        Child = child;
        Start = start;
        Children = children.AsReadOnly();
    }

    public string Name { get; }
    public bool IsStorage => Type is 1 or 5;
    public ulong Length { get; }
    public IReadOnlyList<CompoundEntry> Children { get; }
    internal bool AddChild(CompoundEntry child)
    {
        childNames ??= new HashSet<string>(StringComparer.Ordinal);
        var key = string.Create(child.Name.Length, child.Name, (destination, name) =>
        {
            for (var i = 0; i < name.Length; ++i)
                destination[i] = Upper(name[i]);
        });
        if (!childNames.Add(key))
            return false;
        children.Add(child);
        return true;
    }

    public CompoundEntry? FindChild(string name) => children.Find(child => NamesEqual(child.Name, name));

    // MS-CFB 2.6.4 compares individual UTF-16 units, without casing surrogate pairs.
    internal static bool NamesEqual(string left, string right)
    {
        if (left.Length != right.Length)
            return false;
        for (var i = 0; i < left.Length; ++i)
            if (Upper(left[i]) != Upper(right[i]))
                return false;
        return true;
    }

    // .NET invariant casing leaves dotless i unchanged; Unicode's simple uppercase is I.
    private static char Upper(char value) => value == '\u0131' ? 'I' : char.ToUpperInvariant(value);

    public byte[] ReadBytes()
    {
        if (IsStorage)
            throw new OfficeFileException("A storage has children, not stream bytes.");
        return owner.ReadStream(this);
    }
}
