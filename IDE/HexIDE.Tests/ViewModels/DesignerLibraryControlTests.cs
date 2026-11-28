using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// The form designer asks a control's class which library the control is in, and tells it which library's toolbox entry
/// it was placed from, through <see cref="IDesignerLibraryControl"/>, not through the in-box class (#3411 slice 2.4f-5,
/// #5749). A library's header line goes into the user's form, so only a library the registry lists as offering the class
/// is declared. Each call is contained: a throw is logged, and the control is placed as for a class without the contract.
/// </summary>
public class DesignerLibraryControlTests : IDisposable
{
    // A CommandButton whose class says which library its control is in, and notes each call.
    private sealed class LibraryButton : CommandButtonComponentClass, IDesignerLibraryControl
    {
        public List<string> Calls { get; } = [];
        public string? Throwing { get; set; }
        public IControlLibrary? Answer { get; set; }

        public IControlLibrary? LibraryOf(ComponentInstance instance)
        {
            Calls.Add("LibraryOf");
            if (Throwing == "LibraryOf")
                throw new InvalidOperationException("thrown by the test");
            return Answer;
        }

        public void PlacedFrom(ComponentInstance instance, IControlLibrary library)
        {
            Calls.Add("PlacedFrom " + library.Name);
            if (Throwing == "PlacedFrom")
                throw new InvalidOperationException("thrown by the test");
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
                        && contract.ToString() == "\"IDesignerLibraryControl\"")
                    .Select(e => e.Level).ToList();
        }
    }

    private static readonly AddinOrigin Owner = new("test.libraries", "Test libraries") { IsComposed = true };

    private static IControlLibrary Library(string name, string guid)
    {
        var library = Substitute.For<IControlLibrary>();
        library.Name.Returns(name);
        library.Guid.Returns(guid);
        library.FormLine.Returns($"Object = \"{guid}#1.0#0\"; \"{name}.ocx\"");
        library.IsNamedBy(Arg.Any<ObjectReference>()).Returns(call =>
            string.Equals(call.Arg<ObjectReference>().Guid, guid, StringComparison.OrdinalIgnoreCase));
        return library;
    }

    private readonly LibraryButton button = new();
    // Registered as offering the button.
    private readonly IControlLibrary offering = Library("TestLib", "{11111111-0000-0000-0000-000000000001}");
    // Registered as offering only another class.
    private readonly IControlLibrary another = Library("OtherLib", "{11111111-0000-0000-0000-000000000002}");
    // Not registered at all: the class's own object.
    private readonly IControlLibrary unregistered = Library("TestLib", "{11111111-0000-0000-0000-000000000003}");
    private readonly Captured log = new();
    private readonly FormDefinition form;
    private readonly FormEditViewModel vm;

    public DesignerLibraryControlTests()
    {
        DesignerClassCalls.ForgetReported();
        var registry = new ComponentRegistry
        {
            Grants = [new RegistrationGrant(Owner.Id, Composed: true) { TypeNamespaces = ["TestLib", "OtherLib"] }],
        };
        registry.RegisterLibrary(offering, () => [button], Owner);
        registry.RegisterLibrary(another, () => [CommandButtonComponentClass.Instance], Owner);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        form = new FormDeserializer().Deserialize(project, "VERSION 5.00\r\nBegin VB.Form Form1\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n",
            Substitute.For<IDeserializeErrorSink>())!;
        project.AddForm(form);
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(), registry, Substitute.For<ILocalizationService>());
        vm = new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
    }

    public void Dispose()
    {
        vm.Dispose();
        log.Dispose();
        DesignerClassCalls.ForgetReported();
        GC.SuppressFinalize(this);
    }

    private void Place(IControlLibrary? from = null) =>
        vm.SpawnControlAt(button, new Avalonia.Rect(10, 10, 80, 30), library: from);

    private bool Placed => vm.AllComponents.Any(c => ReferenceEquals(c.Instance.BaseClass, button));

    [AvaloniaFact]
    public void A_control_placed_from_a_library_is_told_which_and_its_library_is_declared_once()
    {
        button.Answer = offering;

        Place(offering);
        Place(offering);

        button.Calls.Should().Equal("PlacedFrom TestLib", "LibraryOf", "PlacedFrom TestLib", "LibraryOf");
        form.HeaderLines.Should().ContainSingle(line => line == offering.FormLine);
        log.Reports().Should().BeEmpty();
    }

    [AvaloniaFact]
    public void A_control_placed_from_no_library_isnt_told_one()
    {
        button.Answer = offering;

        Place();

        button.Calls.Should().Equal("LibraryOf");
        form.HeaderLines.Should().ContainSingle(line => line == offering.FormLine);
    }

    // Only a library the registry lists as offering the class is declared: its line goes into the user's form.
    [AvaloniaTheory]
    [InlineData("another")]
    [InlineData("unregistered")]
    public void A_library_that_doesnt_offer_the_class_isnt_declared(string answer)
    {
        var before = form.HeaderLines.ToList();
        button.Answer = answer == "another" ? another : unregistered;

        Place(offering);

        Placed.Should().BeTrue();
        form.HeaderLines.Should().Equal(before);
        log.Reports().Should().Equal(Serilog.Events.LogEventLevel.Warning);
    }

    [AvaloniaFact]
    public void A_control_in_no_library_declares_none()
    {
        var before = form.HeaderLines.ToList();

        Place(offering);

        Placed.Should().BeTrue();
        form.HeaderLines.Should().Equal(before);
        log.Reports().Should().BeEmpty();
    }

    // A throw is logged, and the control is placed as for a class without the contract: a throw from PlacedFrom still
    // leaves the library the class gives declared, and a throw from LibraryOf declares none.
    [AvaloniaTheory]
    [InlineData("PlacedFrom")]
    [InlineData("LibraryOf")]
    public void A_class_that_throws_still_has_its_control_placed(string throwing)
    {
        var before = form.HeaderLines.ToList();
        button.Answer = offering;
        button.Throwing = throwing;

        Place(offering);
        Place(offering);

        Placed.Should().BeTrue();
        if (throwing == "LibraryOf")
            form.HeaderLines.Should().Equal(before);
        else
            form.HeaderLines.Should().ContainSingle(line => line == offering.FormLine);
        log.Reports().Should().Equal(Serilog.Events.LogEventLevel.Warning, Serilog.Events.LogEventLevel.Debug);
    }
}
