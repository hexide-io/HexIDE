using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Runtime.ProjectElements;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HexIDE.Integration.Tests.Views;

public class CarriedFileSaveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hexide-carried-save-" + Guid.NewGuid().ToString("N"));

    public CarriedFileSaveTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private readonly IWindowManager _windowManager = Substitute.For<IWindowManager>();
    private readonly ILocalizationService _localization = Substitute.For<ILocalizationService>();

    private RelatedDocumentEditorViewModel Open(byte[] bytes)
    {
        var path = Path.Combine(_dir, "carried.dsr");
        File.WriteAllBytes(path, bytes);
        return new RelatedDocumentEditorViewModel(Substitute.For<ILspClient>(), _windowManager, _localization)
            .Initialize(new RelatedDocumentDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "carried.dsr", path));
    }

    private sealed class CapturedLog : ILogEventSink, IDisposable
    {
        private readonly ILogger previous = Log.Logger;
        public List<LogEvent> Events { get; } = [];

        public CapturedLog()
        {
            Log.Logger = new LoggerConfiguration().WriteTo.Sink(this).CreateLogger();
        }

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);

        public void Dispose()
        {
            Log.Logger = previous;
        }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InvalidUtf8BytesSurviveSaving(bool bom, bool edit)
    {
        byte[] original = bom ? [0xEF, 0xBB, 0xBF, 0x63, 0xE9, 0x0D, 0x0A] : [0x63, 0xE9, 0x0D, 0x0A];
        using var vm = Open(original);
        if (edit) vm.Document.Replace(0, 1, "d");

        await vm.SaveAsync();

        var expected = (byte[])original.Clone();
        if (edit) expected[bom ? 3 : 0] = 0x64;
        File.ReadAllBytes(vm.RelatedDocument!.AbsolutePath!).Should().Equal(expected);
        vm.IsDirty.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task SavingTextRestoredToItsOriginalValueDoesNotWrite()
    {
        using var vm = Open([0x63]);
        var path = vm.RelatedDocument!.AbsolutePath!;
        var timestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, timestamp);
        vm.Document.Text = "edited";
        vm.Document.Text = "c";

        await vm.SaveAsync();

        File.GetLastWriteTimeUtc(path).Should().Be(timestamp);
    }

    [AvaloniaFact]
    public async Task AnEditLatin1CannotRepresentLeavesTheOriginalBytesIntact()
    {
        byte[] original = [0x63, 0xE9];
        using var vm = Open(original);
        _localization.GetString("Str.Dialog.SourceEncoding.NothingWritten")
            .Returns("{0}|{1}|{2}|{3}|{4}");
        vm.Document.Text += "✓";

        using var log = new CapturedLog();
        var saveTask = vm.SaveAsync();
        var save = () => saveTask;
        await save.Should().ThrowAsync<SourceEncodingRefusal>();
        saveTask.IsCanceled.Should().BeTrue("a refusal must not reach ListenErrors as a fault");
        log.Events.Should().ContainSingle(e => e.Level == LogEventLevel.Warning
            && e.RenderMessage().Contains("carried.dsr"));
        log.Events.Should().NotContain(e => e.Level >= LogEventLevel.Error);

        await _windowManager.Received(1).MessageBox(
            "carried.dsr|28591|✓|U+2713|cé✓", "HexIDE", MessageBoxButtons.Ok, MessageBoxIcon.Warning);
        File.Exists(vm.RelatedDocument!.AbsolutePath! + ".hexide-tmp").Should().BeFalse();
        File.ReadAllBytes(vm.RelatedDocument!.AbsolutePath!).Should().Equal(original);
        vm.IsDirty.Should().BeTrue();
    }
}
