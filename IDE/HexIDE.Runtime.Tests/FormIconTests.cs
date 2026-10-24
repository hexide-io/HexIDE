using System.Linq;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// What a form's file says about its window's icon (#1594): nothing, (None), or an icon of its own. Measured on vb6.exe,
/// native and p-code agreeing: no Icon line gives VB6's default icon, big only; (None) gives no icon at all; an icon of
/// the form's own is both its big and small icon (the oracle's "A form's window icon"). VB98's dialog templates save
/// (None) as an empty picture: a length of 8, the StdPicture preamble, and a size of 0.
/// </summary>
public class FormIconTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static readonly byte[] Preamble = [0x6C, 0x74, 0, 0];

    // The .frx record VB98's Splash Screen, Options Dialog and ODBC Log In save for Icon = (None).
    private static readonly byte[] NoneRecord = [8, 0, 0, 0, .. Preamble, 0, 0, 0, 0];

    private static readonly byte[] Image = [0, 0, 1, 0, 1, 0, 9, 8, 7];

    private static byte[] Record(byte[] image) =>
        [.. BitConverter.GetBytes(image.Length + 8), .. Preamble, .. BitConverter.GetBytes(image.Length), .. image];

    private static FormIcon Read(byte[]? blob)
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   ClientHeight    =   1500\r\n   ClientWidth     =   2520\r\nEnd\r\n"
            + "Attribute VB_Name = \"Form1\"\r\n", new NullSink())!;
        var root = form.Components.First(c => c.BaseClass is FormComponentClass);
        if (blob is not null)
            root.SetProperty(VBProperties.IconProperty, blob);
        return FormIcon.Read(root);
    }

    [Fact]
    public void A_form_with_no_Icon_line_has_the_default() =>
        Read(null).Kind.Should().Be(FormIconKind.Default);

    [Fact]
    public void An_empty_picture_is_None() =>
        Read(NoneRecord).Kind.Should().Be(FormIconKind.None);

    // A blob runs from the offset the form cites to the next it cites, or the file's end, so (None) can be followed by
    // another record. The picture's own size says where it ends. The live check of #1594 read a 790-byte blob of this
    // shape as an icon, before the size was read.
    [Fact]
    public void An_empty_picture_followed_by_another_record_is_still_None() =>
        Read([.. NoneRecord, .. Record(Image)]).Kind.Should().Be(FormIconKind.None);

    [Fact]
    public void A_picture_with_bytes_is_the_forms_own_icon_and_only_those_bytes()
    {
        var icon = Read([.. Record(Image), .. NoneRecord]);

        icon.Kind.Should().Be(FormIconKind.Own);
        icon.Image.Should().Equal(Image);
    }

    // As VBImage takes a Picture: a blob with no framing at all is taken to be the image itself.
    [Fact]
    public void Bytes_with_no_framing_are_taken_as_the_image()
    {
        var icon = Read(Image);

        icon.Kind.Should().Be(FormIconKind.Own);
        icon.Image.Should().Equal(Image);
    }
}
