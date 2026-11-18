using System;
using System.IO;
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
        var load = () => PanelPicture.FromFile(Convert.FromHexString(signature));
        load.Should().Throw<NotImplementedException>().WithMessage("This Panel.Picture image is not implemented (#1087)");
    }
}
