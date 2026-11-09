namespace HexIDE.Runtime.Interpreter;

// Measured native and p-code: docs/vb6-fidelity-oracle/loadresstring-loadresdata-and-loadrespicture.md (#2626).
public partial class VB6BuiltIns
{
    internal Func<Win32Resources> ProjectResources { get; set; } = () => new();

    private static void RegisterResources(Dictionary<string, BuiltinFn> d)
    {
        d["LoadResString"] = (self, a, _) =>
        {
            ResourceArguments(a, 1);
            var id = ResourceNumber(a[0]);
            var text = id is >= 0 and <= ushort.MaxValue ? self.ProjectResources().FindString((ushort)id) : null;
            return text is null ? throw ResourceMissing(id) : new Vb6Value(text);
        };
        d["LoadResData"] = (self, a, _) =>
        {
            ResourceArguments(a, 2);
            var id = ResourceIdentifier(a[0]);
            var type = ResourceIdentifier(a[1]);
            var bytes = type is ushort t && t == 6 ? null : self.ProjectResources().Find(type, id);
            if (bytes is null) throw ResourceMissing(id);
            return MakeArray(Vb6Value.ValueType.Byte, bytes.Select(b => new Vb6Value(b)).ToArray());
        };
        d["LoadResPicture"] = (self, a, _) =>
        {
            ResourceArguments(a, 2);
            var id = ResourceIdentifier(a[0]);
            var kind = ResourceNumber(a[1]);
            if (kind is < 0 or > 2) throw new VBRunTimeException(VBStandardError.InvalidPictureType);
            var bytes = self.ProjectResources().Find((ushort)(kind == 0 ? 2 : kind == 1 ? 14 : 12), id);
            if (bytes is null) throw ResourceMissing(id);
            if (kind != 0) throw new NotImplementedException("Resource icons and cursors are unmeasured (#2626)");
            return new Vb6Value(PanelPicture.FromResourceBitmap(bytes));
        };
    }

    private static void ResourceArguments(IReadOnlyList<Vb6Value> args, int count)
    {
        if (args.Count != count) throw new NotImplementedException("Resource argument count is unmeasured (#2626)");
    }

    private static int ResourceNumber(Vb6Value value)
    {
        if (value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long
            && value.Type != Vb6Value.ValueType.Single && value.Type != Vb6Value.ValueType.Double
            && value.Type != Vb6Value.ValueType.String)
            throw new NotImplementedException("This resource argument conversion is unmeasured (#2626)");
        return AsInt(value);
    }

    private static object ResourceIdentifier(Vb6Value value)
    {
        if (value.Value is string text) return text;
        var id = ResourceNumber(value);
        return id is >= 0 and <= ushort.MaxValue ? (object)(ushort)id : id;
    }

    private static VBRunTimeException ResourceMissing(object id) =>
        new(new VBStandardError(326, $"Resource with identifier '{id}' not found"));
}
