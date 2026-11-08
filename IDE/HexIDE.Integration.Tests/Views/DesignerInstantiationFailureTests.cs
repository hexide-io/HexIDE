using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HexIDE.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HexIDE.Integration.Tests.Views;

public class DesignerInstantiationFailureTests
{
    private sealed class ThrowingComponent(bool notImplemented) : ComponentBaseClass([])
    {
        public override string Name => "Broken";
        public override string VBTypeName => "Test.Broken";
        protected override Control InstantiateInternal(ComponentInstance instance) =>
            throw (notImplemented ? new NotImplementedException("Preview refused") : new InvalidOperationException("Preview refused"));
    }

    private sealed class CapturedLog : ILogEventSink, IDisposable
    {
        private readonly ILogger previous = Log.Logger;
        public List<LogEvent> Events { get; } = [];
        public CapturedLog() => Log.Logger = new LoggerConfiguration().WriteTo.Sink(this).CreateLogger();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
        public void Dispose() => Log.Logger = previous;
    }

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static (FormDefinition Form, string Original, FormEditView View, Window Window) Open(string source,
        IReadOnlyDictionary<string, ComponentBaseClass>? registrations = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new NullSink(), extraComponents: registrations)!;
        // Start with the writer's format; this test isolates preview changes from format normalisation.
        var original = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        form = new FormDeserializer().Deserialize(project, original, new NullSink(), extraComponents: registrations)!;
        project.AddForm(form);
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Designer.Unmodelled.ViewOnly").Returns("View-only in the designer");
        var vm = new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!,
            Substitute.For<ISettingsService>(), localization).Initialize(form);
        var view = new FormEditView { DataContext = vm };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        window.UpdateLayout();
        return (form, original, view, window);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Failed_preview_keeps_the_form_open_and_unchanged(bool notImplemented)
    {
        using var log = new CapturedLog();
        var component = new ThrowingComponent(notImplemented);
        var source =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1\r\n" +
            "   ClientHeight = 4500\r\n" +
            "   ClientWidth = 6000\r\n" +
            "   Begin Test.Broken Broken1\r\n" +
            "      Left = 300\r\n" +
            "      Top = 600\r\n" +
            "      Width = 1500\r\n" +
            "      Height = 450\r\n" +
            "   End\r\n" +
            "   Begin VB.CommandButton Command1\r\n" +
            "      Left = 300\r\n" +
            "      Top = 1500\r\n" +
            "      Width = 1500\r\n" +
            "      Height = 450\r\n" +
            "   End\r\n" +
            "End\r\n";
        var registrations = new Dictionary<string, ComponentBaseClass> { [component.VBTypeName] = component };
        var (form, original, view, window) = Open(source, registrations);
        try
        {
            var renderer = view.GetVisualDescendants().OfType<ControlRenderer>().Single(r => r.Component!.Name == "Broken1");
            renderer.Child.Should().BeOfType<Border>();
            log.Events.Should().ContainSingle(e => e.Level == LogEventLevel.Error
                && e.Exception != null && e.Exception.Message == "Preview refused");
            renderer.GetVisualDescendants().OfType<TextBlock>().Single().Text.Should()
                .Contain("Broken1").And.Contain("View-only in the designer").And.Contain("Preview refused");
            ToolTip.GetShowOnDisabled(renderer).Should().BeTrue();
            renderer.Bounds.Width.Should().Be(100);
            renderer.Bounds.Height.Should().Be(30);
            window.IsVisible.Should().BeTrue();
            view.GetVisualDescendants().OfType<ControlRenderer>().Single(r => r.Component!.Name == "Command1")
                .Child.Should().BeOfType<Image>();
            var item = view.GetVisualDescendants().OfType<ControlItem>().Single(i => ((ComponentInstanceViewModel)i.DataContext!).Name == "Broken1");
            item.IsEnabled.Should().BeFalse();
            Encoding.UTF8.GetBytes(new FormSerializer().Serialize(form, "Form1.frm").frmText)
                .Should().Equal(Encoding.UTF8.GetBytes(original));
        }
        finally { window.Close(); }
    }

    // A StatusBar's designer instance builds, and refuses only when it is drawn: the usual "Ready … NUM" bar, a spring
    // panel beside a Contents panel with text, needs the Contents width HexIDE has not measured. That took the IDE down
    // from the render after the instance was made, so the guard covers layout and drawing as well (#2405).
    [AvaloniaFact]
    public void A_control_that_refuses_while_it_is_drawn_keeps_the_form_open_and_unchanged()
    {
        using var log = new CapturedLog();
        var (form, original, view, window) = Open(
            "VERSION 5.00\r\n" +
            "Object = \"{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCTL.OCX\"\r\n" +
            "Begin VB.Form Form1\r\n" +
            "   ClientHeight = 3600\r\n" +
            "   ClientWidth = 6000\r\n" +
            "   Begin MSComctlLib.StatusBar StatusBar1\r\n" +
            "      Height = 375\r\n" +
            "      Left = 240\r\n" +
            "      Top = 1200\r\n" +
            "      Width = 4500\r\n" +
            "      _ExtentX = 7938\r\n" +
            "      _ExtentY = 661\r\n" +
            "      _Version = 393216\r\n" +
            "      BeginProperty Panels {8E3867A5-8586-11D1-B16A-00C0F0283628}\r\n" +
            "         NumPanels = 2\r\n" +
            "         BeginProperty Panel1 {8E3867AB-8586-11D1-B16A-00C0F0283628}\r\n" +
            "            AutoSize = 1\r\n" +
            "            Text = \"Ready\"\r\n" +
            "         EndProperty\r\n" +
            "         BeginProperty Panel2 {8E3867AB-8586-11D1-B16A-00C0F0283628}\r\n" +
            "            AutoSize = 2\r\n" +
            "            Text = \"NUM\"\r\n" +
            "         EndProperty\r\n" +
            "      EndProperty\r\n" +
            "   End\r\n" +
            "End\r\n");
        try
        {
            var renderer = view.GetVisualDescendants().OfType<ControlRenderer>().Single(r => r.Component!.Name == "StatusBar1");
            renderer.Child.Should().BeOfType<Border>();
            renderer.GetVisualDescendants().OfType<TextBlock>().Single().Text.Should()
                .Contain("StatusBar1").And.Contain("Nonempty Contents Width");
            renderer.GetVisualDescendants().OfType<VBStatusBar>().Should().BeEmpty("the refused instance is not left behind");
            log.Events.Should().ContainSingle(e => e.Level == LogEventLevel.Error && e.Exception is NotImplementedException);
            window.IsVisible.Should().BeTrue();
            Encoding.UTF8.GetBytes(new FormSerializer().Serialize(form, "Form1.frm").frmText)
                .Should().Equal(Encoding.UTF8.GetBytes(original));
        }
        finally { window.Close(); }
    }
}
