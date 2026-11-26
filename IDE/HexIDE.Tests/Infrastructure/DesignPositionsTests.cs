using Avalonia.Headless.XUnit;
using HexIDE.Addins;
using HexIDE.Desktop.Server;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// The Properties window, the toolbar's readout, the form designer and automation take VB6's scale rules from the positions
/// service the provider registered (#3411 slice 2.4f-6, #5758), not from the provider's <c>DesignPositions</c> and
/// <c>LineComponentClass</c>. The rules decide values written into the user's form, so only a granted owner registers them,
/// each call is contained, and an answer the IDE can't use counts as none: a position is then shown as the model holds it,
/// a typed one is refused, and a Line isn't moved.
/// </summary>
public class DesignPositionsTests : IDisposable
{
    // Rules no real scale gives, so a site that ignores them shows it: a position shows half a point more than its pixels, a
    // typed one is twice the pixels, and a Line's units are 30 twips across and 45 down. Each call is noted.
    private sealed class NotingPositions : IDesignPositions
    {
        public List<string> Calls { get; } = [];
        public string? Failing { get; set; }
        public bool LineUnmeasured { get; set; }

        public (double Value, string Unit) Shown(FormDefinition form, ComponentInstance component, PropertyClass property,
            double pixels)
        {
            Calls.Add($"Shown {property.Name}");
            if (Failing == "throws")
                throw new InvalidOperationException("thrown by the test");
            return Failing switch
            {
                "unit" => (pixels + 0.5, "furlongs"),
                "nan" => (double.NaN, "points"),
                _ => (pixels + 0.5, "points"),
            };
        }

        public double? ToPixels(FormDefinition form, ComponentInstance component, PropertyClass property, double value)
        {
            Calls.Add($"ToPixels {property.Name} {value}");
            if (Failing == "throws")
                throw new InvalidOperationException("thrown by the test");
            if (value == 999)
                return null;
            return Failing switch
            {
                "infinite pixels" => double.PositiveInfinity,
                "negative size" when ReferenceEquals(property, VBProperties.WidthProperty) => -1,
                _ => value * 2,
            };
        }

        public (double X, double Y)? LineTwipsPerUnit(FormDefinition form, ComponentInstance line)
        {
            Calls.Add("Line");
            if (Failing == "throws")
                throw new InvalidOperationException("thrown by the test");
            if (LineUnmeasured)
                return null;
            return Failing switch
            {
                "zero line unit" => (0, 45),
                "nan line unit" => (double.NaN, 45),
                _ => (30, 45),
            };
        }
    }

    private sealed class Captured : Serilog.Core.ILogEventSink, IDisposable
    {
        private readonly Serilog.ILogger previous = Serilog.Log.Logger;
        public List<Serilog.Events.LogEvent> Events { get; } = [];
        public Captured() => Serilog.Log.Logger = new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(this).CreateLogger();
        public void Emit(Serilog.Events.LogEvent logEvent) { lock (Events) Events.Add(logEvent); }
        public void Dispose() => Serilog.Log.Logger = previous;

        public List<Serilog.Events.LogEventLevel> Reports()
        {
            lock (Events)
                return Events.Where(e => e.MessageTemplate.Text.Contains("failed its", StringComparison.Ordinal)
                        && e.Properties.TryGetValue("Contract", out var contract)
                        && contract.ToString() == "\"IDesignPositions\"")
                    .Select(e => e.Level).ToList();
        }
    }

    private static readonly AddinOrigin Owner = new("test.positions", "Test positions") { IsComposed = true };
    private static readonly AddinOrigin Other = new("test.other-positions", "Other positions") { IsComposed = true };

    private static RegistrationGrant[] Granted(params AddinOrigin[] owners) =>
        [.. owners.Select(owner => new RegistrationGrant(owner.Id, Composed: true) { DesignPositions = true })];

    private readonly NotingPositions positions = new();
    private readonly Captured log = new();
    private readonly FormDefinition form;
    private readonly IProjectService writer = Substitute.For<IProjectService>();
    private readonly List<IDisposable> disposables = [];
    private IDisposable? registered;
    private FormEditViewModel? designer;

