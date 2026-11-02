using Avalonia.Controls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;
using static HexIDE.Runtime.Components.InBox.ImageListComponentClass;

namespace HexIDE.Runtime.BuiltinControls;

// Measured: #1083 issuecomment-6427015606; native handles and drawing are explicitly deferred by the supervisor.
public sealed class VBImageList : Control, ICSharpAsyncProxy, ICSharpPropertyBag
{
    internal ImageListImages Images { get; }
    internal readonly List<WeakReference<VBToolbar>> BoundToolbars = [];
    internal void RefuseBoundShrink()
    {
        BoundToolbars.RemoveAll(reference => !reference.TryGetTarget(out _));
        if (BoundToolbars.Any(reference => reference.TryGetTarget(out var bar) && bar.ImageList == this)) throw Error(35617);
    }
    internal int ImageWidth;
    internal int ImageHeight;
    internal int maskColor;
    private int backColor;
    internal bool useMaskColor;

    public VBImageList(ComponentInstance instance)
    {
        ImageWidth = instance.GetPropertyOrDefault(ImageWidthProperty);
        ImageHeight = instance.GetPropertyOrDefault(ImageHeightProperty);
        maskColor = instance.GetPropertyOrDefault(MaskColorProperty);
        backColor = instance.GetPropertyOrDefault(ImageBackColorProperty);
        useMaskColor = instance.GetPropertyOrDefault(UseMaskColorProperty);
        Images = new ImageListImages(this);
        if (instance.GetPropertyOrDefault(PicturesProperty) is null && instance.UnknownRawProperties.Any(p => p.Name == "Images"))
            throw new NotImplementedException("This ImageList Images group is unmeasured and not implemented (#1083)");
        foreach (var picture in instance.GetPropertyOrDefault(PicturesProperty) ?? [])
            Images.Load(picture);
        IsVisible = false;
    }

    internal static VBRunTimeException Error(int number) => new(new VBStandardError(number, number switch
    {
        35600 => "Index out of bounds", 35601 => "Element not found", 35602 => "Key is not unique in collection",
        35603 => "Invalid key", 35607 => "Required argument is missing",
        35617 => "ImageList cannot be modified while another control is bound to it",
        35611 => "Property is read-only if image list contains images", 35700 => "Circular object referencing is not allowed",
        383 => "Property is read-only", _ => throw new ArgumentOutOfRangeException(nameof(number)),
    }));

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = name.ToLowerInvariant() switch
        {
            "imagewidth" => new Vb6Value(ImageWidth), "imageheight" => new Vb6Value(ImageHeight),
            "maskcolor" => new Vb6Value((long)maskColor), "backcolor" => new Vb6Value((long)backColor),
            "usemaskcolor" => new Vb6Value(useMaskColor), "listimages" => new Vb6Value(Images),
            "left" or "top" => throw new VBRunTimeException(VBStandardError.PropertyGetCantBeExecutedAtRunTime),
            "index" => throw new VBRunTimeException(VBStandardError.ObjectNotAnArray),
            "himagelist" or "overlay" => throw new NotImplementedException("ImageList native handles and drawing are not implemented (#1083)"),
            _ => default,
        };
        return name.ToLowerInvariant() is "imagewidth" or "imageheight" or "maskcolor" or "backcolor" or "usemaskcolor" or "listimages";
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        switch (name.ToLowerInvariant())
        {
            case "imagewidth": ImageWidth = Dimension(value, ImageWidth); return true;
            case "imageheight": ImageHeight = Dimension(value, ImageHeight); return true;
            case "listimages": throw Error(383);
            case "himagelist": throw new NotImplementedException("ImageList.hImageList is not implemented (#1083)");
            case "left" or "top" or "index":
                throw new NotImplementedException("Setting this ImageList extender member is unmeasured (#1083)");
            case "maskcolor": maskColor = Long(value); InvalidateImages(); return true;
            case "backcolor": backColor = Long(value); return true;
            case "usemaskcolor":
                if (value.Type != Vb6Value.ValueType.Boolean) throw UnmeasuredConversion();
                useMaskColor = (bool)value.Value!; InvalidateImages(); return true;
            case "name" or "tag" or "parent" or "object": return false;
            default: throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        }
    }

    private void InvalidateImages()
    {
        foreach (var reference in BoundToolbars)
            if (reference.TryGetTarget(out var bar)) bar.InvalidateImages();
    }

    private int Dimension(Vb6Value value, int current)
    {
        Integral(value);
        var size = Convert.ToInt32(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Integer, null).Value);
        if (size < 0) throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        if (Images.Count > 0 && size != current) throw Error(35611);
        return size;
    }

    private static int Long(Vb6Value value)
    {
        Integral(value);
        return Convert.ToInt32(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Long, null).Value);
    }

    private static void Integral(Vb6Value value)
    {
        if (value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long) throw UnmeasuredConversion();
    }

    private static NotImplementedException UnmeasuredConversion() =>
        new("This ImageList property conversion is unmeasured and not implemented (#1083)");

    public void Call(string method, List<Vb6Value> args) => throw new InvalidOperationException("ImageList calls must be awaited");
    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        if (TryGetProperty(method, out var value))
            return Task.FromResult<Vb6Value?>(args.Count == 0 ? value : method.Equals("ListImages", StringComparison.OrdinalIgnoreCase)
                ? Images.Item(args[0]) : throw new NotImplementedException("Indexed ImageList property is not implemented"));
        throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
    }
}

