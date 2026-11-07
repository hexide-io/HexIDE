using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6, native and p-code: named-arguments.md, LoadPicture and SavePicture (#2254).
public class PictureNamedArgumentTests
{
    [Theory]
    [InlineData("LoadPicture", "FileName,Size,ColorDepth,X,Y", 0)]
    [InlineData("SavePicture", "Picture,FileName", 2)]
    public void Picture_signatures_have_the_measured_names_and_required_count(string name, string names, int required)
    {
        VB6BuiltIns.IntrinsicsTakingNamedArguments.Should().ContainKey(name);
        var signature = VB6BuiltIns.IntrinsicsTakingNamedArguments[name];
        signature.Names.Should().Equal(names.Split(','));
        signature.Required.Should().Be(required);
    }

    [Theory]
    [InlineData("LoadPicture", "File", "Named argument not found")]
    [InlineData("SavePicture", "Pic", "Named argument not found")]
    public async Task Unknown_picture_names_are_refused(string name, string parameter, string message)
    {
        var call = () => VB6BuiltIns.ArrangeIntrinsicArguments(name, [new CallArg(new Vb6Value(1), null, Name: parameter)]);
        (await call.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().EndWith(message);
    }

    [Fact]
    public async Task LoadPicture_names_ignore_case_and_evaluate_in_parameter_order()
    {
        var evaluated = new List<string>();
        CallArg Arg(string name) => new(default, null, Name: name, Deferred: () =>
        {
            evaluated.Add(name);
            return Task.FromResult(new CallArg(new Vb6Value(name), null));
        });
        await VB6BuiltIns.ArrangeIntrinsicArguments("LoadPicture", [Arg("y"), Arg("X"), Arg("filename"), Arg("SIZE")]);
        evaluated.Should().Equal("filename", "SIZE", "X", "y");
    }

    [Fact]
    public async Task A_repeated_LoadPicture_name_is_refused()
    {
        var arg = new CallArg(new Vb6Value("x"), null, Name: "FileName");
        var call = () => VB6BuiltIns.ArrangeIntrinsicArguments("LoadPicture", [arg, arg]);
        (await call.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().EndWith("Named argument already specified");
    }
}
