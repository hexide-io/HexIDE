using System.ComponentModel;
using HexIDE.Forms.ViewModels;

namespace HexIDE.Tests.ViewModels;

public class AddProcedureViewModelTests
{
    private readonly AddProcedureViewModel _sut = new();

    [Fact]
    public void DefaultValues_ShouldBeCorrect()
    {
        _sut.Name.Should().BeEmpty();
        _sut.IsSub.Should().BeTrue();
        _sut.IsPublic.Should().BeTrue();
        _sut.IsFunction.Should().BeFalse();
        _sut.IsProperty.Should().BeFalse();
        _sut.IsEvent.Should().BeFalse();
        _sut.IsPrivate.Should().BeFalse();
        _sut.AllLocalStatics.Should().BeFalse();
    }

    [Fact]
    public void Title_ShouldBeAddProcedure()
    {
        _sut.Title.Should().Be("Add Procedure");
    }

    [Fact]
    public void CanResize_ShouldBeFalse()
    {
        _sut.CanResize.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void OkCommand_CanExecute_ShouldBeFalse_WhenNameIsEmptyOrWhitespace(string? name)
    {
        _sut.Name = name ?? "";
        _sut.OkCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void OkCommand_CanExecute_ShouldBeTrue_WhenNameIsSet()
    {
        _sut.Name = "MySub";
        _sut.OkCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void OkCommand_Execute_ShouldFireCloseRequestedTrue()
    {
        bool? result = null;
        _sut.CloseRequested += val => result = val;
        _sut.Name = "MySub";

        _sut.OkCommand.Execute(null);

        result.Should().BeTrue();
    }

    [Fact]
    public void Cancel_ShouldFireCloseRequestedFalse()
    {
        bool? result = null;
        _sut.CloseRequested += val => result = val;

        _sut.Cancel();

        result.Should().BeFalse();
    }

    [Fact]
    public void GenerateCode_PublicSub()
    {
        _sut.Name = "DoWork";
        _sut.IsSub = true;
        _sut.IsPublic = true;

        var (begin, end) = _sut.GenerateCode();

        begin.Should().Be("Public Sub DoWork()\n");
        end.Should().Be("\nEnd Sub\n");
    }

    [Fact]
    public void GenerateCode_PrivateSub()
    {
        _sut.Name = "DoWork";
        _sut.IsSub = true;
        _sut.IsPublic = false;
        _sut.IsPrivate = true;

        var (begin, end) = _sut.GenerateCode();

        begin.Should().Be("Private Sub DoWork()\n");
        end.Should().Be("\nEnd Sub\n");
    }

    [Fact]
    public void GenerateCode_PublicFunction()
    {
        _sut.Name = "GetValue";
        _sut.IsSub = false;
        _sut.IsFunction = true;
        _sut.IsPublic = true;

        var (begin, end) = _sut.GenerateCode();

        begin.Should().Be("Public Function GetValue()\n");
        end.Should().Be("\nEnd Function\n");
    }

    [Fact]
    public void GenerateCode_PrivateFunction()
    {
        _sut.Name = "GetValue";
        _sut.IsSub = false;
        _sut.IsFunction = true;
        _sut.IsPublic = false;
        _sut.IsPrivate = true;

        var (begin, end) = _sut.GenerateCode();

        begin.Should().Be("Private Function GetValue()\n");
        end.Should().Be("\nEnd Function\n");
    }

    [Fact]
    public void GenerateCode_PublicProperty()
    {
        _sut.Name = "MyProp";
        _sut.IsSub = false;
        _sut.IsProperty = true;
        _sut.IsPublic = true;

        var (begin, end) = _sut.GenerateCode();

        begin.Should().Be("Public Property Get MyProp() As Variant\n");
        end.Should().Contain("End Property");
        end.Should().Contain("Property Let MyProp(ByVal vNewValue As Variant)");
    }

    [Fact]
    public void GenerateCode_PrivateProperty()
    {
        _sut.Name = "MyProp";
        _sut.IsProperty = true;
        _sut.IsPublic = false;
        _sut.IsPrivate = true;

        var (begin, end) = _sut.GenerateCode();

        begin.Should().Be("Private Property Get MyProp() As Variant\n");
        end.Should().Contain("Private Property Let MyProp");
    }

    [Fact]
    public void GenerateCode_Event_AlwaysPublic()
    {
        _sut.Name = "OnClick";
        _sut.IsEvent = true;
        _sut.IsPublic = false;
        _sut.IsPrivate = true;

        var (begin, end) = _sut.GenerateCode();

        begin.Should().Be("Public Event OnClick()");
        end.Should().BeEmpty();
    }

    [Fact]
    public void GenerateCode_AllLocalStatics_PublicSub()
    {
        _sut.Name = "DoWork";
        _sut.IsSub = true;
        _sut.IsPublic = true;
        _sut.AllLocalStatics = true;

        var (begin, end) = _sut.GenerateCode();

        begin.Should().Be("Public Static Sub DoWork()\n");
        end.Should().Be("\nEnd Sub\n");
    }

    [Fact]
    public void GenerateCode_AllLocalStatics_PrivateFunction()
    {
        _sut.Name = "Calc";
        _sut.IsSub = false;
        _sut.IsFunction = true;
        _sut.IsPublic = false;
        _sut.IsPrivate = true;
        _sut.AllLocalStatics = true;

        var (begin, end) = _sut.GenerateCode();

        begin.Should().Be("Private Static Function Calc()\n");
        end.Should().Be("\nEnd Function\n");
    }

    [Fact]
    public void GenerateCode_AllLocalStatics_Property()
    {
        _sut.Name = "MyProp";
        _sut.IsProperty = true;
        _sut.IsPublic = true;
        _sut.AllLocalStatics = true;

        var (begin, end) = _sut.GenerateCode();

        begin.Should().Contain("Public Static Property Get");
        end.Should().Contain("Public Static Property Let");
    }

    [Fact]
    public void PropertyChanged_ShouldFire_WhenNameChanges()
    {
        var raised = new List<string>();
        ((INotifyPropertyChanged)_sut).PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        _sut.Name = "Test";

        raised.Should().Contain("Name");
    }

    [Fact]
    public void PropertyChanged_ShouldFire_WhenIsSubChanges()
    {
        var raised = new List<string>();
        ((INotifyPropertyChanged)_sut).PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        _sut.IsSub = false;

        raised.Should().Contain("IsSub");
    }

    [Fact]
    public void PropertyChanged_ShouldFire_WhenIsPublicChanges()
    {
        var raised = new List<string>();
        ((INotifyPropertyChanged)_sut).PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        _sut.IsPublic = false;

        raised.Should().Contain("IsPublic");
    }

    [Fact]
    public void OkCommand_CanExecuteChanged_ShouldFire_WhenNameChanges()
    {
        var fired = false;
        _sut.OkCommand.CanExecuteChanged += (_, _) => fired = true;

        _sut.Name = "Test";

        fired.Should().BeTrue();
    }
}
