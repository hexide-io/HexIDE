using System;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// VB6 SP6, native and p-code: oracle "In-box controls: the CommonDialog", object-model tables (#1084).
public class CommonDialogObjectModelTests : BaseVBTestFixture
{
    private sealed class DialogHost : IBasicStandardLibrary
    {
        public Task<VbFileDialogAnswer?> Answer { get; init; } = Task.FromResult<VbFileDialogAnswer?>(null);
        public VbFileDialogRequest? Request { get; private set; }
        public Task<VbFontChoice?> ShowFontDialog(VbFontChoice initial) =>
            Task.FromResult<VbFontChoice?>(null);
        public Task<VbFileDialogAnswer?> ShowFileDialog(VbFileDialogRequest request)
        {
            Request = request;
            return Answer;
        }
        public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon) =>
            throw new NotImplementedException();
        public Task<string?> InputBox(string prompt, string? title, string defaultText) => throw new NotImplementedException();
        public void DebugPrint(Vb6Value value) => throw new NotImplementedException();
    }

    [Fact]
    public async Task Action_one_awaits_Open_and_returns_its_pick()
    {
        var answer = new TaskCompletionSource<VbFileDialogAnswer?>();
        var host = new DialogHost { Answer = answer.Task };
        var dialog = new VBCommonDialog(host);
        context.AllocVariable(rootEnv, "dlg", new Vb6Value(dialog));
        var run = Run("dlg.Action = 1\nDebug.Print dlg.FileTitle");
        run.IsCompleted.Should().BeFalse();
        answer.SetResult(new VbFileDialogAnswer(@"C:\hexoracle-cdfiles\a.txt", null));
        await run;
        AssertDebugLog([new Vb6Value("a.txt")]);
        host.Request.Should().NotBeNull();
        host.Request!.Save.Should().BeFalse();
    }

    [Fact]
    public async Task Action_two_awaits_Save_and_completes_DefaultExt()
    {
        var answer = new TaskCompletionSource<VbFileDialogAnswer?>();
        var host = new DialogHost { Answer = answer.Task };
        var dialog = new VBCommonDialog(host);
        dialog.TrySetProperty("DefaultExt", new Vb6Value("txt"));
        context.AllocVariable(rootEnv, "dlg", new Vb6Value(dialog));
        var run = Run("dlg.Action = 2\nDebug.Print dlg.FileName\nDebug.Print dlg.FileTitle");
        run.IsCompleted.Should().BeFalse();
        answer.SetResult(new VbFileDialogAnswer(@"C:\hexoracle-cdfiles\d", null));
        await run;
        AssertDebugLog([new Vb6Value(@"C:\hexoracle-cdfiles\d.txt"), new Vb6Value("d.txt")]);
        host.Request!.Save.Should().BeTrue();
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public async Task Action_Save_and_Font_cancel_use_CancelError(int action, bool cancelError)
    {
        var dialog = new VBCommonDialog(new DialogHost());
        dialog.TrySetProperty("CancelError", new Vb6Value(cancelError));
        context.AllocVariable(rootEnv, "dlg", new Vb6Value(dialog));
        await Run($"On Error Resume Next\ndlg.Action = {action}\nDebug.Print Err.Number");
        AssertDebugLog([new Vb6Value(cancelError ? 32755L : 0L)]);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(2, "")]
    [InlineData(3, "")]
    [InlineData(0, "help.hlp")]
    [InlineData(2, "help.hlp")]
    [InlineData(3, "help.hlp")]
    public async Task Action_six_and_ShowHelp_return_without_error(int command, string file)
    {
        var dialog = new VBCommonDialog(null!);
        dialog.TrySetProperty("HelpCommand", new Vb6Value(command));
        dialog.TrySetProperty("HelpFile", new Vb6Value(file));
        dialog.TrySetProperty("CancelError", new Vb6Value(true));
        context.AllocVariable(rootEnv, "dlg", new Vb6Value(dialog));
        await Run("On Error Resume Next\ndlg.Action = 6\ndlg.ShowHelp\nDebug.Print Err.Number");
        AssertDebugLog([new Vb6Value(0L)]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(-1)]
    public async Task Invalid_Action_writes_raise_380(int action)
    {
        context.AllocVariable(rootEnv, "dlg", new Vb6Value(new VBCommonDialog(null!)));
        await Run($"On Error Resume Next\ndlg.Action = {action}\nDebug.Print Err.Number");
        AssertDebugLog([new Vb6Value(380L)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Action_Open_cancel_uses_CancelError(bool cancelError)
    {
        var host = new DialogHost();
        var dialog = new VBCommonDialog(host);
        dialog.TrySetProperty("CancelError", new Vb6Value(cancelError));
        dialog.TrySetProperty("FileName", new Vb6Value("keep.txt"));
        context.AllocVariable(rootEnv, "dlg", new Vb6Value(dialog));
        await Run("On Error Resume Next\ndlg.Action = 1\nDebug.Print Err.Number\nDebug.Print dlg.FileName");
        AssertDebugLog([new Vb6Value(cancelError ? 32755L : 0L), new Vb6Value("keep.txt")]);
    }

    [Theory]
    [InlineData("Dim n As Long\nn = dlg.Color")]
    [InlineData("Debug.Print CDbl(dlg.Color)")]
    [InlineData("Debug.Print dlg.Color + 1")]
    public async Task Unmeasured_unsigned_numeric_conversions_are_refused(string code)
    {
        context.AllocVariable(rootEnv, "dlg", new Vb6Value(new VBCommonDialog(null!)));
        var run = () => Run(code);
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*unsigned Automation Long*");
    }

    [Theory]
    [InlineData(0L, 0U)]
    [InlineData(255L, 255U)]
    [InlineData(16777216L, 16777216U)]
    [InlineData(-2147483643L, 2147483653U)]
    public async Task Color_reads_as_unsigned_Automation_Long(long input, uint expected)
    {
        var dialog = new VBCommonDialog(null!);
        dialog.TrySetProperty("Color", new Vb6Value(input)).Should().BeTrue();
        dialog.TryGetProperty("Color", out var value).Should().BeTrue();
        value.Value.Should().Be(expected);
        context.AllocVariable(rootEnv, "dlg", new Vb6Value(dialog));
        await Run("v = dlg.Color\nDebug.Print VarType(v)");
        AssertDebugLog([new Vb6Value(19L)]);
    }

    [Fact]
    public void Color_negative_one_overflows()
    {
        var dialog = new VBCommonDialog(null!);
        Action set = () => dialog.TrySetProperty("Color", new Vb6Value(-1));
        set.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(6);
    }

    [Theory]
    [InlineData("Action", 394)]
    [InlineData("Left", 393)]
    [InlineData("Top", 393)]
    [InlineData("Visible", 438)]
    [InlineData("Width", 438)]
    [InlineData("Height", 438)]
    [InlineData("Enabled", 438)]
    [InlineData("hWnd", 438)]
    public void Measured_runtime_reads(string name, int error)
    {
        var dialog = new VBCommonDialog(null!);
        Action read = () => dialog.TryGetProperty(name, out _);
        read.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(error);
    }

    [Theory]
    [InlineData("Left", 382)]
    [InlineData("Top", 382)]
    public void Measured_runtime_extender_writes(string name, int error)
    {
        var dialog = new VBCommonDialog(null!);
        Action set = () => dialog.TrySetProperty(name, new Vb6Value(1));
        set.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(error);
    }

    [Fact]
    public async Task Default_member_read_is_write_only()
    {
        context.AllocVariable(rootEnv, "dlg", new Vb6Value(new VBCommonDialog(null!)));
        await Run("On Error Resume Next\nv = dlg\nDebug.Print Err.Number");
        AssertDebugLog([new Vb6Value(394L)]);
    }

    [Theory]
    [InlineData("MaxFileSize", 260)]
    [InlineData("Copies", 1)]
    [InlineData("FromPage", 0)]
    [InlineData("ToPage", 0)]
    [InlineData("Min", 0)]
    [InlineData("Max", 0)]
    [InlineData("Orientation", 1)]
    [InlineData("HelpCommand", 0)]
    public void Integer_defaults_and_storage(string name, int expected)
    {
        var dialog = new VBCommonDialog(null!);
        dialog.TryGetProperty(name, out var value).Should().BeTrue();
        value.Should().Be(new Vb6Value(expected));
        dialog.TrySetProperty(name, new Vb6Value(2)).Should().BeTrue();
        dialog.TryGetProperty(name, out value).Should().BeTrue();
        value.Should().Be(new Vb6Value(2));
    }

    [Theory]
    [InlineData("FilterIndex", -1, 380)]
    [InlineData("MaxFileSize", 0, 380)]
    [InlineData("MaxFileSize", -1, 380)]
    [InlineData("MaxFileSize", 32768, 6)]
    [InlineData("Copies", 0, 380)]
    [InlineData("Copies", -1, 380)]
    [InlineData("FromPage", -1, 380)]
    [InlineData("Orientation", 0, 380)]
    [InlineData("Orientation", -1, 380)]
    [InlineData("Orientation", 3, 380)]
    [InlineData("FileTitle", 0, 383)]
    [InlineData("hDC", 0, 383)]
    public void Measured_invalid_assignments(string name, int input, int error)
    {
        var dialog = new VBCommonDialog(null!);
        Action set = () => dialog.TrySetProperty(name, new Vb6Value(input));
        set.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(error);
    }

    [Theory]
    [InlineData("PrinterDefault", true)]
    [InlineData("FontUnderline", false)]
    [InlineData("FontStrikethru", false)]
    public void Boolean_defaults_and_storage(string name, bool expected)
    {
        var dialog = new VBCommonDialog(null!);
        dialog.TryGetProperty(name, out var value).Should().BeTrue();
        value.Should().Be(new Vb6Value(expected));
        dialog.TrySetProperty(name, new Vb6Value(!expected)).Should().BeTrue();
        dialog.TryGetProperty(name, out value).Should().BeTrue();
        value.Should().Be(new Vb6Value(!expected));
    }

    [Theory]
    [InlineData("HelpFile")]
    [InlineData("HelpKey")]
    public void Help_strings_default_empty_and_store(string name)
    {
        var dialog = new VBCommonDialog(null!);
        dialog.TryGetProperty(name, out var value).Should().BeTrue();
        value.Should().Be(new Vb6Value(""));
        dialog.TrySetProperty(name, new Vb6Value("help")).Should().BeTrue();
        dialog.TryGetProperty(name, out value).Should().BeTrue();
        value.Should().Be(new Vb6Value("help"));
    }
}
