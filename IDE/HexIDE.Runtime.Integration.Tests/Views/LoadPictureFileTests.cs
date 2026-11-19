using System;
using System.IO;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using HexIDE.Integration.Tests;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// VB6 SP6 native/p-code measurements: https://github.com/hexide-io/HexIDE/issues/4597 (latest comment).
public class LoadPictureFileTests : WindowTestFixture
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.Image Image1
              Width = 1200
              Height = 600
           End
        End
        Attribute VB_Name = "Form1"
        """;

    // Generated with Pillow: Image.new("RGB", (20, 10), "red").save(stream, format).
    private const string Jpeg = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAAKABQDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDi6KKK+ZP3EKKKKAP/2Q==";
    private const string Gif = "R0lGODdhFAAKAIEAAP8AAAAAAAAAAAAAACwAAAAAFAAKAAAIGQABCBxIsKDBgwgTKlzIsKHDhxAjSpyoMCAAOw==";
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAABQAAAAKCAIAAAA7N+mxAAAAFUlEQVR4nGP8z0A+YKJA76jmEaIZAHjzARO1LQhGAAAAAElFTkSuQmCC";

    private sealed class Pictures : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        public Pictures() => Directory.CreateDirectory(folder);
        public string PathFor(string name) => Path.Combine(folder, name);
        public string Write(string name, byte[] bytes)
        {
            var path = PathFor(name);
            File.WriteAllBytes(path, bytes);
            return path;
        }
        public void Dispose() => Directory.Delete(folder, true);
    }

    private static byte[] Bitmap()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x4d42); writer.Write(654); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(20); writer.Write(10); writer.Write((ushort)1); writer.Write((ushort)24);
        writer.Write(new byte[24]);
        for (var i = 0; i < 200; i++) { writer.Write((byte)0); writer.Write((byte)0); writer.Write((byte)255); }
        return stream.ToArray();
    }


    // Additional no-32 and mask measurements: https://github.com/hexide-io/HexIDE/issues/4748 (oracle PR #4837).
    // SP6 native/p-code: #4623's latest comment, including order measurements from oracle PR #4692.
    [AvaloniaTheory]
    [InlineData("32:4", 847)]
    [InlineData("32:8", 847)]
    [InlineData("32:24", 847)]
    [InlineData("32:32", 847)]
    [InlineData("16:4", 423)]
    [InlineData("48:4", 1270)]
    [InlineData("32:4,16:4", 847)]
    [InlineData("16:4,32:4", 847)]
    [InlineData("32:32:h", 847)]
    [InlineData("256:png,32:4", 847)]
    [InlineData("32:4,256:png", 847)]
    [InlineData("32:png", 481)]
    [InlineData("32:32:n,256:png", 481)]
    [InlineData("48:32:n,32:32:n,16:32:n", 481)]
    [InlineData("256:png,48:32:n,32:32:n,16:32:n", 481)]
    [InlineData("32:32:nh", 481)]
    [InlineData("32:32:n", 481)]
    [InlineData("16:4,48:4", 423)]
    [InlineData("48:4,16:4", 1270)]
    [InlineData("16:32,48:32", 423)]
    [InlineData("16:8,24:8,48:8", 423)]
    [InlineData("48:4,64:4", 1270)]
    [InlineData("16:4,24:4", 423)]
    [InlineData("32:4:n", 481)]
    [InlineData("32:8:n", 481)]
    [InlineData("32:24:n", 481)]
    [InlineData("256:png,48:4", 481)]
    [InlineData("48:4:n,16:4", 481)]
    [InlineData("16:4,48:32:n", 423)]
    [InlineData("48:32:n,16:4", 481)]
    public void MeasuredIconFiles(string images, int expected)
    {
        var bytes = Icon(images);
        if (expected == 481)
        {
            var load = () => PanelPicture.FromFile(bytes, loadPicture: true);
            load.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(481);
            return;
        }
        var picture = PanelPicture.FromFile(bytes, loadPicture: true);
        picture.TryGetProperty("Width", out var width).Should().BeTrue();
        width.Value.Should().Be((long)expected);
        picture.TryGetProperty("Type", out var type).Should().BeTrue();
        type.Value.Should().Be((short)3);
    }

    [AvaloniaTheory]
    [InlineData("32:32,32:4")]
    [InlineData("32:4,32:32")]
    [InlineData("32:8,32:32")]
    [InlineData("32:4,32:8,32:32")]
    [InlineData("32:4,32:32,32:8")]
    [InlineData("32:8,32:4,32:32")]
    [InlineData("32:8,32:32,32:4")]
    [InlineData("32:32,32:4,32:8")]
    [InlineData("32:32,32:8,32:4")]
    [InlineData("16:4,48:4")]
    [InlineData("48:4,16:4")]
    [InlineData("16:32,48:32")]
    [InlineData("16:8,24:8,48:8")]
    [InlineData("48:4,64:4")]
    [InlineData("16:4,24:4")]
    [InlineData("16:4,48:32:n")]
    public void MeasuredIconOrderTakesFirstColour(string images)
    {
        var picture = PanelPicture.FromFile(Icon(images), loadPicture: true);
        var pixels = new byte[4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { picture.Bitmap!.CopyPixels(new PixelRect(0, 0, 1, 1), pin.AddrOfPinnedObject(), 4, 4); }
        finally { pin.Free(); }
        // First image is red, later images green/blue. Account for the decoder's pixel format.
        var red = picture.Bitmap!.Format == Avalonia.Platform.PixelFormat.Rgba8888 ? 0 : 2;
        pixels[red].Should().Be(255);
        pixels[1].Should().Be(0);
        pixels[2 - red].Should().Be(0);
    }

    [AvaloniaTheory]
    [InlineData("256:png,32:4", 0, 847)]
    [InlineData("32:png", 481, 0)]
    public async Task LoadPictureUsesMeasuredIconRules(string images, int error, int width)
    {
        using var files = new Pictures();
        var path = files.Write("icon.dat", Icon(images));
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env, $"""
            Dim p As Object
            On Error Resume Next
            Set p = LoadPicture({Literal(path)})
            Debug.Print Err.Number
            If Err.Number = 0 Then Debug.Print p.Width
            """);
        values.Select(v => v.Value).Should().Equal(error == 0 ? new object[] { 0L, (long)width } : new object[] { (long)error });
    }

    [AvaloniaTheory]
    [InlineData("32:png", 847)]
    [InlineData("48:4,16:4", 1270)]
    [InlineData("256:png,32:4", 6773)]
    public void OtherPictureSourcesKeepTodaysDecoding(string images, long expectedWidth)
    {
        var picture = PanelPicture.FromFile(Icon(images));
        picture.TryGetProperty("Width", out var width).Should().BeTrue();
        width.Value.Should().Be(expectedWidth);
    }

    private static byte[] Icon(string description)
    {
        var entries = description.Split(',').Select((entry, index) =>
        {
            var fields = entry.Split(':');
            var size = int.Parse(fields[0]);
            var flags = fields.Length > 2 ? fields[2] : "";
            return (size, depth: fields[1] == "png" ? 32 : int.Parse(fields[1]),
                data: fields[1] == "png" ? IconPng(size) : IconDib(size, int.Parse(fields[1]), !flags.Contains('n'), flags.Contains('h'), index));
        }).ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)entries.Length);
        var offset = 6 + 16 * entries.Length;
        foreach (var entry in entries)
        {
            writer.Write((byte)entry.size); writer.Write((byte)entry.size); writer.Write((ushort)0);
            writer.Write((ushort)1); writer.Write((ushort)entry.depth);
            writer.Write(entry.data.Length); writer.Write(offset);
            offset += entry.data.Length;
        }
        foreach (var entry in entries) writer.Write(entry.data);
        return stream.ToArray();
    }

    private static byte[] IconDib(int size, int depth, bool mask, bool headerFields, int colour)
    {
        var stride = (size * depth + 31) / 32 * 4;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(40); writer.Write(size); writer.Write(size * 2);
        writer.Write((ushort)1); writer.Write((ushort)depth); writer.Write(0);
        writer.Write(headerFields ? stride * size : 0);
        writer.Write(headerFields ? 3780 : 0); writer.Write(headerFields ? 3780 : 0);
        writer.Write(0); writer.Write(0);
        var rgb = new byte[] { 0, 0, 0, 255 };
        rgb[2 - colour % 3] = 255;
        if (depth <= 8)
            for (var i = 0; i < 1 << depth; i++) writer.Write(rgb);
        for (var y = 0; y < size; y++)
        {
            var row = new byte[stride];
            if (depth > 8)
                for (var x = 0; x < size; x++) Array.Copy(rgb, 0, row, x * (depth / 8), depth / 8);
            writer.Write(row);
        }
        if (mask) writer.Write(new byte[(size + 31) / 32 * 4 * size]);
        return stream.ToArray();
    }

    private static byte[] IconPng(int size)
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string name, byte[] data)
        {
            var length = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, data.Length); stream.Write(length);
            var bytes = System.Text.Encoding.ASCII.GetBytes(name).Concat(data).ToArray();
            stream.Write(bytes);
            var crc = uint.MaxValue;
            foreach (var b in bytes)
            {
                crc ^= b;
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
            }
            BinaryPrimitives.WriteUInt32BigEndian(length, ~crc); stream.Write(length);
        }
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, size);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), size);
        header[8] = 8; header[9] = 2;
        Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(new byte[(size * 3 + 1) * size]);
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []);
        return stream.ToArray();
    }

    private static string Literal(string path) => "\"" + path.Replace("\"", "\"\"") + "\"";

    [AvaloniaTheory]
    [InlineData("bmp")]
    [InlineData("jpg")]
    [InlineData("gif")]
    public async Task MeasuredBitmapPicturesLoad(string kind)
    {
        using var files = new Pictures();
        // Deliberately use an unrelated extension: detection is by content.
        var path = files.Write("picture.dat", kind == "bmp" ? Bitmap() : Convert.FromBase64String(kind == "jpg" ? Jpeg : Gif));
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env, $"""
            Dim p As Object
            On Error Resume Next
            Set p = LoadPicture({Literal(path)})
            Debug.Print Err.Number
            Debug.Print TypeName(p)
            Debug.Print p.Type
            Debug.Print p.Width
            Debug.Print p.Height
            """);
        values.Select(v => v.Value).Should().Equal(0L, "Picture", (short)1, 529L, 265L);
    }

    [AvaloniaTheory]
    [InlineData("png", 481, "Invalid picture")]
    [InlineData("text", 481, "Invalid picture")]
    [InlineData("empty", 481, "Invalid picture")]
    [InlineData("missing", 53, "File not found")]
    [InlineData("folderMissing", 76, "Path not found")]
    [InlineData("directory", 75, "Path/File access error")]
    public async Task MeasuredErrorsResumeNext(string kind, int number, string description)
    {
        using var files = new Pictures();
        var path = kind switch
        {
            "png" => files.Write("x.png", Convert.FromBase64String(Png)),
            "text" => files.Write("t.png", "not an image"u8.ToArray()),
            "empty" => files.Write("e.png", []),
            "missing" => files.PathFor("nope.png"),
            "folderMissing" => files.PathFor(Path.Combine("nodir", "x.png")),
            _ => files.PathFor("")
        };
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env, $"""
            Dim p As Object
            On Error Resume Next
            Set p = LoadPicture({Literal(path)})
            Debug.Print Err.Number
            Debug.Print Err.Description
            Debug.Print TypeName(p)
            """);
        values.Select(v => v.Value).Should().Equal((long)number,
            number == 481 ? description : $"{description}: '{path}'", "Nothing");
    }

    [AvaloniaTheory]
    [InlineData("png", 481)]
    [InlineData("missing", 53)]
    [InlineData("folderMissing", 76)]
    [InlineData("directory", 75)]
    public async Task MeasuredErrorsReachGoToFallback(string kind, int number)
    {
        using var files = new Pictures();
        var path = kind switch
        {
            "png" => files.Write("x.png", Convert.FromBase64String(Png)),
            "missing" => files.PathFor("nope.png"),
            "folderMissing" => files.PathFor(Path.Combine("nodir", "x.png")),
            _ => files.PathFor("")
        };
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env, $"""
            Dim p As Object
            On Error GoTo fallback
            Set p = LoadPicture({Literal(path)})
            Exit Sub
            fallback:
            Debug.Print Err.Number
            Debug.Print "fallback colour"
            """);
        values.Select(v => v.Value).Should().Equal((long)number, "fallback colour");
    }

    [AvaloniaFact]
    public async Task JpegAssignedToImageDecodesAtItsPixelSize()
    {
        using var files = new Pictures();
        var path = files.Write("x.jpg", Convert.FromBase64String(Jpeg));
        var (_, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        await ContainerRuntimeHarness.Run(ctx, env, $"Set Image1.Picture = LoadPicture({Literal(path)})");
        var image = (VBImage)ContainerRuntimeHarness.Child(canvas, "Image1");
        using var bitmap = FrxImageHelper.TryDecodeToAvaloniaBitmap(image.Picture);
        bitmap.Should().NotBeNull();
        bitmap!.PixelSize.Should().Be(new PixelSize(20, 10));
        image.Picture.Should().Equal(Convert.FromBase64String(Jpeg));
    }

    [AvaloniaTheory]
    [InlineData("D7CD C69A")]
    [InlineData("0100 0900")]
    [InlineData("0200 0900")]
    [InlineData("0000 0200")]
    [InlineData("0100 0000 000000000000000000000000000000000000000000000000000000000000000000000000 20454D46")]
    public void UnmeasuredMetafilesAndCursorsRefuse(string signature)
    {
        var load = () => PanelPicture.FromFile(Convert.FromHexString(signature.Replace(" ", "")));
        load.Should().Throw<NotImplementedException>().WithMessage("*(#4597)");
    }

    [AvaloniaTheory]
    [InlineData("424D")]
    [InlineData("00000100")]
    [InlineData("FFD8FF")]
    [InlineData("474946383761")]
    [InlineData("474946383961")]
    public void RecognisedButUndecodablePicturesKeepTheRefusal(string signature)
    {
        var load = () => PanelPicture.FromFile(Convert.FromHexString(signature), loadPicture: true);
        load.Should().Throw<NotImplementedException>().WithMessage("This Panel.Picture image is not implemented (#1087)");
    }
}
