using System.Text;
using System.Globalization;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// New rows: https://github.com/hexide-io/HexIDE/issues/2626, supervisor measurement comment, 2026-11-14.
// vb6.exe SP6, native and p-code: docs/vb6-fidelity-oracle/loadresstring-loadresdata-and-loadrespicture.md.
public class LoadResourceTests : BaseVBTestFixture
{
    static LoadResourceTests() => Avalonia.Skia.SkiaPlatform.Initialize();
    internal static readonly byte[] Dib = [40,0,0,0,2,0,0,0,2,0,0,0,1,0,24,0,0,0,0,0,16,0,0,0,19,11,0,0,19,11,0,0,0,0,0,0,0,0,0,0,0,0,255,0,255,0,0,0,255,0,0,255,255,255,0,0];

    private static byte[] Entry(object type, object name, byte[] data, ushort language = 0x409)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode);
        writer.Write((uint)data.Length);
        writer.Write(0u);
        void Identifier(object value)
        {
            if (value is ushort ordinal) { writer.Write((ushort)0xffff); writer.Write(ordinal); }
            else { writer.Write(Encoding.Unicode.GetBytes((string)value)); writer.Write((ushort)0); }
        }
        Identifier(type);
        Identifier(name);
        while (stream.Position % 4 != 0) writer.Write((byte)0);
        writer.Write(0u);
        writer.Write((ushort)0x30);
        writer.Write(language);
        writer.Write(0u);
        writer.Write(0u);
        var header = (uint)stream.Position;
        writer.Write(data);
        while (stream.Position % 4 != 0) writer.Write((byte)0);
        stream.Position = 4;
        writer.Write(header);
        return stream.ToArray();
    }

    private static byte[] StringBlock(params (int Slot, string Text)[] values)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode);
        for (var i = 0; i < 16; i++)
        {
            var text = values.FirstOrDefault(v => v.Slot == i).Text ?? "";
            writer.Write((ushort)text.Length);
            writer.Write(Encoding.Unicode.GetBytes(text));
        }
        return stream.ToArray();
    }

    private static byte[] IconImage(int size, bool cursor = false)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        if (cursor) { writer.Write((ushort)3); writer.Write((ushort)5); }
        var stride = cursor ? (size + 31) / 32 * 4 : size * 4;
        var mask = (size + 31) / 32 * 4 * size;
        writer.Write(40); writer.Write(size); writer.Write(size * 2);
        writer.Write((ushort)1); writer.Write((ushort)(cursor ? 1 : 32));
        writer.Write(0); writer.Write(stride * size + mask);
        writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        if (cursor) { writer.Write(0); writer.Write(0x00ffffff); }
        writer.Write(new byte[stride * size + mask]);
        return stream.ToArray();
    }

    private static byte[] Group(bool cursor, params (int Size, ushort Id)[] images)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); writer.Write((ushort)(cursor ? 2 : 1)); writer.Write((ushort)images.Length);
        foreach (var (size, id) in images)
        {
            if (cursor) { writer.Write((ushort)size); writer.Write((ushort)(size * 2)); }
            else { writer.Write((byte)size); writer.Write((byte)size); writer.Write((ushort)0); }
            writer.Write((ushort)1); writer.Write((ushort)(cursor ? 1 : 32));
            writer.Write(IconImage(size, cursor).Length); writer.Write(id);
        }
        return stream.ToArray();
    }

    private async Task<BasicInterpreter> ResourceRun(string code, bool absent = false, bool languages = false, bool reverse = false)
    {
        using var strings = new MemoryStream();
        using (var writer = new BinaryWriter(strings, Encoding.Unicode, true))
            for (var i = 0; i < 16; i++)
            {
                var s = i == 5 ? "Hello" : i == 6 ? "café€" : i == 8 ? "x" : "";
                writer.Write((ushort)s.Length);
                writer.Write(Encoding.Unicode.GetBytes(s));
            }
        var entries = new List<byte[]> { Entry((ushort)6, (ushort)7, strings.ToArray()),
            Entry((ushort)10, (ushort)201, [1,2,3,250]), Entry("CUSTOM", (ushort)202, [65,66]),
            Entry("CUSTOM", "NAMED", [9]), Entry((ushort)2, (ushort)301, Dib),
            Entry((ushort)3, (ushort)1, IconImage(16)), Entry((ushort)3, (ushort)2, IconImage(32)),
            Entry((ushort)14, (ushort)401, Group(false, (16, 1), (32, 2))),
            Entry((ushort)14, (ushort)402, Group(false, (16, 1))),
            Entry((ushort)1, (ushort)4, IconImage(32, true)),
            Entry((ushort)12, (ushort)501, Group(true, (32, 4))) };
        if (languages)
        {
            entries.Add(Entry((ushort)6, (ushort)7, StringBlock((5, "Hallo"), (9, "Nur105")), 0x407));
            entries.Add(Entry((ushort)6, (ushort)8, StringBlock((1, "DE113")), 0x407));
            entries.Add(Entry((ushort)10, (ushort)201, [7], 0x407));
            entries.Add(Entry((ushort)2, (ushort)301, Dib, 0x407));
        }
        if (reverse) entries.Reverse();
        var path = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", Guid.NewGuid() + ".res");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllBytes(path, entries.SelectMany(x => x).ToArray());
            var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, code);
            vb.SetAppInfo(new AppInfo(ResourceFilePath: absent ? null : path));
            await vb.Execute();
            return vb;
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("101", "Hello")]
    [InlineData("102", "café€")]
    [InlineData("104", "x")]
    [InlineData("\"101\"", "Hello")]
    [InlineData("101.4", "Hello")]
    public async Task Strings(string id, string expected)
    {
        await ResourceRun($"Debug.Print LoadResString({id})");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task StringStatement()
    {
        await ResourceRun("LoadResString 101\nDebug.Print 1");
        AssertDebugLog([new Vb6Value(1)]);
    }

    [Theory]
    [InlineData("201, 10", "1 2 3 250")]
    [InlineData("201, 10.4", "1 2 3 250")]
    [InlineData("202, \"CUSTOM\"", "65 66")]
    [InlineData("202, \"custom\"", "65 66")]
    [InlineData("\"NAMED\", \"CUSTOM\"", "9")]
    [InlineData("301, 2", "dib")]
    public async Task Data(string args, string bytes)
    {
        await ResourceRun($"Dim a As Variant\na = LoadResData({args})\nDebug.Print TypeName(a)\nDebug.Print LBound(a)\nDebug.Print UBound(a)\nDim i As Long\nFor i = LBound(a) To UBound(a)\nDebug.Print a(i)\nNext");
        var expected = bytes == "dib" ? Dib : bytes.Split(' ').Select(byte.Parse).ToArray();
        AssertDebugLog([new Vb6Value("Byte()"), new Vb6Value(0L), new Vb6Value((long)expected.Length - 1), .. expected.Select(b => new Vb6Value(b))]);
    }

    [Theory]
    [InlineData("LoadResString(103)", false, 326, "103")]
    [InlineData("LoadResString(999)", false, 326, "999")]
    [InlineData("LoadResString(0)", false, 326, "0")]
    [InlineData("LoadResString(-1)", false, 326, "-1")]
    [InlineData("LoadResData(999, 10)", false, 326, "999")]
    [InlineData("LoadResData(201, \"CUSTOM\")", false, 326, "201")]
    [InlineData("LoadResData(7, 6)", false, 326, "7")]
    [InlineData("LoadResPicture(999, 0)", false, 326, "999")]
    [InlineData("LoadResPicture(301, 1)", false, 326, "301")]
    [InlineData("LoadResPicture(301, 5)", false, 485, "")]
    [InlineData("LoadResString(101)", true, 326, "101")]
    [InlineData("LoadResData(201, 10)", true, 326, "201")]
    [InlineData("LoadResPicture(301, 0)", true, 326, "301")]
    public async Task Errors(string call, bool absent, int number, string id)
    {
        var run = () => ResourceRun($"Debug.Print {call}", absent);
        var error = (await run.Should().ThrowAsync<VBRunTimeException>()).Which;
        error.Error.ErrNo.Should().Be(number);
        if (number == 326) error.Error.Description.Should().Be($"Resource with identifier '{id}' not found");
    }

    [Fact]
    public async Task BitmapPicture()
    {
        var vb = await ResourceRun("Dim p As Object\nSet p = LoadResPicture(301, vbResBitmap)\nDebug.Print TypeName(p)");
        debug[0].Should().Be(new Vb6Value("Picture"));
        var result = await vb.BuiltIns.EvaluateBuiltInFunction("LoadResPicture", [new Vb6Value(301), new Vb6Value(0)]);
        var picture = result!.Value.Value.Should().BeOfType<PanelPicture>().Subject;
        picture.Bitmap!.PixelSize.Width.Should().Be(2);
        picture.Bitmap.PixelSize.Height.Should().Be(2);
    }

    [Theory]
    [InlineData("r = LoadResPicture(301, 0)")]
    [InlineData("Debug.Print LoadResPicture(301, 0).Handle")]
    public async Task NativeHandlesRefuse(string code)
    {
        var run = () => ResourceRun(code);
        await run.Should().ThrowAsync<NotImplementedException>();
    }

    [Theory]
    [InlineData("LoadResString(True)")]
    [InlineData("LoadResData(201, True)")]
    public async Task UnmeasuredMembersRefuse(string call)
    {
        var run = () => ResourceRun($"Debug.Print {call}");
        await run.Should().ThrowAsync<NotImplementedException>();
    }

    [Theory]
    [InlineData("LoadResString(id:=101)", "Hello")]
    [InlineData("UBound(LoadResData(id:=201, type:=10))", 3L)]
    [InlineData("UBound(LoadResData(type:=10, id:=201))", 3L)]
    [InlineData("LoadResPicture(id:=301, restype:=0).Type", (short)1)]
    [InlineData("LoadResPicture(restype:=0, id:=301).Type", (short)1)]
    public async Task NamedArguments(string call, object expected)
    {
        await ResourceRun($"Debug.Print {call}");
        AssertDebugLog([expected switch { string text => new Vb6Value(text), short n => new Vb6Value(n), long n => new Vb6Value(n), _ => throw new InvalidOperationException() }]);
    }

    [Theory]
    [InlineData("LoadResString(index:=101)", "*Named argument not found*")]
    [InlineData("LoadResData(index:=201, format:=10)", "*Named argument not found*")]
    [InlineData("LoadResPicture(index:=301, format:=0)", "*Named argument not found*")]
    [InlineData("LoadResData(id:=201)", "*Argument not optional*")]
    [InlineData("LoadResPicture(restype:=0)", "*Argument not optional*")]
    public async Task NamedArgumentErrors(string call, string message)
    {
        var run = () => ResourceRun($"Debug.Print {call}");
        await run.Should().ThrowAsync<Exception>().WithMessage(message);
    }

    [Theory]
    [InlineData(301, 0, (short)1, 53L)]
    [InlineData(401, 1, (short)3, 847L)]
    [InlineData(402, 1, (short)3, 847L)]
    [InlineData(501, 2, (short)3, 847L)]
    public async Task PictureProperties(int id, int kind, short type, long extent)
    {
        await ResourceRun($"Dim p As StdPicture\nSet p = LoadResPicture({id}, {kind})\nDebug.Print TypeName(p)\nDebug.Print p.Type\nDebug.Print p.Width\nDebug.Print p.Height");
        AssertDebugLog([new Vb6Value("Picture"), new Vb6Value(type), new Vb6Value(extent), new Vb6Value(extent)]);
        debug.Select(v => v.Type).Should().Equal(Vb6Value.ValueType.String, Vb6Value.ValueType.Integer, Vb6Value.ValueType.Long, Vb6Value.ValueType.Long);
    }

    [Theory]
    // Measured shapes: docs/vb6-fidelity-oracle/an-icon-pictures-size-and-the-image-it-draws.md (#3969).
    [InlineData(0, 53L)]
    [InlineData(1, 423L)]
    [InlineData(2, 847L)]
    public async Task LoadPictureSharesTypedProperties(int imageCount, long extent)
    {
        byte[] file;
        var icon = imageCount != 0;
        if (icon)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)imageCount);
            var offset = 6 + imageCount * 16;
            for (var i = 0; i < imageCount; i++)
            {
                var size = i == 0 ? 16 : 32;
                var image = IconImage(size);
                writer.Write((byte)size); writer.Write((byte)size); writer.Write((ushort)0);
                writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(image.Length); writer.Write(offset);
                offset += image.Length;
            }
            for (var i = 0; i < imageCount; i++) writer.Write(IconImage(i == 0 ? 16 : 32));
            file = stream.ToArray();
        }
        else file = PanelPicture.FromResourceBitmap(Dib).FileBytes;
        var path = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", Guid.NewGuid() + (icon ? ".ico" : ".bmp"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllBytes(path, file);
            await Run($"Dim p As StdPicture\nSet p = LoadPicture(\"{path}\")\nDebug.Print p.Type\nDebug.Print p.Width\nDebug.Print p.Height");
            AssertDebugLog([new Vb6Value((short)(icon ? 3 : 1)), new Vb6Value(extent), new Vb6Value(extent)]);
            debug.Select(v => v.Type).Should().Equal(Vb6Value.ValueType.Integer, Vb6Value.ValueType.Long, Vb6Value.ValueType.Long);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task IconGroupSelects32PixelImage()
    {
        // #3969 measured group 401 drawing its 32-pixel image, rather than stretching its 16-pixel one.
        var vb = await ResourceRun("Set p = LoadResPicture(401, vbResIcon)");
        var result = await vb.BuiltIns.EvaluateBuiltInFunction("LoadResPicture", [new Vb6Value(401), new Vb6Value(1)]);
        var picture = result!.Value.Value.Should().BeOfType<PanelPicture>().Subject;
        picture.Bitmap!.PixelSize.Should().Be(new Avalonia.PixelSize(32, 32));
        // Decode the selected source without resource scaling, so a stretched 16-pixel choice cannot pass.
        PanelPicture.FromFile(picture.FileBytes).Bitmap!.PixelSize.Should().Be(new Avalonia.PixelSize(32, 32));
    }

    [Theory]
    [InlineData("401, 0")]
    [InlineData("501, 1")]
    [InlineData("401, 2")]
    [InlineData("\"401\", 1")]
    [InlineData("2, 1")]
    public async Task WrongPictureKindOrName(string args)
    {
        var run = () => ResourceRun($"Set p = LoadResPicture({args})");
        (await run.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(326);
    }

    [Theory]
    [InlineData("401, 14", "group")]
    [InlineData("2, 3", "icon")]
    [InlineData("501, 12", "cursorGroup")]
    [InlineData("4, 1", "cursor")]
    public async Task RawIconAndCursorData(string args, string fixture)
    {
        var expected = fixture switch
        {
            "group" => Group(false, (16, 1), (32, 2)), "icon" => IconImage(32),
            "cursorGroup" => Group(true, (32, 4)), _ => IconImage(32, true)
        };
        await ResourceRun($"a = LoadResData({args})\nDebug.Print TypeName(a)\nDebug.Print LBound(a)\nDebug.Print UBound(a)\nFor i = LBound(a) To UBound(a)\nDebug.Print a(i)\nNext");
        AssertDebugLog([new Vb6Value("Byte()"), new Vb6Value(0L), new Vb6Value((long)expected.Length - 1), .. expected.Select(b => new Vb6Value(b))]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProgramLanguageSelectsBlockBeforeString(bool reverse)
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo("en-GB"));
        await ResourceRun("Debug.Print LoadResString(101)\nDebug.Print LoadResString(113)\nDebug.Print UBound(LoadResData(201, 10))\nSet p = LoadResPicture(301, 0)\nDebug.Print p.Width", languages: true, reverse: reverse);
        AssertDebugLog([new Vb6Value("Hello"), new Vb6Value("DE113"), new Vb6Value(3L), new Vb6Value(53L)]);
    }

    [Fact]
    public async Task EmptyEntryDoesNotFallBackToAnotherLanguage()
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo("en-GB"));
        var run = () => ResourceRun("Debug.Print LoadResString(105)", languages: true);
        (await run.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(326);
    }
}
