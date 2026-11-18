using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Integration.Tests.Views;

namespace HexIDE.Runtime.Integration.Tests.Views;

public class FrmTypeNameCaseTests
{
    // #824, measured VB6 SP6 (oracle PR #4517): both spellings create a Label whose caption is readable.
    [AvaloniaTheory]
    [InlineData("vb.label")]
    [InlineData("VB.LABEL")]
    public async Task A_label_named_in_another_case_is_readable_by_the_running_program(string type)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn($"""
            VERSION 5.00
            Begin VB.Form Form1
               Begin {type} Label1
                  Caption = "L1"
               End
            End
            Attribute VB_Name = "Form1"
            """);

        var log = await ContainerRuntimeHarness.Run(ctx, env, "Debug.Print Label1.Caption");

        log.Should().ContainSingle().Which.Value.Should().Be("L1");
    }
}
