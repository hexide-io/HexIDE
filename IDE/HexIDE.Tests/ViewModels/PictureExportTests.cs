using System.Buffers.Binary;
using Avalonia.Platform.Storage;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.ViewModels;

public class PictureExportTests
{
    private static byte[] Record(byte[] payload)
    {
        var bytes = new byte[payload.Length + 12];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, payload.Length + 8);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 0x746c);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), payload.Length);
        payload.CopyTo(bytes, 12);
        return bytes;
    }

    private static PropertyViewModel Row(IWindowManager windows, PropertyClass property, object? bytes) =>
        new(new PropertiesToolViewModel(Substitute.For<IDocumentDockService>(), Substitute.For<IEventBus>(),
            windows, Substitute.For<ILocalizationService>()), property, bytes);

    [Theory]
    [InlineData("Picture")]
    [InlineData("Icon")]
    [InlineData("MouseIcon")]
    [InlineData("DragIcon")]
    public async Task ExportWritesOnlyThePayloadAndLeavesTheRecordUntouched(string name)
    {
        byte[] payload = [0, 0, 1, 0, 42, 99];
        var bytes = Record(payload);
        var before = bytes.ToArray();
        var property = name switch
        {
            "Icon" => VBProperties.IconProperty,
            "MouseIcon" => VBProperties.MouseIconProperty,
            "DragIcon" => VBProperties.DragIconProperty,
            _ => VBProperties.PictureProperty,
        };
        var windows = Substitute.For<IWindowManager>();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ico");
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(path);
        try
        {
            var row = Row(windows, property, bytes);
            row.CanExportPicture.Should().BeTrue();
            await row.ExportPictureCommand.ExecuteAsync(null);
            File.ReadAllBytes(path).Should().Equal(payload);
            bytes.Should().Equal(before);
            row.Value.Should().BeSameAs(bytes);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CancelWritesNothing()
    {
        var windows = Substitute.For<IWindowManager>();
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns((string?)null);
        var row = Row(windows, VBProperties.PictureProperty, Record([42]));
        await row.ExportPictureCommand.ExecuteAsync(null);
        await windows.Received(1).SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>());
        await windows.DidNotReceiveWithAnyArgs().MessageBox(default!);
    }

    [Fact]
    public async Task WriteFailureIsReported()
    {
        var windows = Substitute.For<IWindowManager>();
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(Path.GetTempPath());
        var row = Row(windows, VBProperties.PictureProperty, Record([42]));
        await row.ExportPictureCommand.ExecuteAsync(null);
        await windows.ReceivedWithAnyArgs(1).MessageBox(default!);
    }

    [Fact]
    public async Task OpaqueAndEmptyValuesDoNotOpenAPicker()
    {
        var windows = Substitute.For<IWindowManager>();
        var changing = Row(windows, VBProperties.PictureProperty, Record([42]));
        changing.CanExportPicture.Should().BeTrue();
        changing.UpdateValueNoRaise(null);
        changing.CanExportPicture.Should().BeFalse();
        changing.ExportPictureCommand.CanExecute(null).Should().BeFalse();
        foreach (var value in new object?[] { null, Array.Empty<byte>(), new byte[] { 42 }, Record([]), Record([42])[4..] })
        {
            var row = Row(windows, VBProperties.PictureProperty, value);
            row.CanExportPicture.Should().BeFalse();
            await row.ExportPictureCommand.ExecuteAsync(null);
        }
        var malformed = Record([42]);
        malformed[8] = 2;
        var malformedRow = Row(windows, VBProperties.PictureProperty, malformed);
        malformedRow.CanExportPicture.Should().BeFalse();
        await malformedRow.ExportPictureCommand.ExecuteAsync(null);
        var other = Row(windows, VBProperties.CaptionProperty, Record([42]));
        other.CanExportPicture.Should().BeFalse();
        await other.ExportPictureCommand.ExecuteAsync(null);
        await windows.DidNotReceiveWithAnyArgs().SaveFilePickerAsync(default!);
    }

    [AvaloniaFact]
    public void FramedPictureBoxImageDecodes()
    {
        // A 1x1 BMP, carried inside the two measured framing layers.
        byte[] bmp = new byte[58];
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M'; bmp[2] = 58; bmp[10] = 54;
        bmp[14] = 40; bmp[18] = 1; bmp[22] = 1; bmp[26] = 1; bmp[28] = 24;
        using var raw = FrxImageHelper.TryDecodeToAvaloniaBitmap(bmp);
        raw.Should().NotBeNull();
        using var framed = FrxImageHelper.TryDecodeToAvaloniaBitmap(Record(bmp));
        framed.Should().NotBeNull();
        framed!.PixelSize.Should().Be(raw!.PixelSize);
    }
}
