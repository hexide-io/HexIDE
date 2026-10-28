using System;
using System.Threading.Tasks;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

// VB6 SP6 native and p-code: oracle "In-box controls: the CommonDialog", dialog results (#1084).
public class CommonDialogFileResultTests
{
    private static VBCommonDialog Dialog(VbFileDialogAnswer? answer)
    {
        var host = Substitute.For<IBasicStandardLibrary>();
        host.ShowFileDialog(Arg.Any<VbFileDialogRequest>()).Returns(Task.FromResult(answer));
        return new VBCommonDialog(host);
    }

    private static Vb6Value Read(VBCommonDialog dialog, string name)
    {
        dialog.TryGetProperty(name, out var value).Should().BeTrue();
        return value;
    }

    [Theory]
    [InlineData(0x80200, "C:\\hexoracle-cdfiles\0a.txt\0b.txt")]
    [InlineData(0x200, @"C:\hexoracle-cdfiles\ a.txt b.txt")]
    public async Task Multiple_picks_use_the_measured_separator_and_folder_suffix(int flags, string expected)
    {
        var host = Substitute.For<IBasicStandardLibrary>();
        host.ShowFileDialog(Arg.Any<VbFileDialogRequest>()).Returns(Task.FromResult<VbFileDialogAnswer?>(
            new VbFileDialogAnswer(@"C:\hexoracle-cdfiles\a.txt", null, [@"C:\hexoracle-cdfiles\b.txt"])));
        var dialog = new VBCommonDialog(host);
        dialog.TrySetProperty("Flags", new Vb6Value((long)flags));
        await dialog.CallAsync("ShowOpen", []);
        Read(dialog, "FileName").Should().Be(new Vb6Value(expected));
        Read(dialog, "FileTitle").Should().Be(new Vb6Value(""));
        await host.Received(1).ShowFileDialog(Arg.Is<VbFileDialogRequest>(r =>
            r.AllowMultiple && VbFileDialogOptions.Open(r, null).AllowMultiple));
    }

    [Theory]
    [InlineData(@"C:\other\b.txt", 0x80200)]
    [InlineData(@"C:\hexoracle-cdfiles\long name.txt", 0x200)]
    public async Task Unmeasured_multiple_pick_shapes_are_refused(string second, int flags)
    {
        var dialog = Dialog(new VbFileDialogAnswer(@"C:\hexoracle-cdfiles\a.txt", null, [second]));
        dialog.TrySetProperty("Flags", new Vb6Value((long)flags));
        Func<Task> show = async () => await dialog.CallAsync("ShowOpen", []);
        await show.Should().ThrowAsync<NotImplementedException>().WithMessage("*multi-select*");
        Read(dialog, "FileName").Should().Be(new Vb6Value(""));
    }

    [Fact]
    public async Task Explorer_multiselect_with_one_pick_keeps_plain_path_title_and_flags()
    {
        var dialog = Dialog(new VbFileDialogAnswer(@"C:\hexoracle-cdfiles\a.txt", null));
        dialog.TrySetProperty("Flags", new Vb6Value(0x80200L));
        await dialog.CallAsync("ShowOpen", []);
        Read(dialog, "FileName").Should().Be(new Vb6Value(@"C:\hexoracle-cdfiles\a.txt"));
        Read(dialog, "FileTitle").Should().Be(new Vb6Value("a.txt"));
        Read(dialog, "Flags").Should().Be(new Vb6Value(0x80600L));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_writes_implied_flags_before_returning_or_raising(bool cancelError)
    {
        var dialog = Dialog(null);
        dialog.TrySetProperty("Flags", new Vb6Value(0x1000L));
        dialog.TrySetProperty("FileName", new Vb6Value("keep.txt"));
        dialog.TrySetProperty("CancelError", new Vb6Value(cancelError));
        Func<Task> show = async () => await dialog.CallAsync("ShowOpen", []);
        if (cancelError)
            (await show.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(32755);
        else
            await show();
        Read(dialog, "Flags").Should().Be(new Vb6Value(0x1800L));
        Read(dialog, "FileName").Should().Be(new Vb6Value("keep.txt"));
        Read(dialog, "FileTitle").Should().Be(new Vb6Value(""));
    }

    [Theory]
    [InlineData("ShowOpen", "", "a.txt", "a.txt", 0x400)]
    [InlineData("ShowOpen", "txt", "c", "c.txt", 0)]
    [InlineData("ShowOpen", "txt", "c.bas", "c.bas", 0x400)]
    [InlineData("ShowSave", "", "e", "e.txt", 0x400)]
    [InlineData("ShowSave", "txt", "d", "d.txt", 0)]
    public async Task Picks_complete_extensions_and_report_ExtensionDifferent(
        string method, string extension, string picked, string expected, int flags)
    {
        var dialog = Dialog(new VbFileDialogAnswer(@"C:\hexoracle-cdfiles\" + picked, 1));
        dialog.TrySetProperty("Filter", new Vb6Value("Text (*.txt)|*.txt"));
        dialog.TrySetProperty("DefaultExt", new Vb6Value(extension));
        await dialog.CallAsync(method, []);
        Read(dialog, "FileName").Should().Be(new Vb6Value(@"C:\hexoracle-cdfiles\" + expected));
        Read(dialog, "FileTitle").Should().Be(new Vb6Value(expected));
        Read(dialog, "Flags").Should().Be(new Vb6Value((long)flags));
    }

    [Theory]
    [InlineData("Text (*.txt)|*.txt|Basic (*.bas)|*.bas")]
    [InlineData("Mixed|*.txt;*.bas")]
    public async Task Save_without_extension_refuses_an_unknown_or_unmeasured_filter(string filter)
    {
        var dialog = Dialog(new VbFileDialogAnswer(@"C:\hexoracle-cdfiles\e", null));
        dialog.TrySetProperty("Filter", new Vb6Value(filter));
        Func<Task> show = async () => await dialog.CallAsync("ShowSave", []);
        await show.Should().ThrowAsync<NotImplementedException>().WithMessage("*extension*filter*");
    }

    [Fact]
    public async Task Too_small_file_buffer_raises_20476()
    {
        var dialog = Dialog(new VbFileDialogAnswer(@"C:\hexoracle-cdfiles\a.txt", 1));
        dialog.TrySetProperty("MaxFileSize", new Vb6Value(10));
        Func<Task> show = async () => await dialog.CallAsync("ShowOpen", []);
        var error = (await show.Should().ThrowAsync<VBRunTimeException>()).Which.Error;
        error.ErrNo.Should().Be(20476);
    }

    [Fact]
    public async Task Font_with_zero_flags_opens_and_cancel_raises_32755()
    {
        var host = Substitute.For<IBasicStandardLibrary>();
        host.ShowFontDialog(Arg.Any<VbFontChoice>()).Returns(Task.FromResult<VbFontChoice?>(null));
        var dialog = new VBCommonDialog(host);
        dialog.TrySetProperty("CancelError", new Vb6Value(true));
        Func<Task> show = async () => await dialog.CallAsync("ShowFont", []);
        (await show.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(32755);
        await host.Received(1).ShowFontDialog(new VbFontChoice("", 8, false, false));
    }
}
