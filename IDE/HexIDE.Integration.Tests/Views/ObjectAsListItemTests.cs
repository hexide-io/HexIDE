using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A class instance handed to a control method that wants text, as <c>List1.AddItem w</c> does, stands for its default
/// member like every other value use (#174). The item reaches the list through the text coercion alone, which listed
/// the CLR type name, so this is the one route that pins that coercion's refusal.
/// </summary>
public class ObjectAsListItemTests
{
    private const string FormWithList = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.ListBox List1
              Left            =   120
              Top             =   120
              Width           =   2000
              Height          =   1200
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static Task Run(ModuleExecutionContext ctx, ExecutionEnvironment env, string cls, string classCode) =>
        new BasicInterpreter(Substitute.For<IBasicStandardLibrary>(), ctx, env,
            $"Dim o As {cls}\nSet o = New {cls}\nList1.AddItem o\n", "Form1", null, [(cls, classCode)]).Execute();

    [AvaloniaFact]
    public async Task A_class_with_a_default_member_is_refused_rather_than_listed_by_its_type_name()
    {
        var (_, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(FormWithList);

        var run = () => Run(ctx, env, "Widget",
            "Public Function Value() As Long\nAttribute Value.VB_UserMemId = 0\n    Value = 42\nEnd Function\n");

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#174*");
        ((ItemsControl)ContainerRuntimeHarness.Child(canvas, "List1")).Items.Cast<object?>().Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task A_class_with_no_default_member_raises_438_rather_than_being_listed_by_its_type_name()
    {
        var (_, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(FormWithList);

        var run = () => Run(ctx, env, "Plain", "Public Tag As Long\n");

        (await run.Should().ThrowAsync<VBRunTimeException>()).Which.Message.Should().Contain("438");
        ((ItemsControl)ContainerRuntimeHarness.Child(canvas, "List1")).Items.Cast<object?>().Should().BeEmpty();
    }
}