internal sealed class ImageListImages(VBImageList owner) : ICSharpAsyncProxy, ICSharpPropertyBag, INamedParameterSource
{
    private readonly List<ImageListImage> items = [];
    internal int Count => items.Count;
    internal IEnumerable<Vb6Value> Values => items.Select(i => new Vb6Value(i));
    internal void Load(ImageListPicture picture) => items.Add(new ImageListImage(this, owner, new ImageListPictureObject(picture.Record), picture.Key, new Vb6Value(picture.Tag)));
    internal int Index(ImageListImage image) => items.IndexOf(image) + 1;
    internal Vb6Value Item(Vb6Value index) => new(items[Position(index)]);

    private int Position(Vb6Value index)
    {
        if (index.IsNull) throw VBImageList.Error(35603);
        if (index.Type == Vb6Value.ValueType.EmptyVariant) throw VBImageList.Error(35601);
        if (index.Type == Vb6Value.ValueType.String)
        {
            var key = (string)index.Value!;
            if (key.Length == 0) throw VBImageList.Error(35603);
            RefuseUnmeasuredKeyCase(key);
            var found = items.FindIndex(i => i.Key.Equals(key, StringComparison.Ordinal));
            return found >= 0 ? found : throw VBImageList.Error(35601);
        }
        var position = Convert.ToInt32(VbNumeric.CoerceOnStore(index, Vb6Value.ValueType.Long, null).Value);
        return position >= 1 && position <= items.Count ? position - 1 : throw VBImageList.Error(35600);
    }

    internal void SetKey(ImageListImage image, Vb6Value value)
    {
        var key = Key(value);
        CheckKey(key, image);
        image.Key = key;
    }

    private static string Key(Vb6Value value)
    {
        if (value.IsMissing) return "";
        if (value.Type != Vb6Value.ValueType.String)
        {
            if (Vb6Value.TryNumericToDouble(value, out _)) throw VBImageList.Error(35603);
            throw new NotImplementedException("This ImageList key conversion is unmeasured (#1083)");
        }
        var key = (string)value.Value!;
        if (key.Length > 0 && double.TryParse(key, out _)) throw VBImageList.Error(35603);
        return key;
    }

    private void CheckKey(string key, ImageListImage? except = null)
    {
        RefuseUnmeasuredKeyCase(key);
        if (key.Length > 0 && items.Any(i => i != except && i.Key.Equals(key, StringComparison.Ordinal)))
            throw VBImageList.Error(35602);
    }

