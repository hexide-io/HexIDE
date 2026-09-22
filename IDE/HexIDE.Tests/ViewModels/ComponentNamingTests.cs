using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// The rename rules the Properties window and <c>set_control_property</c> share (#494). Before, they lived only in
/// the designer's change handler, so a rename made with the designer closed passed no check at all.
/// </summary>
public class ComponentNamingTests
{
    private readonly ILocalizationService _loc = Substitute.For<ILocalizationService>();

    public ComponentNamingTests()
    {
        _loc.GetString("Str.Naming.Msg.NotAVb6Name").Returns("'{0}' is not a VB6 name");
        _loc.GetString("Str.Naming.Msg.DocumentNameTaken").Returns("'{0}' is taken");
    }

    private static (ProjectDefinition Project, FormDefinition Form, ComponentInstance Root,
        ComponentInstance Command1, ComponentInstance Command2) Form()
    {
        var project = TestHelpers.CreateProjectWithForm("P", "Form1");
        project.AddModule(new ModuleDefinition(project, "Module1", ModuleKind.StandardModule));
        var form = project.Forms[0];
        var root = form.Components[0];
        var command1 = new ComponentInstance(CommandButtonComponentClass.Instance, "Command1");
        var command2 = new ComponentInstance(CommandButtonComponentClass.Instance, "Command2");
        form.UpdateComponents([root, command1, command2]);
        return (project, form, root, command1, command2);
    }

    private string? Refusal(ComponentInstance target, string proposed, FormDefinition form, bool isRoot = false) =>
        ComponentNaming.RefusalFor(target, target.GetPropertyOrDefault(VBProperties.NameProperty), proposed,
            form.Components, form, isRoot, _loc);

    [Fact]
    public void A_control_may_take_a_free_name()
    {
        var (_, form, _, command1, _) = Form();
        Refusal(command1, "cmdOk", form).Should().BeNull();
    }

    [Fact]
    public void A_control_may_not_take_a_siblings_name_or_none()
    {
        var (_, form, _, command1, _) = Form();
        Refusal(command1, "Command2", form).Should().Be("Name must be unique in form");
        Refusal(command1, "", form).Should().Be("Name can't be empty");
    }

    [Fact]
    public void Committing_the_same_name_is_not_a_collision()
    {
        var (_, form, _, command1, _) = Form();
        Refusal(command1, "Command1", form).Should().BeNull();
    }

    [Fact]
    public void The_form_must_take_a_VB6_name_no_other_document_has()
    {
        var (_, form, root, _, _) = Form();
        Refusal(root, "1st form", form, isRoot: true).Should().Be("'1st form' is not a VB6 name");
        Refusal(root, "Module1", form, isRoot: true).Should().Be("'Module1' is taken");
        Refusal(root, "frmMain", form, isRoot: true).Should().BeNull();
    }
}
