using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using HexIDE.Integration.Tests;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Runtime.Integration.Tests.Views;

// VB6 SP6: #992, https://github.com/hexide-io/HexIDE/issues/992#issuecomment-7094565387.
// TestData/SavePicture contains that comment's original inputs and VB6 outputs, without alteration.
// Errors: docs/vb6-fidelity-oracle/loadpicture-file-kinds-and-file-errors.md; names: named-arguments.md.
public class SavePictureTests : WindowTestFixture, IDisposable
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.PictureBox Picture1
              Width = 1200
              Height = 600
           End
           Begin VB.Image Image1
              Width = 1200
              Height = 600
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static string Data => Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Runtime.Integration.Tests", "TestData", "SavePicture");
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(Data, name));
    private static string Literal(string path) => "\"" + path.Replace("\"", "\"\"") + "\"";
    private readonly string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "savepicture-" + Guid.NewGuid())).FullName;
    public new void Dispose()
    {
        base.Dispose();
        Directory.Delete(folder, true);
    }

    [Fact]
    public void FixturesMatchMeasuredHashes()
    {
        foreach (var line in File.ReadAllLines(Path.Combine(Data, "SHA256SUMS")))
        {
            var parts = line.Split("  ");
            Convert.ToHexStringLower(SHA256.HashData(Fixture(parts[1]))).Should().Be(parts[0]);
        }
    }

    [AvaloniaTheory]
    [InlineData("x24.bmp")]
    [InlineData("x.bmp")]
    [InlineData("x8.bmp")]
    [InlineData("grey8.bmp")]
    [InlineData("x.gif")]
    [InlineData("x.jpg")]
    [InlineData("x.ico")]
    public async Task WritesMeasuredFile(string source)
    {
        var output = Path.Combine(folder, "saved.out");
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        await ContainerRuntimeHarness.Run(ctx, env, $"SavePicture LoadPicture({Literal(Path.Combine(Data, source))}), {Literal(output)}");
        var expected = Fixture("o_" + source + ".out");
        // The JPEG too: HexIDE's decoded pixels matched VB6's output byte for byte, on Windows and Linux (#992 review).
        File.ReadAllBytes(output).Should().Equal(expected);
    }

    [AvaloniaTheory]
    [InlineData("SavePicture Picture:=p, FileName:=f")]
    [InlineData("SavePicture FileName:=f, Picture:=p")]
    [InlineData("SavePicture Picture1.Picture, f")]
    public async Task NamedAndControlPicturesWriteBmpRegardlessOfExtension(string statement)
    {
        var output = Path.Combine(folder, "saved.jpg");
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        await ContainerRuntimeHarness.Run(ctx, env, $"""
            Dim p As Object
            Dim f As String
            f = {Literal(output)}
            Set p = LoadPicture({Literal(Path.Combine(Data, "x24.bmp"))})
            Set Picture1.Picture = p
            {statement}
            """);
        File.ReadAllBytes(output).Should().Equal(Fixture("o_x24.bmp.out"));
    }

    [AvaloniaTheory]
    [InlineData("LoadPicture()")]
    [InlineData("Picture1.Picture")]
    public async Task EmptyPictureRaises380WithoutWriting(string expression)
    {
        var output = Path.Combine(folder, "empty.bmp");
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env, $"""
            On Error Resume Next
            SavePicture {expression}, {Literal(output)}
            Debug.Print Err.Number
            Debug.Print Err.Description
            """);
        values.Select(v => v.Value).Should().Equal(380L, "Invalid property value");
        File.Exists(output).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task MissingFolderRaises76WithOriginalPath()
    {
        var output = Path.Combine(folder, "nodir", "saved.bmp");
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env, $"""
            On Error Resume Next
            SavePicture LoadPicture({Literal(Path.Combine(Data, "x24.bmp"))}), {Literal(output)}
            Debug.Print Err.Number
            Debug.Print Err.Description
            """);
        values.Select(v => v.Value).Should().Equal(76L, $"Path not found: '{output}'");
        File.Exists(output).Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData("depth1")]
    [InlineData("depth4")]
    [InlineData("depth16")]
    [InlineData("compressed")]
    [InlineData("topDown")]
    [InlineData("shortTable")]
    [InlineData("localTable")]
    [InlineData("usedTransparency")]
    [InlineData("noTransparency")]
    [InlineData("lateControl")]
    [InlineData("multipleControl")]
    [InlineData("interlaced")]
    [InlineData("multipleImages")]
    [InlineData("smallGlobalTable")]
    [InlineData("iconSize")]
    [InlineData("iconCount")]
    public async Task UnmeasuredLayoutsRefuseBeforeWriting(string row)
    {
        var bytes = Fixture(row.StartsWith("icon", StringComparison.Ordinal) ? "x.ico"
            : row is "depth1" or "depth4" or "depth16" or "compressed" or "topDown" or "shortTable" ? "x8.bmp" : "x.gif");
        switch (row)
        {
            case "depth1": bytes[28] = 1; break;
            case "depth4": bytes[28] = 4; break;
            case "depth16": bytes[28] = 16; break;
            case "compressed": bytes[30] = 1; break;
            case "topDown": BitConverter.GetBytes(-10).CopyTo(bytes, 22); break;
            case "shortTable": BitConverter.GetBytes(16).CopyTo(bytes, 46); break;
            case "localTable": bytes[798] |= 0x80; break;
            case "usedTransparency": bytes[787] = 210; break;
            case "noTransparency": bytes[784] = 0; break;
            case "lateControl": bytes = [.. bytes[..781], .. bytes[789..^1], .. bytes[781..789], 0x3b]; break;
            case "multipleControl": bytes = [.. bytes[..789], .. bytes[781..789], .. bytes[789..]]; break;
            case "interlaced": bytes[798] |= 0x40; break;
            case "multipleImages": bytes = [.. bytes[..^1], .. bytes[789..^1], 0x3b]; break;
            case "smallGlobalTable": bytes[10] = 0xf6; break;
            case "iconSize": bytes[6] = 16; break;
            case "iconCount": bytes[4] = 2; break;
        }
        var output = Path.Combine(folder, "refused.bmp");
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        // Bypass LoadPicture's independent decoder refusals so each unsupported source reaches SavePicture itself.
        ctx.AllocVariable(env, "p", new Vb6Value(new PanelPicture(bytes)));
        var run = () => ContainerRuntimeHarness.Run(ctx, env, $"SavePicture p, {Literal(output)}");
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*(#992)");
        File.Exists(output).Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData("p")]
    [InlineData("Picture1.Picture")]
    [InlineData("Image1.Picture")]
    public async Task LoadPictureSelectionDoesNotMakeMultiImageIconMeasured(string expression)
    {
        var icon = Fixture("x.ico");
        var entry = icon[6..22];
        var data = icon[22..];
        BitConverter.GetBytes(38).CopyTo(entry, 12);
        var second = (byte[])entry.Clone();
        BitConverter.GetBytes(38 + data.Length).CopyTo(second, 12);
        byte[] multiple = [0, 0, 1, 0, 2, 0, .. entry, .. second, .. data, .. data];
        var input = Path.Combine(folder, "multiple.ico");
        File.WriteAllBytes(input, multiple);
        var output = Path.Combine(folder, "refused.ico");
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var run = () => ContainerRuntimeHarness.Run(ctx, env, $"""
            Dim p As Object
            Set p = LoadPicture({Literal(input)})
            Set Picture1.Picture = p
            Set Image1.Picture = p
            SavePicture {expression}, {Literal(output)}
            """);
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*(#992)");
        File.Exists(output).Should().BeFalse();
    }

    // SP6, #992 comments “SavePicture to a file that already exists” and “the 75 message names the path as given” (2026-11-23).
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingPlainDestinationIsRewrittenInPlace(bool relative)
    {
        var output = Path.Combine(folder, "existing.bmp");
        File.WriteAllBytes(output, Enumerable.Repeat((byte)'X', 2000).ToArray());
        var oldWrite = new DateTime(2002, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(output, oldWrite);
        if (OperatingSystem.IsWindows())
            File.SetCreationTimeUtc(output, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var creation = File.GetCreationTimeUtc(output);
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        await ContainerRuntimeHarness.Run(ctx, env, $"""
            {ChangeDirectory(relative)}
            SavePicture LoadPicture({Literal(Path.Combine(Data, "x24.bmp"))}), {Literal(relative ? "existing.bmp" : output)}
            """);
        File.ReadAllBytes(output).Should().Equal(Fixture("o_x24.bmp.out"));
        File.GetLastWriteTimeUtc(output).Should().BeAfter(oldWrite);
        if (OperatingSystem.IsWindows())
        {
            File.GetCreationTimeUtc(output).Should().Be(creation);
            File.GetAttributes(output).Should().Be(FileAttributes.Archive);
        }
    }

    private string ChangeDirectory(bool relative) => !relative ? "" :
        (OperatingSystem.IsWindows() ? $"ChDrive {Literal(folder)}\n" : "") + $"ChDir {Literal(folder)}";

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ExistingReadOnlyDestinationRaises75(bool relative) =>
        ProtectedDestinationRaises75(FileAttributes.ReadOnly, relative);

    [AvaloniaFact]
    public async Task ExistingHiddenDestinationRaises75()
    {
        if (!OperatingSystem.IsWindows()) throw Xunit.Sdk.SkipException.ForSkip("Requires Windows hidden file attributes");
        await ProtectedDestinationRaises75(FileAttributes.Hidden, true);
    }

    private async Task ProtectedDestinationRaises75(FileAttributes attributes, bool relative)
    {
        var output = Path.Combine(folder, "existing.bmp");
        var path = relative ? (attributes == FileAttributes.Hidden ? @"sub\..\existing.bmp" : "existing.bmp") : output;
        var original = Enumerable.Repeat((byte)'X', 2000).ToArray();
        File.WriteAllBytes(output, original);
        if (OperatingSystem.IsWindows())
            File.SetCreationTimeUtc(output, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(output, new DateTime(2002, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetAttributes(output, attributes);
        var creation = File.GetCreationTimeUtc(output);
        var lastWrite = File.GetLastWriteTimeUtc(output);
        var originalAttributes = File.GetAttributes(output);
        try
        {
            var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
            var values = await ContainerRuntimeHarness.Run(ctx, env, $"""
                {ChangeDirectory(relative)}
                On Error Resume Next
                SavePicture LoadPicture({Literal(Path.Combine(Data, "x24.bmp"))}), {Literal(path)}
                Debug.Print Err.Number
                Debug.Print Err.Description
                """);
            values.Select(v => v.Value).Should().Equal(75L, $"Path/File access error: '{path}'");
            File.ReadAllBytes(output).Should().Equal(original);
            File.GetCreationTimeUtc(output).Should().Be(creation);
            File.GetLastWriteTimeUtc(output).Should().Be(lastWrite);
            File.GetAttributes(output).Should().Be(originalAttributes);
        }
        finally { File.SetAttributes(output, FileAttributes.Normal); }
    }

    [AvaloniaFact]
    public async Task OtherWriteErrorsRefuse()
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var run = () => ContainerRuntimeHarness.Run(ctx, env,
            $"SavePicture LoadPicture({Literal(Path.Combine(Data, "x24.bmp"))}), {Literal(folder)}");
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*(#992)");
        Directory.GetFiles(folder).Should().BeEmpty();
    }

    // SP6: docs/vb6-fidelity-oracle/controls-add-and-controls-remove.md, every property of a removed control is 735.
    // This is already correct on main; the Image refusal must not take precedence over it.
    [AvaloniaFact]
    public async Task RemovedControlKeeps735BeforeImageRefusal()
    {
        var (_, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        VBProps.SetRemoved(ContainerRuntimeHarness.Child(canvas, "Picture1"), true);
        var values = await ContainerRuntimeHarness.Run(ctx, env, """
            On Error Resume Next
            Debug.Print Picture1.Image
            Debug.Print Err.Number
            """);
        values.Select(v => v.Value).Should().Equal(735L);
    }

    [AvaloniaFact]
    public async Task ImageStillRefuses()
    {
        var output = Path.Combine(folder, "image.bmp");
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var run = () => ContainerRuntimeHarness.Run(ctx, env, $"SavePicture Picture1.Image, {Literal(output)}");
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*#992*");
        File.Exists(output).Should().BeFalse();
    }
}
