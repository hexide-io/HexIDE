using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

public class ImageListTests : BaseVBTestFixture
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    internal static string Corpus(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Join(root.FullName, "corpus"))) root = root.Parent;
        var basePath = Path.Join(root!.FullName, "corpus", "inbox-controls");
        var saved = Path.Join(basePath, "saved", "imagelist");
        return Path.Join(Directory.Exists(saved) ? saved : Path.Join(basePath, "pending", "imagelist"), name, "Form1.frm");
    }

    // #1083, issuecomment-6427015606 and its empty-Key follow-up: these are VB6's own saves.
    [Theory]
    [InlineData("three")]
    [InlineData("custom")]
    [InlineData("empty-key")]
    public void Vb6_saved_images_load_and_round_trip_verbatim(string name)
    {
        var path = Corpus(name);
        var source = Vb6TextFile.ReadAllText(path);
        var bytes = File.ReadAllBytes(Path.ChangeExtension(path, ".frx"));
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink(),
            FrxDeserializer.Read(bytes, source))!;
        form.Components.Should().Contain(c => c.BaseClass.VBTypeName == "MSComctlLib.ImageList");
        var output = new FormSerializer().Serialize(form, "Form1.frm");
        output.frmText.ReplaceLineEndings().Should().Be(source.ReplaceLineEndings());
        output.frxContent.Should().Equal(bytes);
    }

    private Avalonia.Controls.Control Loaded(string name = "three")
    {
        var path = Corpus(name);
        var source = Vb6TextFile.ReadAllText(path);
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink(),
            FrxDeserializer.Read(File.ReadAllBytes(Path.ChangeExtension(path, ".frx")), source))!;
        var component = form.Components.Single(c => c.BaseClass.VBTypeName == "MSComctlLib.ImageList");
        var control = ((HexIDE.Runtime.Components.ComponentBaseClass)component.BaseClass).Instantiate(component);
        context.AllocVariable(rootEnv, "il", new Vb6Value((Avalonia.Controls.Control)control));
        return control;
    }

    // #1083 issuecomment-6427015606: interface names, picture types/sizes, keys/tags and For Each order.
    [Fact]
    public async Task Saved_images_are_available_to_the_interpreter()
    {
        Loaded();
        await Run("Debug.Print TypeName(il)\nDebug.Print TypeOf il Is MSComctlLib.ImageList\nDebug.Print TypeName(il.ListImages)\n" +
            "For Each li In il.ListImages\nDebug.Print TypeName(li), CStr(li.Index), li.Key, li.Tag, CStr(li.Picture.Type), CStr(li.Picture.Width)\nNext\n" +
            "Debug.Print CStr(il.ListImages!red.Index)\nDim images\nSet images = il.ListImages\nDebug.Print images(2).Key\n");
        AssertDebugLog(["ImageList", true, "IImages", "IImage", "1", "red", "t1", "1", "423",
            "IImage", "2", "blue", "", "1", "423", "IImage", "3", "ico", "t3", "3", "423", "1", "blue"]);
    }

    // #1083 issuecomment-6427015606: a String is always a key; Empty, Null, bounds and half-even indices.
    [Theory]
    [InlineData("\"missing\"", 35601)]
    [InlineData("\"1\"", 35601)]
    [InlineData("Empty", 35601)]
    [InlineData("\"\"", 35603)]
    [InlineData("Null", 35603)]
    [InlineData("0", 35600)]
    [InlineData("-1", 35600)]
    [InlineData("99", 35600)]
    [InlineData("True", 35600)]
    public async Task Lookup_and_remove_raise_the_measured_errors(string index, int error)
    {
        Loaded();
        await Run($"On Error Resume Next\nSet li = il.ListImages({index})\nDebug.Print Err.Number\nErr.Clear\nil.ListImages.Remove {index}\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value((long)error), new Vb6Value((long)error)]);
    }

    // #1083 issuecomment-6427015606: insertion, rounding, renumbering and Clear's size reset.
    [Fact]
    public async Task Add_remove_and_clear_update_indices_and_dimensions()
    {
        Loaded();
        await Run("Set p = il.ListImages(1).Picture\nil.ListImages.Clear\nDebug.Print il.ImageWidth, il.ImageHeight\n" +
            "Set li = il.ListImages.Add(, \"r\", p)\nSet li = il.ListImages.Add(, \"b\", p)\n" +
            "Set li = il.ListImages.Add(1, \"first\", p)\nDebug.Print il.ImageWidth, il.ImageHeight, CStr(li.Index)\n" +
            "Debug.Print il.ListImages(1.5).Key, il.ListImages(2.5).Key\nil.ListImages.Remove \"first\"\n" +
            "Debug.Print il.ListImages(1).Key, CStr(il.ListImages(2).Index)\nil.ListImages.Remove 1\nDebug.Print il.ListImages(1).Key\n");
        AssertDebugLog([0, 0, 16, 16, "1", "r", "r", "r", "2", "b"]);
    }

    // #1083 issuecomment-6427015606: duplicate/numeric keys, missing/invalid pictures, read-only members and circular Tag.
    [Theory]
    [InlineData("il.ListImages.Add , \"red\", p", 35602)]
    [InlineData("il.ListImages.Add , \"5\", p", 35603)]
    [InlineData("il.ListImages.Add , 7, p", 35603)]
    [InlineData("il.ListImages.Add 0, , p", 35600)]
    [InlineData("il.ListImages.Add 99, , p", 35600)]
    [InlineData("il.ListImages.Add , \"nopic\"", 35607)]
    [InlineData("il.ListImages.Add , , Nothing", 481)]
    [InlineData("il.ListImages.Add , , \"abc\"", 13)]
    [InlineData("li.Index = 1", 383)]
    [InlineData("Set li.Picture = Nothing", 383)]
    [InlineData("li.Key = \"blue\"", 35602)]
    [InlineData("Set li.Tag = il", 35700)]
    [InlineData("Set il.ListImages = Nothing", 383)]
    [InlineData("il.ListImages.Count = 3", 383)]
    [InlineData("il.ImageWidth = 32", 35611)]
    [InlineData("il.ListImages.Clear\nil.ImageWidth = -1", 380)]
    [InlineData("il.ListImages.Clear\nil.ImageWidth = 40000", 6)]
    public async Task Mutations_raise_the_measured_errors(string statement, int error)
    {
        Loaded();
        await Run("Set li = il.ListImages(1)\nSet p = li.Picture\nOn Error Resume Next\n" + statement + "\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value((long)error)]);
    }

    // Supervisor decisions on #1083: these members exist but refuse rather than fabricate a handle or picture.
    [Theory]
    [InlineData("Debug.Print il.hImageList")]
    [InlineData("il.ListImages(1).Draw 0")]
    [InlineData("Set p = il.Overlay(1, 2)")]
    [InlineData("Set p = il.ListImages(1).ExtractIcon")]
    public async Task Deferred_members_refuse_even_under_resume_next(string statement)
    {
        Loaded();
        await ((Func<Task>)(() => Run("On Error Resume Next\n" + statement + "\n"))).Should().ThrowAsync<NotImplementedException>();
    }

    // #1083 issuecomment-6427015606: defaults, writable mask/color properties, Variant tags and no default member.
    [Fact]
    public async Task Properties_tags_and_the_invisible_extender_match_the_probe()
    {
        Loaded();
        await Run("Debug.Print il.MaskColor, il.BackColor, il.UseMaskColor, TypeName(il.ListImages.Count)\n" +
            "il.MaskColor = &HFF00FF\nil.BackColor = vbRed\nil.UseMaskColor = False\n" +
            "Debug.Print il.MaskColor, il.BackColor, il.UseMaskColor\nSet li = il.ListImages(1)\n" +
            "li.Tag = 42\nli.Key = \"r2\"\nDebug.Print li.Tag, li.Key\nli.Key = \"\"\nDebug.Print li.Key\n" +
            "On Error Resume Next\nv = li\nDebug.Print Err.Number\nErr.Clear\n" +
            "Debug.Print il.Left\nDebug.Print Err.Number\nErr.Clear\nDebug.Print il.Index\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(12632256L), new Vb6Value(-2147483643L), true, "Integer",
            new Vb6Value(16711935L), new Vb6Value(255L), false, 42, "r2", "", new Vb6Value(438L), new Vb6Value(393L), new Vb6Value(343L)]);

        debug.Clear();
        Loaded("custom");
        await Run("Debug.Print il.ImageWidth, il.ImageHeight, il.UseMaskColor\n" +
            "Debug.Print CStr(il.ListImages(1).Picture.Width), CStr(il.ListImages(1).Picture.Height)\n");
        AssertDebugLog([24, 20, false, "847", "847"]);

        // These conversions/setters were not in the probe: they must not fabricate trappable VB6 errors.
        foreach (var unmeasured in new[]
        {
            "il.Left = 1", "il.UseMaskColor = 1", "il.ImageWidth = \"16\"", "il.ListImages.Add , , il",
            "Set li = il.ListImages(1)\nli.Key = \"caseKey\"\nSet p = il.ListImages(\"CASEKEY\")",
        })
        {
            context = new ModuleExecutionContext();
            rootEnv = new ExecutionEnvironment();
            Loaded();
            await ((Func<Task>)(() => Run("On Error Resume Next\n" + unmeasured + "\n"))).Should().ThrowAsync<NotImplementedException>();
        }
    }
}