    private void RefuseUnmeasuredKeyCase(string key)
    {
        if (items.Any(i => i.Key != key && i.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            throw new NotImplementedException("ImageList key case comparison is unmeasured (#1083)");
    }

    public void Call(string method, List<Vb6Value> args) => throw new InvalidOperationException("ListImages calls must be awaited");
    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        Vb6Value Arg(int i) => i < args.Count ? args[i] : Vb6Value.Missing;
        Vb6Value? result = null;
        switch (method.ToLowerInvariant())
        {
            case "count": result = new Vb6Value(items.Count); break;
            case "item": result = Item(Arg(0)); break;
            case "clear": owner.RefuseBoundShrink(); items.Clear(); owner.ImageWidth = owner.ImageHeight = 0; break;
            case "remove":
                var position = Position(Arg(0));
                owner.RefuseBoundShrink();
                items.RemoveAt(position);
                break;
            case "add":
            {
                var key = Key(Arg(1));
                CheckKey(key);
                var at = Arg(0).IsMissing ? items.Count : Convert.ToInt32(VbNumeric.CoerceOnStore(Arg(0), Vb6Value.ValueType.Long, null).Value) - 1;
                if (at < 0 || at > items.Count) throw VBImageList.Error(35600);
                var picture = Arg(2);
                if (picture.IsMissing) throw VBImageList.Error(35607);
                if (picture.Type == Vb6Value.ValueType.Object && picture.Value is null) throw new VBRunTimeException(VBStandardError.InvalidPicture);
                if (picture.Value is not ImageListPictureObject image)
                {
                    if (picture.Type == Vb6Value.ValueType.String) throw new VBRunTimeException(VBStandardError.TypeMismatch);
                    throw new NotImplementedException("Converting this value to an ImageList Picture is unmeasured (#1083)");
                }
                if (items.Count == 0) { owner.ImageWidth = image.PixelWidth; owner.ImageHeight = image.PixelHeight; }
                var added = new ImageListImage(this, owner, image, key, new Vb6Value(""));
                items.Insert(at, added);
                result = new Vb6Value(added);
                break;
            }
            default: throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        }
        return Task.FromResult(result);
    }

    public bool TryGetProperty(string name, out Vb6Value value) { value = new Vb6Value(items.Count); return name.Equals("Count", StringComparison.OrdinalIgnoreCase); }
    public bool TrySetProperty(string name, Vb6Value value) => name.Equals("Count", StringComparison.OrdinalIgnoreCase) ? throw VBImageList.Error(383) : false;
    public bool TryGetParameters(string member, out IReadOnlyList<string> names, out int required)
    {
        names = member.ToLowerInvariant() switch { "add" => ["Index", "Key", "Picture"], "item" or "remove" => ["Index"], _ => [] };
        required = member.Equals("add", StringComparison.OrdinalIgnoreCase) ? 0 : names.Count;
        return true;
    }
}

internal sealed class ImageListImage(ImageListImages images, VBImageList owner, ImageListPictureObject picture, string key, Vb6Value tag)
    : ICSharpAsyncProxy, ICSharpPropertyBag
{
    internal string Key = key;
    private Vb6Value tag = tag;
    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = name.ToLowerInvariant() switch
        {
            "index" => new Vb6Value(images.Index(this)), "key" => new Vb6Value(Key), "tag" => tag, "picture" => new Vb6Value(picture),
            "draw" or "extracticon" => throw new NotImplementedException("ListImage drawing and ExtractIcon are not implemented (#1083)"),
            _ => throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod),
        };
        return true;
    }
    public bool TrySetProperty(string name, Vb6Value value)
    {
        switch (name.ToLowerInvariant())
        {
            case "index" or "picture": throw VBImageList.Error(383);
            case "key": images.SetKey(this, value); return true;
            case "tag":
                if (ReferenceEquals(value.Value, owner)) throw VBImageList.Error(35700);
                tag = value; return true;
            default: return false;
        }
    }
    public void Call(string method, List<Vb6Value> args) => throw new InvalidOperationException("ListImage calls must be awaited");
    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args) { TryGetProperty(method, out var value); return Task.FromResult<Vb6Value?>(value); }
}

internal sealed class ImageListPictureObject : ICSharpAsyncProxy
{
    internal int PixelWidth { get; }
    internal int PixelHeight { get; }
    private readonly int pictureType;
    internal readonly byte[] Payload;
    internal ImageListPictureObject(byte[] record)
    {
        if (record.Length < 28 || BitConverter.ToInt32(record, 0) != record.Length - 4
            || new Guid(record.AsSpan(4, 16)) != new Guid("0BE35204-8F91-11CE-9DE3-00AA004BB851")
            || BitConverter.ToInt32(record, 20) != 0x746c || BitConverter.ToInt32(record, 24) != record.Length - 28)
            throw new NotImplementedException("Unmeasured ImageList picture record (#1083)");
        Payload = record[28..];
        var file = record.AsSpan(28);
        if (file.Length >= 26 && file[0] == 'B' && file[1] == 'M')
        {
            pictureType = 1; PixelWidth = BitConverter.ToInt32(file[18..]); PixelHeight = Math.Abs(BitConverter.ToInt32(file[22..]));
        }
        else if (file.Length >= 8 && BitConverter.ToInt32(file) == 0x10000)
        {
            pictureType = 3; PixelWidth = file[6] == 0 ? 256 : file[6]; PixelHeight = file[7] == 0 ? 256 : file[7];
        }
        else throw new NotImplementedException("Only measured BMP and ICO ImageList pictures are implemented (#1083)");
    }
    public void Call(string method, List<Vb6Value> args) => throw new InvalidOperationException("Picture calls must be awaited");
    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args) => Task.FromResult<Vb6Value?>(method.ToLowerInvariant() switch
    {
        "type" => new Vb6Value(pictureType),
        "width" => new Vb6Value(InBoxComponentClass.Extent(PixelWidth)),
        "height" => new Vb6Value(InBoxComponentClass.Extent(PixelHeight)),
        _ => throw new NotImplementedException("This Picture member is not implemented (#1083)"),
    });
}
