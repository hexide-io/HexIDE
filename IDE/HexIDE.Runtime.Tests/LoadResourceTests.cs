using System.Text;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

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

    private async Task<BasicInterpreter> ResourceRun(string code, bool absent = false, bool languages = false)
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
            Entry((ushort)14, (ushort)302, [0]), Entry((ushort)12, (ushort)303, [0]) };
        if (languages) entries.Add(Entry((ushort)10, (ushort)201, [7], 0x407));
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
    [InlineData("Debug.Print LoadResPicture(301, 0).Type")]
    [InlineData("Debug.Print LoadResPicture(301, 0).Width")]
    [InlineData("Debug.Print LoadResPicture(301, 0).Height")]
    [InlineData("Debug.Print LoadResPicture(301, 0).Handle")]
    public async Task PictureMembersWithoutAMeasuredTypeOrNativeHandleRefuse(string code)
    {
        var run = () => ResourceRun(code);
        await run.Should().ThrowAsync<NotImplementedException>();
    }

    [Theory]
    [InlineData("LoadResPicture(302, 1)")]
    [InlineData("LoadResPicture(303, 2)")]
    [InlineData("LoadResString(True)")]
    [InlineData("LoadResData(201, True)")]
    public async Task UnmeasuredMembersRefuse(string call)
    {
        var run = () => ResourceRun($"Debug.Print {call}");
        await run.Should().ThrowAsync<NotImplementedException>();
    }

    [Theory]
    [InlineData("LoadResString(id:=101)")]
    [InlineData("LoadResData(id:=201, type:=10)")]
    [InlineData("LoadResPicture(id:=301, type:=0)")]
    public async Task UnmeasuredNamedArgumentsRefuse(string call)
    {
        var run = () => ResourceRun($"Debug.Print {call}");
        await run.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public async Task MultipleLanguagesRefuse()
    {
        var run = () => ResourceRun("Debug.Print LoadResData(201, 10)", languages: true);
        await run.Should().ThrowAsync<NotImplementedException>();
    }
}
