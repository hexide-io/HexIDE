using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
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
    [AvaloniaTheory]
    [InlineData("Placement = 1", false)]
    [InlineData("MultiRow = -1", false)]
    [InlineData("TabWidthStyle = 1", false)]
    [InlineData("TabFixedWidth = 1200", false)]
    [InlineData("TabFixedHeight = 400", false)]
    [InlineData("TabStyle = 1", false)]
    [InlineData("", true)]
    public void Unsupported_TabStrip_layout_has_a_view_only_placeholder(string property, bool overflow)
    {
        var tabs = string.Join("\r\n", Enumerable.Range(1, overflow ? 12 : 1).Select(i =>
            $"         BeginProperty Tab{i} {{1EFB659A-857C-11D1-B16A-00C0F0283628}}\r\n" +
            "            Caption = \"Another tab\"\r\n" +
            "            ImageVarType = 2\r\n" +
            "         EndProperty"));
        var (form, original, view, window) = Open(
            "VERSION 5.00\r\n" +
            "Object = \"{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCTL.OCX\"\r\n" +
            "Begin VB.Form Form1\r\n" +
            "   ClientHeight = 3600\r\n" +
            "   ClientWidth = 6000\r\n" +
            "   Begin MSComctlLib.TabStrip TabStrip1\r\n" +
            "      Height = 1500\r\n" +
            "      Left = 120\r\n" +
            "      Top = 120\r\n" +
            "      Width = 3600\r\n" +
            "      _ExtentX = 6350\r\n" +
            "      _ExtentY = 2646\r\n" +
            $"      {property}\r\n" +
            "      _Version = 393216\r\n" +
            "      BeginProperty Tabs {1EFB6598-857C-11D1-B16A-00C0F0283628}\r\n" +
            $"         NumTabs = {(overflow ? 12 : 1)}\r\n{tabs}\r\n" +
            "      EndProperty\r\n" +
            "   End\r\n" +
            "End\r\n");
        try
        {
            var renderer = view.GetVisualDescendants().OfType<ControlRenderer>().Single(r => r.Component!.Name == "TabStrip1");
            renderer.Child.Should().BeOfType<Border>();
            renderer.GetVisualDescendants().OfType<TextBlock>().Single().Text.Should()
                .Contain("TabStrip1").And.Contain("View-only in the designer").And.Contain("drawing").And.Contain("#1089");
            renderer.Component!.HasDesignerPreview.Should().BeFalse();
            ToolTip.GetTip(renderer).Should().NotBeNull();
            window.IsVisible.Should().BeTrue();
            new FormSerializer().Serialize(form, "Form1.frm").frmText.Should().Be(original);
        }
        finally { window.Close(); }
    }

    private sealed class ThrowingComponent(bool notImplemented) : ComponentBaseClass([])
    {
        public override string Name => "Broken";
        public override string VBTypeName => "Test.Broken";
        protected override Control InstantiateInternal(ComponentInstance instance) =>
            throw (notImplemented ? new NotImplementedException("Preview refused") : new InvalidOperationException("Preview refused"));
    }

    private sealed class PairedPreviewComponent(bool fails) : FrameComponentClass
    {
        public override string VBTypeName => "Test.Paired";
        protected override Control InstantiateInternal(ComponentInstance instance) =>
            fails ? throw new NotImplementedException("Preview refused") : base.InstantiateInternal(instance);
    }

    [AvaloniaTheory]
    [InlineData("plain")]
    [InlineData("children")]
    [InlineData("companion")]
    [InlineData("unknown")]
    public void Editing_elsewhere_keeps_the_same_block_with_and_without_preview_failure(string scenario)
    {
        var source = "VERSION 5.00\r\nBegin VB.Form Form1\r\n" +
            "   ClientHeight = 4500\r\n   ClientWidth = 6000\r\n" +
            "   Begin Test.Paired Broken1\r\n" +
            "      Left = 300\r\n      Top = 600\r\n      Width = 1500\r\n      Height = 900\r\n" +
            (scenario == "children" ? "      Begin VB.CommandButton Child1\r\n         Caption = \"Child\"\r\n         Width = 600\r\n         Height = 300\r\n      End\r\n" : "") +
            (scenario == "companion" ? "      UnmodelledPicture = \"Form1.frx\":0000\r\n" : "") +
            (scenario == "unknown" ? "      UnmodelledValue = \"kept\"\r\n      BeginProperty UnmodelledGroup\r\n         Value = 7\r\n      EndProperty\r\n" : "") +
            "   End\r\n   Begin VB.CommandButton Command1\r\n" +
            "      Left = 300\r\n      Top = 1800\r\n      Width = 1500\r\n      Height = 450\r\n   End\r\nEnd\r\n";
        string? workingBlock = null;
        foreach (var fails in new[] { false, true })
        {
            var component = new PairedPreviewComponent(fails);
            byte[]? companion = scenario == "companion" ? [4, 0, 0, 0, 1, 2, 3, 4] : null;
            var (form, input, view, window) = Open(source,
                new Dictionary<string, ComponentBaseClass> { [component.VBTypeName] = component }, companion);
            try
            {
                var broken = form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Broken1");
                view.GetVisualDescendants().OfType<ControlRenderer>().Single(r => r.Component!.Name == "Broken1")
                    .Component!.HasDesignerPreview.Should().Be(!fails);
                if (scenario == "children")
                    broken.ContainedControls.Should().ContainSingle(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Child1");
                var command = form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Command1");
                var left = command.GetPropertyOrDefault(VBProperties.LeftProperty);
                command.SetProperty(VBProperties.LeftProperty, left + 120);
                form.Components.Single(c => c.BaseClass is FormComponentClass)
                    .SetProperty(VBProperties.CaptionProperty, "Edited elsewhere");
                var output = new FormSerializer().Serialize(form, "Form1.frm");
                output.frmText.Should().NotBe(input);
                var reread = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
                    output.frmText, new NullSink(), extraComponents:
                    new Dictionary<string, ComponentBaseClass> { [component.VBTypeName] = component })!;
                reread.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Command1").GetPropertyOrDefault(VBProperties.LeftProperty)
                    .Should().Be(left + 120, "the edited save must go through the full writer");
                reread.Components.Single(c => c.BaseClass is FormComponentClass).GetPropertyOrDefault(VBProperties.CaptionProperty)
                    .Should().Be("Edited elsewhere");
                var block = PairedBlock(output.frmText);
                Encoding.UTF8.GetBytes(block).Should().Equal(Encoding.UTF8.GetBytes(PairedBlock(input)));
                if (workingBlock is not null)
                    block.Should().Be(workingBlock);
                workingBlock = block;
                if (companion is not null)
                    output.frxContent.Should().Equal(companion);
                window.IsVisible.Should().BeTrue();
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void Corpus_TabStrip_block_survives_a_form_edit_after_its_preview_refuses()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "corpus")))
            root = root.Parent;
        var source = File.ReadAllText(Path.Combine(root!.FullName, "corpus", "inbox-controls", "saved", "tabstrip", "Form1.frm"));
        string? withoutPreview = null;
        foreach (var showPreview in new[] { false, true })
        {
            var companion = File.ReadAllBytes(Path.Combine(root.FullName, "corpus", "inbox-controls", "saved", "tabstrip", "Form1.frx"));
            var (form, input, view, window) = Open(source, companion: companion, showPreview: showPreview);
            try
            {
                if (showPreview)
                    view.GetVisualDescendants().OfType<ControlRenderer>().Single(r => r.Component!.Name == "TabStrip1")
                        .Component!.HasDesignerPreview.Should().BeFalse();
                form.Components.Single(c => c.BaseClass is FormComponentClass)
                    .SetProperty(VBProperties.CaptionProperty, "Edited elsewhere");
                var output = new FormSerializer().Serialize(form, "Form1.frm").frmText;
                output.Should().NotBe(input).And.Contain("Edited elsewhere");
                string Block(string text)
                {
                    var start = text.IndexOf("   Begin MSComctlLib.TabStrip TabStrip1", StringComparison.Ordinal);
                    var end = text.IndexOf("\r\n   End", start, StringComparison.Ordinal) + "\r\n   End".Length;
                    return text[start..end];
                }
                Encoding.UTF8.GetBytes(Block(output)).Should().Equal(Encoding.UTF8.GetBytes(Block(input)));
                // VB6's own save of this form round-trips, so the block is also VB6's bytes, not only the writer's.
                Block(input).Should().Be(Block(source));
                if (withoutPreview is not null)
                    Block(output).Should().Be(withoutPreview);
                withoutPreview = Block(output);
            }
            finally { window.Close(); }
        }
    }

    private static string PairedBlock(string source)
    {
        var start = source.IndexOf("   Begin Test.Paired Broken1", StringComparison.Ordinal);
        var end = source.IndexOf("\r\n   End", start, StringComparison.Ordinal) + "\r\n   End".Length;
        return source[start..end];
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
        IReadOnlyDictionary<string, ComponentBaseClass>? registrations = null,
        byte[]? companion = null, bool showPreview = true)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new NullSink(),
            frxBlobs: companion is null ? null : FrxDeserializer.Read(companion, source), extraComponents: registrations)!;
        // Start with the writer's format; this test isolates preview changes from format normalisation.
        var input = new FormSerializer().Serialize(form, "Form1.frm");
        var original = input.frmText;
        form = new FormDeserializer().Deserialize(project, original, new NullSink(),
            frxBlobs: input.frxContent is null ? null : FrxDeserializer.Read(input.frxContent, original), extraComponents: registrations)!;
        project.AddForm(form);
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Designer.Unmodelled.ViewOnly").Returns("View-only in the designer");
        var vm = new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!,
            Substitute.For<ISettingsService>(), localization, new Vb6ProjectFileFormat()).Initialize(form);
        var view = new FormEditView { DataContext = vm };
        var window = new Window { Width = 800, Height = 600, Content = view };
        if (showPreview)
        {
            window.Show();
            window.UpdateLayout();
        }
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

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Failed_preview_is_excluded_from_property_and_multiple_selection(bool selectBeforeFailure)
    {
        var component = new ThrowingComponent(true);
        var (form, _, view, window) = Open(
            "VERSION 5.00\r\nBegin VB.Form Form1\r\n" +
            "   ClientHeight = 4500\r\n   ClientWidth = 6000\r\n" +
            "   Begin Test.Broken Broken1\r\n" +
            "      Width = 1500\r\n      Height = 450\r\n   End\r\nEnd\r\n",
            new Dictionary<string, ComponentBaseClass> { [component.VBTypeName] = component });
        try
        {
            var designer = (FormEditViewModel)view.DataContext!;
            var broken = designer.AllComponents.Single(c => c.Name == "Broken1");
            var dock = Substitute.For<IDocumentDockService>();
            var grid = new PropertiesToolViewModel(dock, Substitute.For<IEventBus>(),
                Substitute.For<IWindowManager>(), Substitute.For<ILocalizationService>(), new Vb6ProjectFileFormat());
            dock.ActiveDocument.Returns(designer);
            dock.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(dock,
                new PropertyChangedEventArgs(nameof(dock.ActiveDocument)));
            if (selectBeforeFailure)
            {
                broken.PreviewFailure = null;
                designer.SelectedComponent = broken;
                designer.SetSelectedComponents([broken]);
                broken.PreviewFailure = "Preview refused";
            }
            else
            {
                designer.SelectedComponent = broken;
                designer.SetSelectedComponents([broken]);
            }
            designer.SelectedComponent.Should().BeSameAs(designer.Form);
            designer.SelectedComponents.Should().BeEmpty();
            grid.ComponentsProxy.Should().NotContain(broken);
            grid.ComponentsProxy.Should().Contain(designer.Form);
            grid.SelectedComponentProxy = broken;
            grid.SelectedComponentProxy.Should().BeSameAs(designer.Form);
            designer.SetSelectedComponents([broken, designer.Form]);
            designer.SelectedComponents.Should().Equal(designer.Form);
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
