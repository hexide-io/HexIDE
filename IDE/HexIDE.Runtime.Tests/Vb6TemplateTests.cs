using HexIDE.TestSupport;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Where a test finds VB6's Template tree (#1440): the folder <c>VB6_TEMPLATES</c> names or the Template folder inside
/// it, a set variable that finds nothing failing, and only an unset one on a machine without VB6 skipping.
/// </summary>
public class Vb6TemplateTests : IDisposable
{
    private readonly string extracted = Path.Join(Path.GetTempPath(), "hexide-template-" + Guid.NewGuid().ToString("N"));
    private readonly string nowhere = Path.Join(Path.GetTempPath(), "hexide-no-vb6-" + Guid.NewGuid().ToString("N"));

    public Vb6TemplateTests()
    {
        Directory.CreateDirectory(Path.Join(extracted, "Template", "Forms"));
        System.IO.File.WriteAllText(Path.Join(extracted, "Template", "Forms", "Form1.frm"), "VERSION 5.00\r\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(extracted, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string Template => Path.Join(extracted, "Template");

    [Fact]
    public void The_variable_may_name_the_folder_the_tree_was_extracted_into()
    {
        Vb6Template.FileFor(extracted, nowhere, ["Forms", "Form1.frm"])
            .Should().Be(Path.Join(Template, "Forms", "Form1.frm"));
    }

    [Fact]
    public void The_variable_may_name_the_Template_folder_itself()
    {
        Vb6Template.FileFor(Template, nowhere, ["Forms", "Form1.frm"])
            .Should().Be(Path.Join(Template, "Forms", "Form1.frm"));
    }

    [Fact]
    public void A_set_variable_that_finds_nothing_fails_and_names_what_it_tried()
    {
        var find = () => Vb6Template.FileFor(extracted, nowhere, ["Forms", "Missing.frm"]);

        find.Should().Throw<FileNotFoundException>().Which.Message
            .Should().Contain($"{Vb6Template.Variable} is '{extracted}'")
            .And.Contain(Path.Join(extracted, "Forms", "Missing.frm"))
            .And.Contain(Path.Join(Template, "Forms", "Missing.frm"));
    }

    [Fact]
    public void A_set_variable_never_skips()
    {
        Vb6Template.SkipReasonFor(Path.Join(nowhere, "typo"), nowhere)
            .Should().BeNull("a variable that names nothing is a mistake to report, not a machine without VB6");
    }

    [Fact]
    public void Unset_on_a_machine_without_VB6_the_test_skips_and_says_why()
    {
        Vb6Template.SkipReasonFor(null, nowhere).Should().Contain(Vb6Template.Variable).And.Contain(nowhere);
        Vb6Template.SkipReasonFor("", nowhere).Should().NotBeNull("an empty variable is an unset one");
    }

    [Fact]
    public void Unset_where_VB6_is_installed_the_tree_is_read_from_there()
    {
        Vb6Template.SkipReasonFor(null, Template).Should().BeNull();
        Vb6Template.FileFor(null, Template, ["Forms", "Form1.frm"]).Should().Be(Path.Join(Template, "Forms", "Form1.frm"));
    }

    [Fact]
    public void Unset_where_VB6_is_installed_a_missing_file_fails()
    {
        var find = () => Vb6Template.FileFor(null, Template, ["Forms", "Missing.frm"]);

        find.Should().Throw<FileNotFoundException>().Which.Message.Should().Contain($"VB6 is installed at '{Template}'");
    }
}
