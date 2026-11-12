using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

public class UnfaithfulSaveCauseTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => throw new InvalidOperationException(error);
    }

    [Fact]
    public void MissingUserControlExtents_DoNotImplySSTabPageMetadata()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var reader = new FormDeserializer();
        var part = reader.Deserialize(project,
            "VERSION 5.00\nBegin VB.UserControl Dial\nInvisibleAtRuntime = -1\nEnd\n", new Sink())!;
        var registry = new Dictionary<string, ComponentBaseClass>();
        registry.Add("P.Dial", UserControlComponentClass.ForModule("P.Dial", part, registry));
        var form = reader.Deserialize(project,
            "VERSION 5.00\nBegin VB.Form Form1\nBegin P.Dial Dial1\nEnd\nEnd\n",
            new Sink(), extraComponents: registry)!;

        form.CanSaveFaithfully.Should().BeFalse();
        form.UnfaithfulSaveCauses.Should().Be(UnfaithfulSaveCause.MissingUserControlExtents);
        form.UnfaithfulSaveCauses.Should().NotHaveFlag(UnfaithfulSaveCause.SSTabPageMetadata);
    }

    [Fact]
    public void SSTabPageMetadata_DoesNotImplyMissingUserControlExtents()
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), """
            VERSION 5.00
            Begin VB.Form Form1
               Begin TabDlg.SSTab SSTab1
                  Tab(0).ControlCount = 1
                  Tab(0).Control(0) = "MissingChild"
               End
            End
            """, new Sink())!;

        form.CanSaveFaithfully.Should().BeFalse();
        form.UnfaithfulSaveCauses.Should().Be(UnfaithfulSaveCause.SSTabPageMetadata);
        form.UnfaithfulSaveCauses.Should().NotHaveFlag(UnfaithfulSaveCause.MissingUserControlExtents);
    }

    [Fact]
    public void EveryCause_IsADistinctSingleBit()
    {
        var causes = Enum.GetValues<UnfaithfulSaveCause>().Where(c => c != UnfaithfulSaveCause.None).ToArray();

        causes.Should().OnlyHaveUniqueItems();
        foreach (var cause in causes)
        {
            var bits = (uint)cause;
            (bits & (bits - 1)).Should().Be(0, "{0} must be a single bit", cause);
        }
    }
}
