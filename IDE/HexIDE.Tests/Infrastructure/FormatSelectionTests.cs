using Avalonia.Labs.Input;
using Avalonia.Threading;
using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.Infrastructure;

public class FormatSelectionTests
{
    private static FormEditViewModel Designer()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        var form = new FormDefinition(project,
            [new ComponentInstance(FormComponentClass.Instance, "Form1"),
             new ComponentInstance(CommandButtonComponentClass.Instance, "Command1"),
             new ComponentInstance(CommandButtonComponentClass.Instance, "Command2")], "");
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IComponentRegistry>(), Substitute.For<ILocalizationService>());
        return new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(),
            null!, null!, Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
    }

    [AvaloniaTheory]
    [InlineData("AlignLefts")]
    [InlineData("AlignRights")]
    [InlineData("AlignTops")]
    [InlineData("AlignBottoms")]
    [InlineData("AlignCentersH")]
    [InlineData("AlignCentersV")]
    [InlineData("MakeSameWidth")]
    [InlineData("MakeSameHeight")]
    [InlineData("MakeSameSize")]
    public void Direct_alignment_and_sizing_calls_guard_a_single_selection(string command)
    {
        var vm = Designer();
        var controls = vm.AllComponents.Where(c => c.Name.StartsWith("Command")).ToArray();
        controls[0].Left = 10;
        controls[0].Top = 20;
        controls[0].Width = 30;
        controls[0].Height = 40;
        controls[1].Left = 100;
        controls[1].Top = 200;
        controls[1].Width = 300;
        controls[1].Height = 400;
        // Selection and primary are set separately by the view. Even during that transition, refuse a lone target.
        vm.SelectedComponent = controls[1];
        vm.SetSelectedComponents([controls[0]]);

        typeof(FormEditViewModel).GetMethod(command)!.Invoke(vm, null);

        (controls[0].Left, controls[0].Top, controls[0].Width, controls[0].Height).Should().Be((10d, 20d, 30d, 40d));
        vm.CanUndo.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData("AlignLefts")]
    [InlineData("AlignRights")]
    [InlineData("AlignTops")]
    [InlineData("AlignBottoms")]
    [InlineData("AlignCentersH")]
    [InlineData("AlignCentersV")]
    [InlineData("MakeSameWidth")]
    [InlineData("MakeSameHeight")]
    [InlineData("MakeSameSize")]
    public void Alignment_and_sizing_predicates_follow_selection(string command)
    {
        var vm = Designer();
        var predicate = typeof(FormEditViewModel).GetMethod("Can" + command);
        predicate.Should().NotBeNull();
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        var controls = vm.AllComponents.Where(c => c.Name.StartsWith("Command")).ToArray();
        Dispatcher.UIThread.RunJobs();
        var requeried = false;
        void OnRequery(object? sender, EventArgs e) => requeried = true;
        CommandManager.RequerySuggested += OnRequery;
        try
        {
            foreach (var count in new[] { 0, 1, 2, 1, 0 })
            {
                notifications.Clear();
                requeried = false;
                vm.SetSelectedComponents(controls.Take(count));
                Dispatcher.UIThread.RunJobs();
                predicate!.Invoke(vm, [null]).Should().Be(count >= 2);
                notifications.Should().Contain(nameof(vm.SelectedComponents));
                requeried.Should().BeTrue();
            }
        }
        finally { CommandManager.RequerySuggested -= OnRequery; }
    }

    [AvaloniaTheory]
    [InlineData("AlignLefts", 2)]
    [InlineData("AlignRights", 2)]
    [InlineData("AlignTops", 2)]
    [InlineData("AlignBottoms", 2)]
    [InlineData("AlignCentersH", 2)]
    [InlineData("AlignCentersV", 2)]
    [InlineData("MakeSameWidth", 2)]
    [InlineData("MakeSameHeight", 2)]
    [InlineData("MakeSameSize", 2)]
    [InlineData("MakeHorizontalSpacingEqual", 2)]
    [InlineData("IncreaseHorizontalSpacing", 2)]
    [InlineData("DecreaseHorizontalSpacing", 2)]
    [InlineData("RemoveHorizontalSpacing", 2)]
    [InlineData("MakeVerticalSpacingEqual", 2)]
    [InlineData("IncreaseVerticalSpacing", 2)]
    [InlineData("DecreaseVerticalSpacing", 2)]
    [InlineData("RemoveVerticalSpacing", 2)]
    [InlineData("SizeToGrid", 1)]
    [InlineData("CenterHorizontally", 1)]
    [InlineData("CenterVertically", 1)]
    public async Task Format_refuses_below_its_minimum_and_explains_the_next_step(string command, int minimum)
    {
        var vm = Designer();
        var dock = Substitute.For<IDocumentDockService>();
        dock.ActiveDocument.Returns(vm);
        using var context = new IdeContext(Substitute.For<IProjectManager>(), dock, Substitute.For<ILspClient>(),
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        var tools = new HexIdeTools(context);
        var controls = vm.AllComponents.Where(c => c.Name.StartsWith("Command")).ToArray();
        for (var count = 0; count < minimum; count++)
        {
            vm.SetSelectedComponents(controls.Take(count));
            vm.SelectedComponent = count == 0 ? null : controls[0];
            var result = await tools.InvokeFormatCommandAsync(command, default);
            result.Success.Should().BeFalse();
            result.Error.Should().Contain(command).And.Contain($"{minimum}").And.Contain("interact")
                .And.Contain(count == 0 ? "select" : "add_to_selection")
                .And.Contain(count == 0 ? "no control is selected" : "Command1");
            vm.CanUndo.Should().BeFalse();
        }
        vm.SetSelectedComponents(controls.Take(minimum));
        vm.SelectedComponent = controls[0];
        var applied = await tools.InvokeFormatCommandAsync(command, default);
        applied.Success.Should().BeTrue();
        if (applied.Note!.StartsWith("Nothing changed"))
            applied.Note.Should().Contain($"'{command}' requires at least {minimum}");
    }
}
