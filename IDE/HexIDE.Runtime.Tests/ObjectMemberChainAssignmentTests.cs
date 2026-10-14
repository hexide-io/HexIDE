namespace HexIDE.Runtime.Tests;

/// <summary>
/// An assignment through a chain of objects, <c>p.Child.Name = x</c> (#822): every member but the last is read as an
/// expression reads it, and the last is the one assigned. It used to be refused as "Object member chains are single-dot
/// only", which also refused <c>Form2.Text1.Text = x</c>, the line every program of several forms has.
/// </summary>
public class ObjectMemberChainAssignmentTests : BaseVBTestFixture
{
    private const string Parent = """
        Public Child As Child
        Public Property Get Kid() As Child
            Set Kid = Child
        End Property

        """;

    private const string Child = """
        Public Name As String
        Private mTitle As String
        Public Property Let Title(ByVal v As String)
            mTitle = "titled " & v
        End Property
        Public Property Get Title() As String
            Title = mTitle
        End Property

        """;

    private Task RunFamily(string code) =>
        RunClasses(("Dim p As Parent\nSet p = New Parent\nSet p.Child = New Child\n" + code).Replace("\r\n", "\n"),
            ("Parent", Parent.Replace("\r\n", "\n")), ("Child", Child.Replace("\r\n", "\n")));

    [Fact]
    public async Task A_field_reached_through_a_field_is_assigned_on_the_object_it_names()
    {
        await RunFamily("p.Child.Name = \"x\"\nDebug.Print p.Child.Name\n");
        AssertDebugLog(["x"]);
    }

    [Fact]
    public async Task A_field_reached_through_a_Property_Get_is_assigned_on_the_object_it_returns()
    {
        await RunFamily("p.Kid.Name = \"y\"\nDebug.Print p.Child.Name\n");
        AssertDebugLog(["y"]);
    }

    [Fact]
    public async Task A_Property_Let_at_the_end_of_the_chain_runs()
    {
        await RunFamily("p.Child.Title = \"z\"\nDebug.Print p.Child.Title\n");
        AssertDebugLog(["titled z"]);
    }
}
