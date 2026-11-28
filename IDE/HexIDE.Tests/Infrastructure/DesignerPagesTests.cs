using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// The form designer and automation ask a container for its pages through <see cref="IDesignerPages"/>, not through
/// SSTab's class (#3411 slice 2.4f-4, #5742). The class's code is the provider's or an add-in's, so each call is
/// contained: a throw is logged, and that call answers as a container without pages would. A switch that automation
/// asks for is refused, and not saved, when the class fails it.
/// </summary>
public class DesignerPagesTests : IDisposable
{
    // A Frame whose class has pages, and notes each question the IDE asks it.
    private sealed class PagedFrame : FrameComponentClass, IDesignerPages
    {
        public List<string> Calls { get; } = [];
        public string? Throwing { get; set; }
        public int Shown { get; set; } = 1;
        public string? Refusal { get; set; }
        public bool TabUnderClick { get; set; } = true;
        public bool Container { get; set; } = true;
        public override bool IsContainer => Container;

        private void Ask(string call)
        {
            Calls.Add(call);
            if (Throwing is { } throwing && call.StartsWith(throwing, StringComparison.Ordinal))
                throw new InvalidOperationException("thrown by the test");
        }

        private static string Names(IReadOnlyList<ComponentInstance> children) =>
            string.Join(",", children.Select(c => c.GetPropertyOrDefault(VBProperties.NameProperty)));

        public void LoadPages(ComponentInstance instance) => Ask("Load");

        public void SynchronizePages(ComponentInstance instance, IReadOnlyList<ComponentInstance> children) =>
            Ask($"Sync {Names(children)}");

        public int ShownPage(ComponentInstance instance)
        {
            Ask("Showing");
            return Shown;
        }

        public int PageCount(ComponentInstance instance)
        {
            Ask("Count");
            return 4;
        }

        public int? PageOf(ComponentInstance instance, string childName)
        {
            Ask($"PageOf {childName}");
            return childName == "Command1" ? 2 : null;
        }

        public string? PageSwitchRefusal(ComponentInstance instance)
        {
            Ask("Refusal");
            return Refusal;
        }

        public bool ShowPageAt(ComponentInstance instance, double x, double y, double width, double height,
            IReadOnlyList<ComponentInstance> children)
        {
            Ask(FormattableString.Invariant($"At {x},{y} {width}x{height} {Names(children)}"));
            return TabUnderClick;
        }

