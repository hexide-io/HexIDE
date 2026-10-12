using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.ProjectElements;

/// <summary>
/// A <c>.ctl</c> or <c>.pag</c> is one file in one encoding, and its module holds it. The designer half reads
/// and sets the module's, so the designer's Save Form and the module's own save cannot write the file two ways.
/// </summary>
public sealed class DesignerHalfEncodingTests
{
    private static readonly Vb6TextEncoding CentralEuropean = new(1250);
    private static readonly Vb6TextEncoding Cyrillic = new(1251);

    /// <summary>
    /// Both halves start in Latin-1, which is no host's ANSI code page. Left at the default, either could
    /// already hold 1250 or 1251 on a Central European or Cyrillic Windows, and a broken link would pass.
    /// </summary>
    private static (ModuleDefinition Module, FormDefinition Half) AUserControl()
    {
        var project = TestHelpers.CreateProject();
        var module = new ModuleDefinition(project, "Gauge", ModuleKind.UserControl) { TextEncoding = Vb6TextEncoding.Latin1 };
        var half = new FormDefinition(project, FormComponentClass.Instance, "Gauge") { TextEncoding = Vb6TextEncoding.Latin1 };
        half.UpdateRootTypeName("VB.UserControl");
        module.UpdateFormPart(half);
        return (module, half);
    }

    [Fact]
    public void ADesignerHalfReadsItsModulesEncoding()
    {
        var (module, half) = AUserControl();

        module.TextEncoding = CentralEuropean;

        half.TextEncoding.Should().Be(CentralEuropean);
    }

    [Fact]
    public void ADesignerHalfJoinedToAModuleTakesTheModulesEncoding()
    {
        var project = TestHelpers.CreateProject();
        var module = new ModuleDefinition(project, "Gauge", ModuleKind.UserControl) { TextEncoding = CentralEuropean };
        var half = new FormDefinition(project, FormComponentClass.Instance, "Gauge") { TextEncoding = Cyrillic };

        module.UpdateFormPart(half);

        half.TextEncoding.Should().Be(CentralEuropean, "the file's encoding is the module's, whatever the half held before");
    }

    [Fact]
    public void SettingADesignerHalfsEncodingSetsItsModules()
    {
        var (module, half) = AUserControl();

        half.TextEncoding = Cyrillic;

        module.TextEncoding.Should().Be(Cyrillic);
    }

    [Fact]
    public void ADesignerHalfLetGoKeepsTheFilesEncoding()
    {
        var (module, half) = AUserControl();
        module.TextEncoding = CentralEuropean;

        module.UpdateFormPart(null);
        module.TextEncoding = Cyrillic;

        half.TextEncoding.Should().Be(CentralEuropean);
    }

    [Fact]
    public void AFormKeepsAnEncodingOfItsOwn()
    {
        var project = TestHelpers.CreateProject();
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");

        form.TextEncoding = CentralEuropean;

        form.TextEncoding.Should().Be(CentralEuropean);
    }
}