    // Form1, a twips form, holds Command1 at 10, 20 pixels, 80 by 30, and Line1 from 100, 100 to 200, 200 in its units.
    public DesignPositionsTests()
    {
        DesignerClassCalls.ForgetReported();
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1") { WorkingDirectory = Path.GetTempPath() };
        var button = new ComponentInstance(CommandButtonComponentClass.Instance, "Command1");
        button.SetProperty(VBProperties.LeftProperty, 10);
        button.SetProperty(VBProperties.TopProperty, 20);
        button.SetProperty(VBProperties.WidthProperty, 80);
        button.SetProperty(VBProperties.HeightProperty, 30);
        var line = new ComponentInstance(LineComponentClass.Instance, "Line1");
        line.SetProperty(VBProperties.X1Property, 100);
        line.SetProperty(VBProperties.Y1Property, 100);
        line.SetProperty(VBProperties.X2Property, 200);
        line.SetProperty(VBProperties.Y2Property, 200);
        form = new FormDefinition(project, [new ComponentInstance(FormComponentClass.Instance, "Form1"), button, line], "")
        {
            AbsolutePath = Path.Join(project.WorkingDirectory, "hexide-5758-" + Guid.NewGuid() + ".frm"),
        };
        project.AddForm(form);
        writer.SaveForm(form, false, interactive: false, rewrites: Arg.Any<SaveRewriteReport>()).Returns(true);
    }

    public void Dispose()
    {
        foreach (var disposable in disposables)
            disposable.Dispose();
        designer?.Dispose();
        if (registered is not null)
        {
            registered.Dispose();
            App.RegisterProviderPositions();
        }
        log.Dispose();
        DesignerClassCalls.ForgetReported();
        GC.SuppressFinalize(this);
    }

    private static TestApp App => (TestApp)Avalonia.Application.Current!;

    // Takes the provider's rules out of this test's application, and puts the test's in, or none.
    private void Use(IDesignPositions? rules)
    {
        App.ProviderPositions!.Dispose();
        registered = rules is null
            ? new NoRegistration()
            : new AddinDesignPositionService { Grants = Granted(Owner) }.Register(rules, Owner);
    }

    private sealed class NoRegistration : IDisposable { public void Dispose() { } }

    private ComponentInstance Button => form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Command1");

    private ComponentInstance Line => form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Line1");

    private FormEditViewModel Open() =>
        designer = new FormEditViewModel(null!, new EventBus(), writer, null!, null!, Substitute.For<ISettingsService>(),
            Substitute.For<ILocalizationService>()).Initialize(form);

    private ComponentInstanceViewModel Placed(string name) => designer!.AllComponents.Single(c => c.Name == name);