        public void ShowPage(ComponentInstance instance, int page, IReadOnlyList<ComponentInstance> children)
        {
            Ask($"Show {page} {Names(children)}");
            Shown = page;
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
                        && contract.ToString() == "\"IDesignerPages\"")
                    .Select(e => e.Level).ToList();
        }
    }

    private readonly PagedFrame paged = new();
    private readonly Captured log = new();
    private readonly FormDefinition form;
    private readonly IProjectService writer = Substitute.For<IProjectService>();
    private FormEditViewModel? designer;
    private readonly List<IdeContext> contexts = [];

    // Form1 holds Frame1, 200 by 100 pixels at 10, 20, whose class has pages, and Command1 inside it.
    public DesignerPagesTests()
    {
        DesignerClassCalls.ForgetReported();
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1") { WorkingDirectory = Path.GetTempPath() };
        var frame = new ComponentInstance(paged, "Frame1");
        frame.SetProperty(VBProperties.LeftProperty, 10);
        frame.SetProperty(VBProperties.TopProperty, 20);
        frame.SetProperty(VBProperties.WidthProperty, 200);
        frame.SetProperty(VBProperties.HeightProperty, 100);
        var button = new ComponentInstance(CommandButtonComponentClass.Instance, "Command1");
        button.SetContainer(frame);
        form = new FormDefinition(project, [new ComponentInstance(FormComponentClass.Instance, "Form1"), frame, button], "")
        {
            AbsolutePath = Path.Join(project.WorkingDirectory, "hexide-5742-" + Guid.NewGuid() + ".frm"),
        };
        project.AddForm(form);
        writer.SaveForm(form, false, interactive: false, rewrites: Arg.Any<HexIDE.Projects.SaveRewriteReport>()).Returns(true);
    }

    public void Dispose()
    {
        foreach (var context in contexts)
            context.Dispose();
        designer?.Dispose();
        log.Dispose();
        DesignerClassCalls.ForgetReported();
        GC.SuppressFinalize(this);
    }

    private FormEditViewModel Open() =>
        designer = new FormEditViewModel(null!, new EventBus(), null!, null!, null!, Substitute.For<ISettingsService>(),
            Substitute.For<HexIDE.Localization.ILocalizationService>()).Initialize(form);

    private HexIdeTools Tools()
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([form.Owner]);
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(designer is null ? [] : [designer]);
        var context = new IdeContext(projects, dock, Substitute.For<ILspClient>(),
            null!, null!, writer, new EventBus(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        contexts.Add(context);
        return new HexIdeTools(context);
    }

    // ── The designer ─────────────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void The_designer_loads_the_pages_on_opening_records_them_on_flushing_and_switches_them_on_a_click()
    {
        var vm = Open();
        paged.Calls.Should().Equal("Load");

        vm.FlushComponentsToModel();
        paged.Calls.Should().Equal("Load", "Sync Command1");

        // 60, 30 on the canvas is 50, 10 into Frame1.
        vm.SwitchDesignerPage(new Avalonia.Point(60, 30)).Should().BeTrue();
        paged.Calls.Should().Contain("At 50,10 200x100 Command1");
        vm.SelectedComponent!.Instance.BaseClass.Should().BeSameAs(paged);
        log.Reports().Should().BeEmpty();
    }

    [AvaloniaFact]
    public void A_click_on_no_tab_shows_no_page()
    {
        paged.TabUnderClick = false;
        var vm = Open();

        vm.SwitchDesignerPage(new Avalonia.Point(60, 30)).Should().BeFalse();
        paged.Calls.Should().Contain(call => call.StartsWith("At ", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void A_click_outside_the_container_doesnt_ask_it()
    {
        var vm = Open();

        vm.SwitchDesignerPage(new Avalonia.Point(5, 5)).Should().BeFalse();
        paged.Calls.Should().NotContain(call => call.StartsWith("At ", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void A_form_the_designer_opens_read_only_isnt_asked_for_its_pages()
    {
        form.MarkUnfaithfulToSave(UnfaithfulSaveCause.SSTabPageMetadata, "Test page reference");
        var vm = Open();

        vm.FlushComponentsToModel();
        vm.SwitchDesignerPage(new Avalonia.Point(60, 30)).Should().BeFalse();
        paged.Calls.Should().BeEmpty();
    }

    // Registering a container needs the provider's grant and a plain control doesn't, so a class that isn't a container
    // is never asked for pages, whatever it implements.
    [AvaloniaFact]
    public async Task A_class_that_isnt_a_container_is_never_asked_for_pages()
    {
        paged.Container = false;
        var vm = Open();
        vm.FlushComponentsToModel();
        vm.SwitchDesignerPage(new Avalonia.Point(60, 30)).Should().BeFalse();
        var tools = Tools();

        var button = (await tools.GetFormControlsAsync("Form1")).Controls.Single(c => c.Name == "Command1");
        var properties = (await tools.GetFormControlsAsync("Form1", controlName: "Frame1")).Properties!;
        var reply = await tools.SetControlPropertyAsync("Form1", "Frame1", "Tab", "3");

        paged.Calls.Should().BeEmpty();
        button.Tab.Should().BeNull();
        properties.Should().NotContain(p => p.Name == "Tab" || p.Name == "Tabs");
        reply.Success.Should().BeFalse();
        reply.Error.Should().StartWith("Property 'Tab' not found");
    }

    // Each call is contained on its own: the designer opens, flushes and takes a click whichever one throws, and only a
    // throwing click fails to show a page. The first failure is a warning and later ones are at debug.
    [AvaloniaTheory]
    [InlineData("Load")]
    [InlineData("Sync")]
    [InlineData("At")]
    public void A_class_that_throws_leaves_the_designer_usable(string throwing)
    {
        paged.Throwing = throwing;

        var vm = Open();
        vm.FlushComponentsToModel();
        vm.FlushComponentsToModel();
        var switched = vm.SwitchDesignerPage(new Avalonia.Point(60, 30));
        vm.SwitchDesignerPage(new Avalonia.Point(60, 30));

        switched.Should().Be(throwing != "At");
        var reports = log.Reports();
        reports.Should().NotBeEmpty();
        reports[0].Should().Be(Serilog.Events.LogEventLevel.Warning);
        reports.Skip(1).Should().OnlyContain(level => level == Serilog.Events.LogEventLevel.Debug);
    }

    // ── Automation ───────────────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaTheory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "Showing")]
    [InlineData(false, "Count")]
    [InlineData(false, "PageOf")]
    public async Task Automation_reads_the_pages_from_the_class(bool open, string? throwing)
    {
        if (open)
            Open();
        paged.Throwing = throwing;
        var tools = Tools();

        var button = (await tools.GetFormControlsAsync("Form1")).Controls.Single(c => c.Name == "Command1");
        var properties = (await tools.GetFormControlsAsync("Form1", controlName: "Frame1")).Properties!;

        button.Tab.Should().Be(throwing == "PageOf" ? null : 2);
        if (throwing is "Showing" or "Count")
            properties.Should().NotContain(p => p.Name == "Tab" || p.Name == "Tabs");
        else
        {
            properties.Should().ContainSingle(p => p.Name == "Tab").Which.Should().Match<PropertyRow>(p => p.Value == "1" && p.Settable);
            properties.Should().ContainSingle(p => p.Name == "Tabs").Which.Should().Match<PropertyRow>(p => p.Value == "4" && !p.Settable);
        }
        if (throwing is null)
            log.Reports().Should().BeEmpty();
        else
            log.Reports().Should().StartWith(Serilog.Events.LogEventLevel.Warning);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Automation_switches_the_page_through_the_class_after_its_check(bool open)
    {
        if (open)
            Open();
        paged.Calls.Clear();

        var reply = await Tools().SetControlPropertyAsync("Form1", "Frame1", "Tab", "3");

        reply.Success.Should().BeTrue(reply.Error);
        reply.Note.Should().Contain("Frame1.Tab is now 3");
        paged.Calls.Should().StartWith(["Refusal", "Count", "Show 3 Command1"]);
        await writer.Received(1).SaveForm(form, false, interactive: false, rewrites: Arg.Any<HexIDE.Projects.SaveRewriteReport>());
    }

    [AvaloniaFact]
    public async Task Automation_gives_the_classs_own_refusal_and_changes_nothing()
    {
        paged.Refusal = "Frame1's pages are its own.";

        var reply = await Tools().SetControlPropertyAsync("Form1", "Frame1", "Tab", "3");

        reply.Success.Should().BeFalse();
        reply.Error.Should().Be("Frame1's class refuses to switch its page. Nothing was changed. It says: "
            + "\"Frame1's pages are its own.\"");
        paged.Calls.Should().NotContain(call => call.StartsWith("Show", StringComparison.Ordinal));
        await writer.DidNotReceive().SaveForm(form, false, interactive: false, rewrites: Arg.Any<HexIDE.Projects.SaveRewriteReport>());
    }

    [AvaloniaFact]
    public async Task Automation_refuses_a_page_past_the_classs_count()
    {
        var reply = await Tools().SetControlPropertyAsync("Form1", "Frame1", "Tab", "4");

        reply.Success.Should().BeFalse();
        reply.Error.Should().Contain("from 0 to 3");
        paged.Calls.Should().NotContain(call => call.StartsWith("Show", StringComparison.Ordinal));
    }

    // A switch the class can't check or count is refused before anything changes; one it throws while making isn't saved.
    [AvaloniaTheory]
    [InlineData("Refusal", "Frame1's class failed checking whether its page can be switched, so it isn't switched here. Nothing was changed.")]
    [InlineData("Count", "Frame1's class failed giving its page count, so its page isn't switched here. Nothing was changed.")]
    [InlineData("Show", "Frame1's class failed switching its page, so the form wasn't saved. The IDE's log has the reason.")]
    public async Task Automation_refuses_a_switch_the_class_fails(string throwing, string refusal)
    {
        paged.Throwing = throwing;

        var reply = await Tools().SetControlPropertyAsync("Form1", "Frame1", "Tab", "3");

        reply.Success.Should().BeFalse();
        reply.Error.Should().Be(refusal);
        if (throwing != "Show")
            paged.Calls.Should().NotContain(call => call.StartsWith("Show", StringComparison.Ordinal));
        await writer.DidNotReceive().SaveForm(form, false, interactive: false, rewrites: Arg.Any<HexIDE.Projects.SaveRewriteReport>());
        log.Reports().Should().Equal(Serilog.Events.LogEventLevel.Warning);
    }
}
