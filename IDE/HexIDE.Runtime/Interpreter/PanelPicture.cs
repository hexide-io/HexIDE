using System.IO;
using Avalonia.Media.Imaging;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Interpreter;

// #1087, issuecomment-6428228813: saved panel pictures contain the original BMP/ICO file.
internal sealed class PanelPicture : ICSharpProxy, ICSharpPropertyBag
{
    private readonly Lazy<Bitmap?> bitmap;

    private PanelPicture(byte[] file) => bitmap = new(() => FrxImageHelper.TryDecodeToAvaloniaBitmap(file));

    internal static PanelPicture FromRecord(byte[] record)
    {
        var clsid = new Guid("0BE35204-8F91-11CE-9DE3-00AA004BB851").ToByteArray();
        if (record.Length < 28 || BitConverter.ToInt32(record, 0) != record.Length - 4
            || !record.AsSpan(4, 16).SequenceEqual(clsid)
            || BitConverter.ToInt32(record, 20) != 0x746c
            || BitConverter.ToInt32(record, 24) != record.Length - 28)
            throw new NotImplementedException("This Panel.Picture record is not implemented (#1087)");
        return FromFile(record[28..]);
    }

    internal static PanelPicture FromFile(byte[] file)
    {
        if (FrxImageHelper.DetectKind(file) is not (FrxImageHelper.FrxBlobKind.Bitmap or FrxImageHelper.FrxBlobKind.Icon))
            throw new NotImplementedException("This Panel.Picture file format is not implemented (#1087)");
        var picture = new PanelPicture(file);
        if (picture.Bitmap == null)
            throw new NotImplementedException("This Panel.Picture image is not implemented (#1087)");
        return picture;
    }

    internal Bitmap? Bitmap => bitmap.Value;

    internal static Vb6Value Load(IReadOnlyList<Vb6Value> args, VbFiles files)
    {
        if (args.Count != 1 || args[0].Type != Vb6Value.ValueType.String || args[0].Value is not string { Length: > 0 } path)
            throw new NotImplementedException("This LoadPicture argument combination is unmeasured (#1087)");
        try { return new Vb6Value(FromFile(File.ReadAllBytes(files.FullPath(path)))); }
        catch (IOException) { throw new NotImplementedException("LoadPicture file errors are unmeasured (#1087)"); }
        catch (UnauthorizedAccessException) { throw new NotImplementedException("LoadPicture file errors are unmeasured (#1087)"); }
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = default;
        // Type/Width/Height values were measured, but their return types were not. Native handles
        // and the rest of StdPicture also need their own measured contract before being exposed.
        throw new NotImplementedException($"Panel.Picture.{name} is not implemented (#1087)");
    }

    public bool TrySetProperty(string name, Vb6Value value) => throw new NotImplementedException($"Panel.Picture.{name} is not implemented (#1087)");
    public void Call(string method, List<Vb6Value> args) => throw new NotImplementedException($"Panel.Picture.{method} is not implemented (#1087)");
}
