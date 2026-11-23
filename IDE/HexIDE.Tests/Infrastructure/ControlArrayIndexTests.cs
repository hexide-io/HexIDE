using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.Infrastructure;

// Design-time measurements: https://github.com/hexide-io/HexIDE/issues/2701, supervisor comment / PR #4074.
public class ControlArrayIndexTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hexide-2701-" + Guid.NewGuid());

    public ControlArrayIndexTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, recursive: true);

    // #1397 / oracle PR #5234: the tool cannot ask a question; it names the two-call route instead.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rename_refuses_with_the_array_route_then_joins_and_saves(bool open)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P") { WorkingDirectory = directory };
        var form = new FormDeserializer().Deserialize(project, """
            VERSION 5.00
            Begin VB.Form Form1
               Begin VB.CommandButton Command1
               End
               Begin VB.CommandButton Command2
                  Caption = "Command2"
               End
               Begin VB.CommandButton Command3
               End
            End
            Attribute VB_Name = "Form1"
            """, Substitute.For<IDeserializeErrorSink>())!;
        project.AddForm(form);
        form.AbsolutePath = Path.Combine(directory, "Form1.frm");
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        var dock = Substitute.For<IDocumentDockService>();
        var events = new EventBus();
        using var designer = open ? new FormEditViewModel(null!, events, null!, null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form) : null;
        dock.OpenDocuments.Returns(designer is null ? [] : [designer]);
        var writer = new ProjectService(() => throw new InvalidOperationException(),
            Substitute.For<IWindowManager>(), events, projects, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<HexIDE.Sidecar.IUserSidecarService>(),
            new FileBaselineStore(), Substitute.For<ILocalizationService>(), Substitute.For<IHeaderRefresher>(), new ComponentRegistry());
        using var context = new IdeContext(projects, dock, Substitute.For<ILspClient>(), null!, null!, writer, events,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, Substitute.For<ILocalizationService>(), null!);
        var tools = new HexIdeTools(context);
        var before = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        var refused = await tools.SetControlPropertyAsync("Form1", "Command2", "Name", "Command1");
        refused.Success.Should().BeFalse();
        refused.Error.Should().Contain("set_control_property").And.Contain("Command1").And.Contain("Index to 0").And.Contain("then rename");
        new FormSerializer().Serialize(form, "Form1.frm").frmText.Should().Be(before);
        File.Exists(form.AbsolutePath).Should().BeFalse();
        (await tools.SetControlPropertyAsync("Form1", "Command1", "Index", "0")).Success.Should().BeTrue();
        var joined = await tools.SetControlPropertyAsync("Form1", "Command2", "Name", "Command1");
        joined.Success.Should().BeTrue(joined.Error);
        joined.Note.Should().Contain("Command1(1)");
        var third = await tools.SetControlPropertyAsync("Form1", "Command3", "Name", "Command1");
        third.Success.Should().BeTrue(third.Error);
        third.Note.Should().Contain("Command1(2)");
        var ambiguous = await tools.SetControlPropertyAsync("Form1", "Command1", "Name", "Other");
        ambiguous.Success.Should().BeFalse();
        ambiguous.Error.Should().Contain("can rejoin").And.NotContain("nothing in the IDE joins it back");
        var loaded = new FormDeserializer().Deserialize(project, File.ReadAllText(form.AbsolutePath), Substitute.For<IDeserializeErrorSink>())!;
        loaded.Components.Where(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Command1")
            .Select(PropertyText.ArrayIndexOf).Should().BeEquivalentTo(new int?[] { 0, 1, 2 });
    }

    [AvaloniaTheory]
    [InlineData(false, "0")]
    [InlineData(false, "3")]
    [InlineData(false, "32767")]
    [InlineData(true, "0")]
    [InlineData(true, "3")]
    [InlineData(true, "32767")]
    [InlineData(false, "-1")]
    [InlineData(false, "32768")]
    [InlineData(false, "x")]
    [InlineData(true, "-1")]
    [InlineData(true, "32768")]
    [InlineData(true, "x")]
    [InlineData(false, "unindexed")]
    [InlineData(true, "unindexed")]
    [InlineData(false, "duplicate")]
    [InlineData(true, "duplicate")]
    [InlineData(false, "move")]
    [InlineData(true, "move")]
    [InlineData(false, "clear")]
    [InlineData(true, "clear")]
    [InlineData(false, "siblings")]
    [InlineData(true, "siblings")]
    public async Task Index_edit_uses_the_same_rules_with_and_without_a_designer(bool open, string check)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P") { WorkingDirectory = directory };
        var form = new FormDeserializer().Deserialize(project, """
            VERSION 5.00
            Begin VB.Form Form1
               Begin VB.Timer Timer1
               End
            End
            Attribute VB_Name = "Form1"
            """, Substitute.For<IDeserializeErrorSink>())!;
        project.AddForm(form);
        form.AbsolutePath = Path.Combine(project.WorkingDirectory, "hexide-2701-" + Guid.NewGuid() + ".frm");
        var control = form.Components.Single(c => c.BaseClass is TimerComponentClass);
        if (check is "move" or "clear" or "siblings" or "duplicate" or "unindexed")
            control.UnknownRawProperties.Add(new("Index", ["Index = 0"]));
        if (check is "siblings" or "duplicate" or "unindexed")
        {
            var sibling = new ComponentInstance(TimerComponentClass.Instance, "Timer1");
            if (check != "unindexed")
                sibling.UnknownRawProperties.Add(new("Index", ["Index = 3"]));
            form.UpdateComponents([.. form.Components, sibling]);
        }
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        var dock = Substitute.For<IDocumentDockService>();
        var events = new EventBus();
        using var designer = open ? new FormEditViewModel(null!, events, null!, null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form) : null;
        dock.OpenDocuments.Returns(designer is null ? [] : [designer]);
        var writer = new ProjectService(() => throw new InvalidOperationException(),
            Substitute.For<IWindowManager>(), events, projects, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<HexIDE.Sidecar.IUserSidecarService>(),
            new FileBaselineStore(), Substitute.For<ILocalizationService>(), Substitute.For<IHeaderRefresher>(), new ComponentRegistry());
        using var context = new IdeContext(projects, dock, Substitute.For<ILspClient>(), null!, null!, writer, events,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, Substitute.For<ILocalizationService>(), null!);
        var tools = new HexIdeTools(context);
        var before = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        var value = check switch { "move" => "7", "duplicate" or "unindexed" => "3", "clear" or "siblings" => "", _ => check };
        var reply = await tools.SetControlPropertyAsync("Form1", PropertyText.AddressedName(control), "Index", value);
        var saved = File.Exists(form.AbsolutePath) ? File.ReadAllText(form.AbsolutePath) : null;
        var refused = check is "-1" or "32768" or "x" or "duplicate" or "unindexed" or "siblings";
        if (refused)
        {
            reply.Success.Should().BeFalse();
            reply.Error.Should().Contain(check == "duplicate" ? "Timer1(3)" : check == "siblings" ? "Timer1" : "Invalid property value");
            reply.Error.Should().Contain("control-arrays.md");
            if (check == "duplicate") reply.Error.Should().Contain("'Timer1(3)' already has Index 3").And.NotContain("or has no Index");
            if (check == "unindexed") reply.Error.Should().Contain("'Timer1' has the same name and no Index");
            new FormSerializer().Serialize(form, "Form1.frm").frmText.Should().Be(before);
            saved.Should().BeNull();
        }
        else
        {
            reply.Success.Should().BeTrue(reply.Error);
            saved.Should().NotBeNull();
            var index = value.Length == 0 ? (int?)null : int.Parse(value);
            PropertyText.ArrayIndexOf(control).Should().Be(index);
            if (index is { } n)
            {
                reply.Note.Should().Contain($"element {n} of array 'Timer1'");
                saved.Should().Contain($"      Index           =   {n}");
            }
            else
            {
                reply.Note.Should().Contain("plain control");
                saved.Should().NotContain("Index");
            }
            var loaded = new FormDeserializer().Deserialize(project, saved!, Substitute.For<IDeserializeErrorSink>())!;
            PropertyText.ArrayIndexOf(loaded.Components.Single(c => c.BaseClass is TimerComponentClass)).Should().Be(index);
            await writer.SaveProjectToDirectory(project, directory);
            var reread = await new Vb6ProjectFileFormat().ReadProjectAsync(project.AbsolutePath!,
                new Vb6TextEncoding(1252), null, Substitute.For<IDeserializeErrorSink>());
            PropertyText.ArrayIndexOf(reread.Project.Forms.Single().Components
                .Single(c => c.BaseClass is TimerComponentClass)).Should().Be(index);
            var rows = await tools.GetFormControlsAsync("Form1", controlName: PropertyText.AddressedName(control));
            rows.Controls.Should().ContainSingle().Which.Index.Should().Be(index);
            var indexRow = rows.Properties!.Single(p => p.Name == "Index");
            indexRow.Value.Should().Be(value);
            indexRow.Settable.Should().BeTrue();
        }
    }

    [AvaloniaFact]
    public void Properties_window_edits_Index_and_undo_restores_it()
    {
        var project = TestHelpers.CreateProjectWithForm("P", "Form1");
        var form = project.Forms[0];
        var control = new ComponentInstance(TimerComponentClass.Instance, "Timer1");
        form.UpdateComponents([.. form.Components, control]);
        var dock = Substitute.For<IDocumentDockService>();
        var events = new EventBus();
        using var designer = new FormEditViewModel(null!, events, null!, null!, null!, Substitute.For<ISettingsService>(),
            Substitute.For<ILocalizationService>()).Initialize(form);
        var properties = new PropertiesToolViewModel(dock, events, Substitute.For<IWindowManager>(),
            Substitute.For<ILocalizationService>());
        dock.ActiveDocument.Returns(designer);
        dock.PropertyChanged += Raise.Event<System.ComponentModel.PropertyChangedEventHandler>(dock,
            new System.ComponentModel.PropertyChangedEventArgs(nameof(dock.ActiveDocument)));
        designer.SelectedComponent = designer.AllComponents.Single(c => c.Instance == control);
        properties.Properties.Where(p => p.Name == "Index").Should().ContainSingle();
        var row = properties.Properties.OfType<PropertyViewModel>().Single(p => p.Name == "Index");
        row.Value.Should().Be("");
        var addressed = new List<string>();
        var selected = designer.SelectedComponent!;
        selected.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ComponentInstanceViewModel.AddressedName))
                addressed.Add(selected.AddressedName);
        };
        row.Value = "3";
        PropertyText.ArrayIndexOf(control).Should().Be(3);
        designer.SelectedComponent!.AddressedName.Should().Be("Timer1(3)");
        designer.UndoCommand.Execute(null);
        PropertyText.ArrayIndexOf(control).Should().BeNull();
        row.Value.Should().Be("");
        addressed.Should().Equal("Timer1(3)", "Timer1");
    }
    [AvaloniaTheory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("32767")]
    [InlineData("-1")]
    [InlineData("32768")]
    [InlineData("x")]
    [InlineData("unindexed")]
    [InlineData("duplicate")]
    [InlineData("move")]
    [InlineData("clear")]
    [InlineData("siblings")]
    public void Properties_window_validates_before_mutation(string check)
    {
        var project = TestHelpers.CreateProjectWithForm("P", "Form1");
        var form = project.Forms[0];
        var control = new ComponentInstance(TimerComponentClass.Instance, "Timer1");
        if (check is "move" or "clear" or "siblings" or "duplicate" or "unindexed")
            control.UnknownRawProperties.Add(new("Index", ["Index = 0"]));
        form.UpdateComponents([.. form.Components, control]);
        if (check is "siblings" or "duplicate" or "unindexed")
        {
            var sibling = new ComponentInstance(TimerComponentClass.Instance, "Timer1");
            if (check != "unindexed")
                sibling.UnknownRawProperties.Add(new("Index", ["Index = 3"]));
            form.UpdateComponents([.. form.Components, sibling]);
        }
        var dock = Substitute.For<IDocumentDockService>();
        var events = new EventBus();
        var windows = Substitute.For<IWindowManager>();
        var loc = Substitute.For<ILocalizationService>();
        loc.GetString(Arg.Any<string>()).Returns(c => c.Arg<string>());
        using var designer = new FormEditViewModel(null!, events, null!, null!, null!, Substitute.For<ISettingsService>(),
            loc).Initialize(form);
        var properties = new PropertiesToolViewModel(dock, events, windows, loc);
        dock.ActiveDocument.Returns(designer);
        dock.PropertyChanged += Raise.Event<System.ComponentModel.PropertyChangedEventHandler>(dock,
            new System.ComponentModel.PropertyChangedEventArgs(nameof(dock.ActiveDocument)));
        designer.SelectedComponent = designer.AllComponents.Single(c => c.Instance == control);
        properties.Properties.Where(p => p.Name == "Index").Should().ContainSingle();
        var row = properties.Properties.OfType<PropertyViewModel>().Single(p => p.Name == "Index");
        var value = check switch { "move" => "7", "duplicate" or "unindexed" => "3", "clear" or "siblings" => "", _ => check };
        var before = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        row.Value = check == "3" ? " \n3\n " : value;
        if (check is "-1" or "32768" or "x" or "duplicate" or "unindexed" or "siblings")
        {
            new FormSerializer().Serialize(form, "Form1.frm").frmText.Should().Be(before);
            var key = check switch { "duplicate" => "DuplicateIndex", "unindexed" => "UnindexedSibling", "siblings" => "ClearRefused", _ => "InvalidIndex" };
            loc.Received().GetString("Str.ControlArray." + key);
            designer.UndoCommand.CanExecute(null).Should().BeFalse();
        }
        else
        {
            PropertyText.ArrayIndexOf(control).Should().Be(value.Length == 0 ? null : int.Parse(value));
            row.Value.Should().Be(value);
            if (check == "3")
                control.UnknownRawProperties.Single(p => p.Name == "Index").Lines.Should().Equal("Index = 3");
            designer.UndoCommand.CanExecute(null).Should().BeTrue();
        }
    }

    // Index layout measured by hexide-mcp on Windows, #2701 round-2 review.
    [Theory]
    [InlineData("new")]
    [InlineData("move")]
    [InlineData("untouched")]
    public void Saved_Index_uses_VB6_property_columns(string edit)
    {
        const string source = "VERSION 5.00\r\n"
            + "Begin VB.Form Form1 \r\n"
            + "   Begin VB.Timer Timer1 \r\n"
            + "      Index           =   1\r\n"
            + "   End\r\n"
            + "End\r\n"
            + "Attribute VB_Name = \"Form1\"\r\n";
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var input = source;
        if (edit == "new") input = input.Replace("      Index           =   1\r\n", "");
        var form = new FormDeserializer().Deserialize(project, input, Substitute.For<IDeserializeErrorSink>())!;
        var control = form.Components.Single(c => c.BaseClass is TimerComponentClass);
        if (edit != "untouched") control.SetUntypedProperty(VBProperties.DesignIndexProperty, "5");
        var output = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        const string edited = "VERSION 5.00\r\n"
            + "Begin VB.Form Form1 \r\n"
            + "   Begin VB.Timer Timer1 \r\n"
            + "      Index           =   5\r\n"
            + "   End\r\n"
            + "End\r\n"
            + "Attribute VB_Name = \"Form1\"\r\n";
        output.Should().Be(edit == "untouched" ? source : edited);
    }

    [Theory]
    [InlineData("x", "Index must be a whole number from 0 to 32767, or empty.")]
    [InlineData("", "Index can't be cleared while 'Timer1(3)' has the same name.")]
    [InlineData("3", "'Timer1(3)' already has Index 3. Choose another Index.")]
    [InlineData("unindexed", "'Timer1' has the same name and no Index. Set its Index first.")]
    public void Properties_refusals_are_plain_localized_instructions(string value, string expected)
    {
        var target = new ComponentInstance(TimerComponentClass.Instance, "Timer1");
        var sibling = new ComponentInstance(TimerComponentClass.Instance, "Timer1");
        if (value != "unindexed") sibling.UnknownRawProperties.Add(new("Index", ["      Index           =   3"]));
        var loc = Substitute.For<ILocalizationService>();
        var pack = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(
            FindHexIdeProjectDir(), "Localization", "Packs", "en.json")));
        using (pack)
        {
            loc.GetString(Arg.Any<string>()).Returns(c => pack.RootElement.GetProperty("strings").GetProperty(c.Arg<string>()).GetString()!);
            var refusal = ControlArrayIndex.RefusalFor(target, value == "unindexed" ? "3" : value, [target, sibling]);
            refusal.Should().NotBeNull();
            refusal!.Localized(loc).Should().Be(expected);
        }
    }

    [AvaloniaTheory]
    [InlineData("3", "'Timer1(3)' já tem Index 3. Escolha outro Index.")]
    [InlineData("", "Não é possível limpar Index enquanto 'Timer1(3)' tiver o mesmo nome.")]
    public void Brazilian_Portuguese_refusals_use_the_updated_embedded_region_pack(string value, string expected)
    {
        var target = new ComponentInstance(TimerComponentClass.Instance, "Timer1");
        var sibling = new ComponentInstance(TimerComponentClass.Instance, "Timer1");
        sibling.UnknownRawProperties.Add(new("Index", ["      Index           =   3"]));
        var localization = new LocalizationService();
        localization.Apply("pt-BR");
        var refusal = ControlArrayIndex.RefusalFor(target, value, [target, sibling]);
        refusal.Should().NotBeNull();
        refusal!.Localized(localization).Should().Be(expected);
    }

    private static string FindHexIdeProjectDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "IDE", "HexIDE", "HexIDE.csproj")))
                return Path.Combine(dir.FullName, "IDE", "HexIDE");
        throw new DirectoryNotFoundException("Could not locate IDE/HexIDE");
    }

}
