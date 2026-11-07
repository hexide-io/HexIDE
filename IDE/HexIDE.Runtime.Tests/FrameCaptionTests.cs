using System.Linq;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

public class FrameCaptionTests : BaseVBTestFixture
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => throw new System.Exception(error);
    }

    [Fact]
    public async Task EmptyCaption_ReadsEmptyString()
    {
        var instance = new ComponentInstance(FrameComponentClass.Instance, "z");
        context.AllocVariable(rootEnv, "c", new Vb6Value(new VBFrame { Header = instance.GetPropertyOrDefault(VBProperties.CaptionProperty) }));

        await Run("Debug.Print c.Caption");

        AssertDebugLog([new Vb6Value("")]);
    }

    [Fact]
    public async Task CaptionFromForm_ReadsSavedCaption()
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), """
            VERSION 5.00
            Begin VB.Form Form1
               Begin VB.Frame Frame1
                  Caption = "Saved caption"
               End
            End
            Attribute VB_Name = "Form1"
            """, new Sink())!;
        var instance = form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Frame1");
        context.AllocVariable(rootEnv, "Frame1", new Vb6Value(new VBFrame { Header = instance.GetPropertyOrDefault(VBProperties.CaptionProperty) }));

        await Run("Debug.Print Frame1.Caption");

        AssertDebugLog([new Vb6Value("Saved caption")]);
    }
}