    private PropertiesToolViewModel Window()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var dock = Substitute.For<IDocumentDockService>();
        dock.ActiveDocument.Returns(designer);
        var window = new PropertiesToolViewModel(dock, Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), localization);
        designer!.SelectedComponent = Placed("Command1");
        return window;
    }

    private static PropertyViewModel Row(PropertiesToolViewModel window, string name) =>
        window.Properties.OfType<PropertyViewModel>().Single(p => p.Name == name);

    private FocusedProjectUtil Readout()
    {
        var dock = Substitute.For<IDocumentDockService>();
        var focus = new FocusedProjectUtil(dock, Substitute.For<IProjectManager>());
        dock.ActiveDocument.Returns(designer);
        dock.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(dock,
            new PropertyChangedEventArgs(nameof(dock.ActiveDocument)));
        designer!.SelectedComponent = Placed("Command1");
        return focus;
    }

    private HexIdeTools Tools()
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([form.Owner]);
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(designer is null ? [] : [designer]);
        var context = new IdeContext(projects, dock, Substitute.For<ILspClient>(),
            null!, null!, writer, new EventBus(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        disposables.Add(context);
        return new HexIdeTools(context);
    }

    private async Task<PropertyRow> ToolRow(string name) =>
        (await Tools().GetFormControlsAsync("Form1", controlName: "Command1")).Properties!.Single(p => p.Name == name);

    // ── Registration ─────────────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Only_a_granted_owner_registers_the_rules_and_only_one_at_a_time()
    {
        var scope = new object();

        var refused = () => new AddinDesignPositionService { Scope = scope, Grants = [] }.Register(positions, Owner);
        refused.Should().Throw<InvalidOperationException>().WithMessage("*isn't one HexIDE lets give*");
        AddinDesignPositionService.For(scope).Should().BeNull();

        var service = new AddinDesignPositionService { Scope = scope, Grants = Granted(Owner, Other) };
        var first = service.Register(positions, Owner);
        var second = () => service.Register(new NotingPositions(), Other);
        second.Should().Throw<InvalidOperationException>().WithMessage("*Test positions (test.positions) gives them already*");
        AddinDesignPositionService.For(scope).Should().BeSameAs(positions);

        first.Dispose();
        AddinDesignPositionService.For(scope).Should().BeNull();
        using var replaced = service.Register(new NotingPositions(), Other);
        AddinDesignPositionService.For(scope).Should().NotBeNull().And.NotBeSameAs(positions);
    }

    [AvaloniaFact]
    public void The_provider_holds_the_only_grant()
    {
        RegistrationGrants.All.Where(grant => grant.DesignPositions).Select(grant => grant.OwnerId)
            .Should().Equal(RegistrationGrants.BasicProvider);
    }

    // ── Each site asks the rules ─────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void The_Properties_window_shows_and_takes_a_position_in_the_rules_units()
    {
        Use(positions);
        Open();
        var window = Window();

        Row(window, "Left").Value.Should().Be(10.5);
        Row(window, "Width").Value.Should().Be(80.5);

        Row(window, "Left").Value = "4";

        Button.GetPropertyOrDefault(VBProperties.LeftProperty).Should().Be(8);
        positions.Calls.Should().Contain("ToPixels Left 4");
        log.Reports().Should().BeEmpty();
    }

    [AvaloniaFact]
    public void The_readout_shows_the_rules_values()
    {
        Use(positions);
        Open();

        var focus = Readout();

        focus.FocusedComponentPosition.Should().Be("10.5, 20.5");
        focus.FocusedComponentSize.Should().Be("80.5 x 30.5");
    }

    [AvaloniaFact]
    public async Task Automation_reads_sets_and_reads_back_a_position_in_the_rules_units()
    {
        Use(positions);
        Open();

        var row = await ToolRow("Left");
        var reply = await Tools().SetControlPropertyAsync("Form1", "Command1", "Left", "4");

        row.Value.Should().Be("10.5");
        row.Note.Should().Be("in points, as the Properties window shows it");
        reply.Success.Should().BeTrue();
        Button.GetPropertyOrDefault(VBProperties.LeftProperty).Should().Be(8);
        reply.Note.Should().Contain("Command1.Left is now 8.5 points.");
    }

    [AvaloniaFact]
    public void A_Line_is_laid_out_drawn_and_moved_in_the_rules_units()
    {
        Use(positions);
        Open();
        var line = Placed("Line1");

        line.LineTwipsPerUnit.Should().Be((30, 45));
        // 100 units of 30 twips is 200 pixels across, and of 45 twips 300 pixels down, less the 2-pixel pad round the pen.
        line.RelativeLeft.Should().Be(198);
        line.RelativeTop.Should().Be(298);

        // 3 pixels is 45 twips: 1.5 units across, 1 unit down.
        line.Left += 3;
        line.Top += 3;

        Line.GetPropertyOrDefault(VBProperties.X1Property).Should().Be(101.5);
        Line.GetPropertyOrDefault(VBProperties.X2Property).Should().Be(201.5);
        Line.GetPropertyOrDefault(VBProperties.Y1Property).Should().Be(101);
        Line.GetPropertyOrDefault(VBProperties.Y2Property).Should().Be(201);
    }

    // A scale the rules can't run is an answer, not a failure: the Line is laid out and moved in twips, as it always has
    // been (#5759 is that move's own bug), and nothing is logged.
    [AvaloniaFact]
    public void A_Line_in_a_scale_the_rules_cant_run_is_moved_in_twips_as_before()
    {
        positions.LineUnmeasured = true;
        Use(positions);
        Open();
        var line = Placed("Line1");

        line.LineTwipsPerUnit.Should().Be((1, 1));
        line.Left += 3;

        Line.GetPropertyOrDefault(VBProperties.X1Property).Should().Be(145);
        log.Reports().Should().BeEmpty();
    }

    // A typed value no position holds is the rules' answer too: refused, with nothing changed and nothing logged.
    [AvaloniaFact]
    public async Task A_value_no_position_holds_is_refused_and_not_logged()
    {
        Use(positions);
        Open();
        var window = Window();

        Row(window, "Left").Value = "999";
        var reply = await Tools().SetControlPropertyAsync("Form1", "Command1", "Left", "999");

        Button.GetPropertyOrDefault(VBProperties.LeftProperty).Should().Be(10);
        reply.Success.Should().BeFalse();
        reply.Error.Should().Be("Cannot set 'Left' to '999': no position can hold it. Nothing was changed.");
        log.Reports().Should().BeEmpty();
    }

    // ── Failures ─────────────────────────────────────────────────────────────────────────────────────────────────

    // Rules that throw, or that aren't registered at all: each position is shown as the model holds it, in pixels with no
    // unit, a typed one is refused with nothing changed, and a Line is laid out in twips but not moved. A throw is logged
    // as a warning the first time and at debug after that.
    [AvaloniaTheory]
    [InlineData("throws")]
    [InlineData("none")]
    public async Task Without_an_answer_positions_show_as_the_model_holds_them_and_nothing_changes(string failure)
    {
        positions.Failing = failure;
        Use(failure == "none" ? null : positions);
        Open();
        var window = Window();
        var focus = Readout();
        var line = Placed("Line1");

        Row(window, "Left").Value.Should().Be(10d);
        Row(window, "Left").Value = "4";
        focus.FocusedComponentPosition.Should().Be("10, 20");
        var row = await ToolRow("Left");
        var reply = await Tools().SetControlPropertyAsync("Form1", "Command1", "Left", "4");
        line.LineTwipsPerUnit.Should().Be((1, 1));
        line.Left += 3;

        Button.GetPropertyOrDefault(VBProperties.LeftProperty).Should().Be(10);
        row.Value.Should().Be("10");
        row.Note.Should().BeNull();
        reply.Success.Should().BeFalse();
        reply.Error.Should().StartWith("Cannot set 'Left' to '4': its units couldn't be worked out, so nothing was changed.");
        Line.GetPropertyOrDefault(VBProperties.X1Property).Should().Be(100);
        if (failure == "none")
            log.Reports().Should().BeEmpty();
        else
        {
            log.Reports().Should().NotBeEmpty();
            log.Reports()[0].Should().Be(Serilog.Events.LogEventLevel.Warning);
            log.Reports().Skip(1).Should().OnlyContain(level => level == Serilog.Events.LogEventLevel.Debug);
        }
    }

    // An answer the IDE can't use is no answer, and logged: a unit VB6 doesn't show, a value or pixels that aren't finite,
    // a Width below zero, and a Line unit that isn't finite and above zero.
    [AvaloniaTheory]
    [InlineData("unit")]
    [InlineData("nan")]
    [InlineData("infinite pixels")]
    [InlineData("negative size")]
    [InlineData("zero line unit")]
    [InlineData("nan line unit")]
    public async Task An_answer_the_IDE_cant_use_counts_as_none(string failure)
    {
        positions.Failing = failure;
        Use(positions);
        Open();

        switch (failure)
        {
            case "unit" or "nan":
                (await ToolRow("Left")).Should().Be(new PropertyRow("Left", "10", true));
                Row(Window(), "Left").Value.Should().Be(10d);
                break;
            case "infinite pixels" or "negative size":
                var property = failure == "infinite pixels" ? "Left" : "Width";
                var reply = await Tools().SetControlPropertyAsync("Form1", "Command1", property, "4");
                reply.Success.Should().BeFalse();
                Button.GetPropertyOrDefault(VBProperties.LeftProperty).Should().Be(10);
                Button.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(80);
                break;
            default:
                var line = Placed("Line1");
                line.LineTwipsPerUnit.Should().Be((1, 1));
                line.Left += 3;
                Line.GetPropertyOrDefault(VBProperties.X1Property).Should().Be(100);
                break;
        }
        log.Reports().Should().NotBeEmpty();
        log.Reports()[0].Should().Be(Serilog.Events.LogEventLevel.Warning);
    }

    // ── The provider's rules ─────────────────────────────────────────────────────────────────────────────────────

    // What the provider registers answers as DesignPositions and TwipsPerUnit do, with their refusals as answers.
    [AvaloniaFact]
    public void The_providers_rules_answer_as_its_own_types_do()
    {
        var rules = new ProviderDesignPositions();
        var pixelForm = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            "VERSION 5.00\r\nBegin VB.Form Form2\r\n   ClientHeight = 3090\r\n   ClientWidth = 4680\r\n   ScaleMode = 3\r\n"
            + "   Begin VB.TextBox Text1\r\n      Left = 120\r\n      Top = 120\r\n      Width = 1215\r\n      Height = 495\r\n   End\r\n"
            + "   Begin VB.PictureBox Picture1\r\n      Height = 1500\r\n      Left = 240\r\n      ScaleHeight = 10\r\n"
            + "      ScaleMode = 0\r\n      ScaleWidth = 10\r\n      Top = 240\r\n      Width = 1500\r\n"
            + "      Begin VB.Line Line1\r\n         X1 = 1\r\n         X2 = 5\r\n         Y1 = 1\r\n         Y2 = 5\r\n      End\r\n"
            + "   End\r\n"
            + "   Begin VB.Line Line2\r\n      X1 = 10\r\n      X2 = 50\r\n      Y1 = 10\r\n      Y2 = 50\r\n   End\r\n"
            + "End\r\nAttribute VB_Name = \"Form2\"\r\n", new NullSink())!;
        var text = pixelForm.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Text1");
        ComponentInstance Named(string name) =>
            pixelForm.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == name);

        rules.Shown(pixelForm, text, VBProperties.LeftProperty, 8).Should().Be((8d, "pixels"));
        rules.ToPixels(pixelForm, text, VBProperties.LeftProperty, 10.5).Should().Be(158 / 15d);
        rules.ToPixels(pixelForm, text, VBProperties.LeftProperty, double.MaxValue).Should().BeNull();
        rules.LineTwipsPerUnit(pixelForm, Named("Line2")).Should().Be((15d, 15d));
        // A Line in a User-scale PictureBox is a scale HexIDE can't run yet (#4693).
        rules.LineTwipsPerUnit(pixelForm, Named("Line1")).Should().BeNull();
    }

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string message) { } }
}
