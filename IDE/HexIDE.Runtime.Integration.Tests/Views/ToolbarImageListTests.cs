using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

using HexIDE.Integration.Tests;
using HexIDE.Integration.Tests.Views;

namespace HexIDE.Runtime.Integration.Tests.Views;

// #1088/#2334/#4407 ImageList probes, native and p-code; #4407 records oracle PR #5214.
public class ToolbarImageListTests
{
    private sealed class Sink : IDeserializeErrorSink { public void LogError(string _) { } }

    internal static VBImageList Images()
    {
        var root = RepoTree.Root();
        var path = Path.Join(root, "corpus", "inbox-controls", "saved", "imagelist", "three", "Form1.frm");
        var source = Vb6TextFile.ReadAllText(path);
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink(),
            FrxDeserializer.Read(File.ReadAllBytes(Path.ChangeExtension(path, ".frx")), source))!;
        var component = form.Components.Single(c => c.BaseClass.VBTypeName == "MSComctlLib.ImageList");
        return (VBImageList)((HexIDE.Runtime.Components.ComponentBaseClass)component.BaseClass).Instantiate(component);
    }

    private static Vb6Value Read(ICSharpPropertyBag bag, string name)
    {
        bag.TryGetProperty(name, out var value).Should().BeTrue();
        return value;
    }

    // Measured on vb6.exe SP6, native and p-code: hexide-io/HexIDE#4407, oracle PR #5214.
    [AvaloniaTheory]
    [InlineData("imagelist1", false, false)]
    [InlineData("NoSuchList", false, true)]
    [InlineData("Text1", false, true)]
    [InlineData("Toolbar2", false, true)]
    [InlineData("ImageList1", true, true)]
    public async Task Saved_names_resolve_case_insensitively_or_read_430(string name, bool array, bool unresolved)
    {
        var path = Path.Join(RepoTree.Root(), "corpus", "inbox-controls", "saved", "toolbar", "Form1.frm");
        var source = Vb6TextFile.ReadAllText(path).Replace("\"ImageList1\"", $"\"{name}\"");
        source = source.Replace("   Begin MSComctlLib.Toolbar Toolbar1", "   Begin VB.TextBox Text1\n   End\n   Begin MSComctlLib.Toolbar Toolbar1");
        if (array) source = source.Replace("Begin MSComctlLib.ImageList ImageList1", "Begin MSComctlLib.ImageList ImageList1\n      Index = 0");
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source,
            new Sink(), FrxDeserializer.Read(File.ReadAllBytes(Path.ChangeExtension(path, ".frx")), source))!;
        var context = new ModuleExecutionContext();
        var environment = new ExecutionEnvironment();
        HexIDE.Runtime.VBLoader.SpawnComponents(form, context, environment);
        var printed = await ContainerRuntimeHarness.Run(context, environment, """
            On Error Resume Next
            Debug.Print TypeName(Toolbar1.ImageList)
            Debug.Print Err.Number, Toolbar1.Buttons(1).Image, Toolbar1.Buttons(2).Image, Toolbar1.Buttons(7).Image
            """);
        printed.Should().Equal(unresolved
            ? new[] { new Vb6Value(430L), new Vb6Value(1), new Vb6Value(2), new Vb6Value(3) }
            : new[] { new Vb6Value("ImageList"), new Vb6Value(0L), new Vb6Value(1), new Vb6Value(2), new Vb6Value(3) });
    }

    [AvaloniaTheory]
    [InlineData("ImageList", false)]
    [InlineData("ImageList", true)]
    [InlineData("HotImageList", false)]
    [InlineData("HotImageList", true)]
    [InlineData("DisabledImageList", false)]
    [InlineData("DisabledImageList", true)]
    public async Task Let_binds_and_rebinding_preserves_all_images(string member, bool let)
    {
        var context = new ModuleExecutionContext();
        var environment = new ExecutionEnvironment();
        var bar = new VBToolbar();
        var images = Images();
        context.AllocVariable(environment, "bar", new Vb6Value(bar));
        context.AllocVariable(environment, "images", new Vb6Value((Control)images));
        var printed = await ContainerRuntimeHarness.Run(context, environment, $$"""
            {{(let ? "" : "Set ")}}bar.{{member}} = images
            Set bar.ImageList = images
            Set b = bar.Buttons.Add(, , , , 1)
            Set c = bar.Buttons.Add(, , , , "blue")
            Set bar.{{member}} = images
            Debug.Print b.Image, c.Image
            {{(let ? "" : "Set ")}}bar.{{member}} = images
            Debug.Print b.Image, c.Image
            """);
        printed.Should().Equal(new Vb6Value(1), new Vb6Value("blue"), new Vb6Value(1), new Vb6Value("blue"));
        Read(bar, member).Value.Should().BeSameAs(images);
    }

    [AvaloniaTheory]
    [InlineData("ImageList", "text")]
    [InlineData("ImageList", "other")]
    [InlineData("ImageList", "New Collection")]
    [InlineData("HotImageList", "text")]
    [InlineData("HotImageList", "other")]
    [InlineData("HotImageList", "New Collection")]
    [InlineData("DisabledImageList", "text")]
    [InlineData("DisabledImageList", "other")]
    [InlineData("DisabledImageList", "New Collection")]
    [InlineData("ImageList", "picture")]
    [InlineData("HotImageList", "picture")]
    [InlineData("DisabledImageList", "picture")]
    public async Task Non_list_objects_raise_35610_and_preserve_binding(string member, string expression)
    {
        var context = new ModuleExecutionContext();
        var environment = new ExecutionEnvironment();
        var bar = new VBToolbar();
        var images = Images();
        bar.TrySetProperty("ImageList", new Vb6Value((Control)images));
        bar.TrySetProperty(member, new Vb6Value((Control)images));
        context.AllocVariable(environment, "bar", new Vb6Value(bar));
        context.AllocVariable(environment, "text", new Vb6Value(new VBTextBox()));
        context.AllocVariable(environment, "other", new Vb6Value(new VBToolbar()));
        context.AllocVariable(environment, "picture", new Vb6Value(new VBPictureBox()));
        var printed = await ContainerRuntimeHarness.Run(context, environment, $$"""
            On Error Resume Next
            Set bar.{{member}} = {{expression}}
            Debug.Print Err.Number
            """);
        printed.Should().Equal(new Vb6Value(35610L));
        Read(bar, member).Value.Should().BeSameAs(images);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_binding_resolves_in_either_file_order_and_preserves_the_files(bool imagesFirst)
    {
        var root = RepoTree.Root();
        var path = Path.Join(root, "corpus", "inbox-controls", "saved", "toolbar", "Form1.frm");
        var original = File.ReadAllBytes(path);
        var companion = File.ReadAllBytes(Path.ChangeExtension(path, ".frx"));
        var source = Vb6TextFile.ReadDocument(original).Text;
        if (imagesFirst)
        {
            var start = source.IndexOf("   Begin MSComctlLib.ImageList", StringComparison.Ordinal);
            var end = source.IndexOf("\r\n   End\r\n", start, StringComparison.Ordinal) + "\r\n   End\r\n".Length;
            var block = source[start..end];
            source = source.Remove(start, end - start);
            source = source.Insert(source.IndexOf("   Begin MSComctlLib.Toolbar", StringComparison.Ordinal), block);
        }
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source,
            new Sink(), FrxDeserializer.Read(companion, source))!;
        var context = new ModuleExecutionContext();
        var environment = new ExecutionEnvironment();
        var loaded = (DockPanel)HexIDE.Runtime.VBLoader.SpawnComponents(form, context, environment);
        var controls = loaded.Children.OfType<Canvas>().Single().Children;
        var bar = controls.OfType<VBToolbar>().Single(b => HexIDE.Runtime.BuiltinControls.VBProps.GetName(b) == "Toolbar1");
        var images = controls.OfType<VBImageList>().Single();
        Read(bar, "ImageList").Value.Should().BeSameAs(images);
        Read(controls.OfType<VBToolbar>().Single(b => b != bar), "ImageList").Value.Should().BeNull();
        foreach (var (key, index) in new[] { ("new", 1), ("bold", 2), ("drop", 3) })
            Read((ToolbarItem)bar.Buttons.Item([new Vb6Value(key)]).Value!, "Image").Should().Be(new Vb6Value(index));
        var printed = await ContainerRuntimeHarness.Run(context, environment, """
            Toolbar1.Buttons("new").Image = "blue"
            Debug.Print Toolbar1.Buttons("new").Image
            """);
        printed.Should().Equal(new Vb6Value("blue"));
        var shrink = () => images.Images.CallAsync("Clear", []);
        (await shrink.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(35617);
        var saved = new FormSerializer().Serialize(form, "Form1.frm");
        System.Text.Encoding.ASCII.GetBytes(saved.frmText).Should().Equal(
            imagesFirst ? System.Text.Encoding.ASCII.GetBytes(source) : original);
        saved.frxContent.Should().Equal(companion);
    }

    // The oracle's probe set all three lists from code. Only ImageList reached the Toolbar, and the others were error 91
    // (#2334 live check).
    [AvaloniaFact]
    public async Task Hot_and_disabled_lists_are_set_from_code()
    {
        var root = RepoTree.Root();
        var path = Path.Join(root, "corpus", "inbox-controls", "saved", "toolbar", "Form1.frm");
        var source = Vb6TextFile.ReadAllText(path);
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink(),
            FrxDeserializer.Read(File.ReadAllBytes(Path.ChangeExtension(path, ".frx")), source))!;
        var context = new ModuleExecutionContext();
        var environment = new ExecutionEnvironment();
        var loaded = (DockPanel)HexIDE.Runtime.VBLoader.SpawnComponents(form, context, environment);
        var controls = loaded.Children.OfType<Canvas>().Single().Children;
        var bar = controls.OfType<VBToolbar>().Single(b => HexIDE.Runtime.BuiltinControls.VBProps.GetName(b) == "Toolbar1");
        var images = controls.OfType<VBImageList>().Single();
        await ContainerRuntimeHarness.Run(context, environment, """
            Set Toolbar1.HotImageList = ImageList1
            Set Toolbar1.DisabledImageList = ImageList1
            """);
        bar.HotImageList.Should().BeSameAs(images);
        bar.DisabledImageList.Should().BeSameAs(images);
    }

    [AvaloniaTheory]
    [InlineData("ImageList")]
    [InlineData("HotImageList")]
    [InlineData("DisabledImageList")]
    public async Task Saved_toolbar_accepts_list_Let_without_an_Object_receiver(string member)
    {
        var path = Path.Join(RepoTree.Root(), "corpus", "inbox-controls", "saved", "toolbar", "Form1.frm");
        var source = Vb6TextFile.ReadAllText(path);
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source,
            new Sink(), FrxDeserializer.Read(File.ReadAllBytes(Path.ChangeExtension(path, ".frx")), source))!;
        var context = new ModuleExecutionContext();
        var environment = new ExecutionEnvironment();
        HexIDE.Runtime.VBLoader.SpawnComponents(form, context, environment);
        var printed = await ContainerRuntimeHarness.Run(context, environment, $$"""
            Toolbar1.{{member}} = ImageList1
            Debug.Print TypeName(Toolbar1.{{member}}), Toolbar1.Buttons(1).Image
            """);
        printed.Should().Equal(new Vb6Value("ImageList"), new Vb6Value(1));
    }

    [AvaloniaFact]
    public void Defaults_and_invalid_bindings_match_the_probe()
    {
        var bar = new VBToolbar();
        foreach (var name in new[] { "ImageList", "HotImageList", "DisabledImageList" })
            Read(bar, name).Value.Should().BeNull();
        var invalid = () => bar.TrySetProperty("ImageList", new Vb6Value(new VBPictureBox()));
        invalid.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(35610);
        Read(bar, "ImageList").Value.Should().BeNull();
        var let = () => bar.TrySetProperty("ImageList", new Vb6Value("ImageList1"));
        let.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(13);
    }

    [AvaloniaFact]
    public async Task Index_key_and_zero_assignments_preserve_the_measured_types()
    {
        var bar = new VBToolbar();
        var images = Images();
        bar.TrySetProperty("ImageList", new Vb6Value((Control)images)).Should().BeTrue();
        Read(bar, "ImageList").Value.Should().BeSameAs(images);
        var button = (ToolbarItem)(await bar.Buttons.CallAsync("Add", []))!.Value.Value!;
        button.TrySetProperty("Image", new Vb6Value(1));
        Read(button, "Image").Should().Be(new Vb6Value(1));
        button.TrySetProperty("Image", new Vb6Value("blue"));
        Read(button, "Image").Should().Be(new Vb6Value("blue"));
        button.TrySetProperty("Image", new Vb6Value(0));
        Read(button, "Image").Should().Be(new Vb6Value(0));
        var added = (ToolbarItem)(await bar.Buttons.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing,
            Vb6Value.Missing, Vb6Value.Missing, new Vb6Value("blue")]))!.Value.Value!;
        Read(added, "Image").Should().Be(new Vb6Value("blue"));
    }

    [AvaloniaTheory]
    [InlineData(false, 35600)]
    [InlineData(true, 35601)]
    public async Task Invalid_images_leave_the_previous_image_and_collection_unchanged(bool key, int error)
    {
        var bar = new VBToolbar();
        bar.TrySetProperty("ImageList", new Vb6Value((Control)Images()));
        var button = (ToolbarItem)(await bar.Buttons.CallAsync("Add", []))!.Value.Value!;
        button.TrySetProperty("Image", new Vb6Value(1));
        var image = key ? new Vb6Value("missing") : new Vb6Value(99);
        var assign = () => button.TrySetProperty("Image", image);
        assign.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(error);
        Read(button, "Image").Should().Be(new Vb6Value(1));
        var add = () => bar.Buttons.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing,
            Vb6Value.Missing, Vb6Value.Missing, image]);
        await add.Should().ThrowAsync<VBRunTimeException>();
        Read(bar.Buttons, "Count").Value.Should().Be(1);
    }

    [AvaloniaFact]
    public async Task Interpreter_can_bind_select_and_unbind()
    {
        var context = new ModuleExecutionContext();
        var environment = new ExecutionEnvironment();
        context.AllocVariable(environment, "bar", new Vb6Value(new VBToolbar()));
        context.AllocVariable(environment, "images", new Vb6Value((Control)Images()));
        var printed = await ContainerRuntimeHarness.Run(context, environment, """
            Set bar.ImageList = images
            Set b = bar.Buttons.Add(, , , , "blue")
            Debug.Print b.Image, TypeName(b.Image)
            b.Image = 1
            Debug.Print b.Image, TypeName(b.Image)
            Set bar.ImageList = Nothing
            Debug.Print b.Image, TypeName(b.Image)
            """);
        printed.Should().Equal(new Vb6Value("blue"), new Vb6Value("String"), new Vb6Value(1),
            new Vb6Value("Integer"), new Vb6Value(0), new Vb6Value("Integer"));
    }

    [AvaloniaFact]
    public void Empty_list_binding_raises_35613_and_leaves_the_bar_unbound()
    {
        var bar = new VBToolbar();
        var empty = new VBImageList(new HexIDE.Runtime.Components.ComponentInstance(
            HexIDE.Runtime.Components.InBox.ImageListComponentClass.Instance, "ImageList1"));
        var bind = () => bar.TrySetProperty("ImageList", new Vb6Value((Control)empty));
        bind.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(35613);
        Read(bar, "ImageList").Value.Should().BeNull();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replacing_a_list_resets_selectors_resizes_and_releases_the_old_list(bool key)
    {
        var bar = new VBToolbar();
        var old = Images();
        bar.TrySetProperty("ImageList", new Vb6Value((Control)old));
        var button = (ToolbarItem)(await bar.Buttons.CallAsync("Add", [Vb6Value.Missing,
            Vb6Value.Missing, new Vb6Value("One")]))!.Value.Value!;
        button.TrySetProperty("Image", key ? new Vb6Value("blue") : new Vb6Value(1));
        var replacement = Images();
        replacement.ImageWidth = replacement.ImageHeight = 32;
        bar.TrySetProperty("ImageList", new Vb6Value((Control)replacement));
        Read(bar, "ImageList").Value.Should().BeSameAs(replacement);
        Read(button, "Image").Should().Be(new Vb6Value(0));
        Convert.ToSingle(Read(bar, "ButtonHeight").Value).Should().Be(54 * 15);
        await old.Images.CallAsync("Clear", []);
        var shrink = () => replacement.Images.CallAsync("Clear", []);
        (await shrink.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(35617);
    }

    [AvaloniaTheory]
    [InlineData(16, 0, 38, 42)]
    [InlineData(16, 1, 22, 26)]
    [InlineData(32, 0, 54, 58)]
    public async Task Bound_image_geometry_keeps_caption_widths_and_uniform_button_space(int size, int alignment, int height, int barHeight)
    {
        // #2334 used Align=0 bars; aligned heights are measured separately in #4802.
        var bar = new VBToolbar { Width = 800, LoadedAlign = 0 };
        bar.TrySetProperty("TextAlignment", new Vb6Value(alignment));
        var images = Images();
        images.ImageWidth = images.ImageHeight = size;
        var button = (ToolbarItem)(await bar.Buttons.CallAsync("Add", [Vb6Value.Missing,
            Vb6Value.Missing, new Vb6Value("One")]))!.Value.Value!;
        var before = Convert.ToSingle(Read(bar, "ButtonWidth").Value);
        bar.TrySetProperty("ImageList", new Vb6Value((Control)images));
        Convert.ToSingle(Read(bar, "ButtonWidth").Value).Should().Be(Math.Max(before, (size + 7) * 15));
        Convert.ToSingle(Read(bar, "ButtonHeight").Value).Should().Be(height * 15);
        Convert.ToSingle(Read(bar, "Height").Value).Should().Be(barHeight * 15);
        var blank = (ToolbarItem)(await bar.Buttons.CallAsync("Add", []))!.Value.Value!;
        Read(blank, "Height").Should().Be(Read(button, "Height"));
        bar.TrySetProperty("Style", new Vb6Value(1));
        Convert.ToSingle(Read(bar, "Height").Value).Should().Be(height * 15);
        Convert.ToSingle(Read(button, "Top").Value).Should().Be(0);
    }

    [AvaloniaTheory]
    [InlineData(24, 0, "One")]
    [InlineData(32, 1, "One")]
    [InlineData(32, 0, "")]
    public async Task Unmeasured_image_geometry_refuses(int size, int alignment, string caption)
    {
        var bar = new VBToolbar();
        bar.TrySetProperty("TextAlignment", new Vb6Value(alignment));
        await bar.Buttons.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new Vb6Value(caption)]);
        var images = Images();
        images.ImageWidth = images.ImageHeight = size;
        var bind = () => bar.TrySetProperty("ImageList", new Vb6Value((Control)images));
        bind.Should().Throw<NotImplementedException>();
    }

    // Measured on vb6.exe SP6, native and p-code: hexide-io/HexIDE#4147.
    [AvaloniaTheory]
    [InlineData("False", 0, 0)]
    [InlineData("#1/2/1900#", 0, 3)]
    [InlineData("2@", 0, 2)]
    [InlineData("2#", 0, 2)]
    [InlineData("CSng(2)", 0, 2)]
    [InlineData("CByte(2)", 0, 2)]
    [InlineData("CCur(2)", 0, 2)]
    [InlineData("CDec(2)", 0, 2)]
    [InlineData("1.6#", 0, 2)]
    [InlineData("2.5#", 0, 2)]
    [InlineData("3E0", 0, 3)]
    [InlineData("0", 0, 0)]
    [InlineData("Empty", 0, 0)]
    [InlineData("True", 35600, 1)]
    [InlineData("-1", 35600, 1)]
    [InlineData("4", 35600, 1)]
    [InlineData("Null", 35603, 1)]
    [InlineData("\"\"", 35603, 1)]
    [InlineData("\"blue\"", 0, "blue")]
    [InlineData("\"BLUE\"", 35601, 1)]
    [InlineData("\"Blue\"", 35601, 1)]
    [InlineData("\"Nope\"", 35601, 1)]
    [InlineData("\"2\"", 35601, 1)]
    public async Task Measured_image_selectors_validate_assignment_and_add(string expression, int error, object expected)
    {
        var context = new ModuleExecutionContext();
        var environment = new ExecutionEnvironment();
        context.AllocVariable(environment, "bar", new Vb6Value(new VBToolbar()));
        context.AllocVariable(environment, "images", new Vb6Value((Control)Images()));
        var printed = await ContainerRuntimeHarness.Run(context, environment, $$"""
            Set bar.ImageList = images
            Set b = bar.Buttons.Add(, , , , 1)
            On Error Resume Next
            b.Image = {{expression}}
            Debug.Print Err.Number, b.Image, TypeName(b.Image)
            Err.Clear
            Set added = bar.Buttons.Add(, , , , {{expression}})
            Debug.Print Err.Number, bar.Buttons.Count, b.Image
            If Err.Number = 0 Then Debug.Print added.Image, TypeName(added.Image)
            """);
        var value = expected is string text ? new Vb6Value(text) : new Vb6Value((int)expected);
        var type = new Vb6Value(expected is string ? "String" : "Integer");
        var wanted = new[] { new Vb6Value((long)error), value, type, new Vb6Value((long)error),
            new Vb6Value(error == 0 ? 2 : 1), value };
        printed.Should().Equal(error == 0 ? wanted.Concat(new[] { value, type }) : wanted);
    }

    [AvaloniaTheory]
    [InlineData("HotImageList")]
    [InlineData("DisabledImageList")]
    public async Task Measured_alternate_bindings_preserve_image_and_refused_assignments(string member)
    {
        var bar = new VBToolbar();
        bar.TrySetProperty("ImageList", new Vb6Value((Control)Images()));
        var button = (ToolbarItem)(await bar.Buttons.CallAsync("Add", []))!.Value.Value!;
        button.TrySetProperty("Image", new Vb6Value(1));
        var alternate = Images();
        await alternate.Images.CallAsync("Remove", [new Vb6Value(3)]);
        await alternate.Images.CallAsync("Remove", [new Vb6Value(2)]);
        bar.TrySetProperty(member, new Vb6Value((Control)alternate)).Should().BeTrue();
        Read(bar, member).Value.Should().BeSameAs(alternate);
        var empty = Images();
        await empty.Images.CallAsync("Clear", []);
        foreach (var (value, error) in new[] { (new Vb6Value("x"), 13),
            (new Vb6Value((Control)empty), 380), (new Vb6Value(new VBPictureBox()), 35610) })
        {
            var assign = () => bar.TrySetProperty(member, value);
            assign.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(error);
            // Fidelity Principle decision in #4147: refuse PictureBox without VB6's clear-on-error defect.
            Read(bar, member).Value.Should().BeSameAs(alternate);
            Read(button, "Image").Should().Be(new Vb6Value(1));
        }
        bar.TrySetProperty(member, Vb6Value.Nothing).Should().BeTrue();
        Read(bar, member).Value.Should().BeNull();
        Read(button, "Image").Should().Be(new Vb6Value(1));
    }

    [AvaloniaTheory]
    [InlineData("HotImageList", 16, false)]
    [InlineData("DisabledImageList", 16, false)]
    [InlineData("HotImageList", 32, false)]
    [InlineData("DisabledImageList", 32, false)]
    [InlineData("HotImageList", 16, true)]
    [InlineData("DisabledImageList", 16, true)]
    public async Task Alternate_size_mismatch_preserves_binding_and_empty_32_still_refuses(string member, int normalSize, bool empty)
    {
        var bar = new VBToolbar();
        var normal = Images();
        normal.ImageWidth = normal.ImageHeight = normalSize;
        bar.TrySetProperty("ImageList", new Vb6Value((Control)normal));
        if (!empty && normalSize == 16) bar.TrySetProperty(member, new Vb6Value((Control)normal));
        var alternate = Images();
        if (empty) await alternate.Images.CallAsync("Clear", []);
        alternate.ImageWidth = alternate.ImageHeight = normalSize == 16 ? 32 : 16;
        var assign = () => bar.TrySetProperty(member, new Vb6Value((Control)alternate));
        if (empty) assign.Should().Throw<NotImplementedException>();
        else assign.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(380);
        Read(bar, member).Value.Should().BeSameAs(!empty && normalSize == 16 ? normal : null);
    }

    [AvaloniaFact]
    public async Task Bound_list_can_grow_but_cannot_shrink_until_unbound()
    {
        var bar = new VBToolbar();
        var images = Images();
        bar.TrySetProperty("ImageList", new Vb6Value((Control)images));
        var button = (ToolbarItem)(await bar.Buttons.CallAsync("Add", []))!.Value.Value!;
        button.TrySetProperty("Image", new Vb6Value("blue"));
        foreach (var method in new[] { "Remove", "Clear" })
        {
            var shrink = () => images.Images.CallAsync(method, method == "Remove" ? [new Vb6Value(1)] : []);
            (await shrink.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(35617);
            images.Images.Count.Should().Be(3);
        }
        var first = (ImageListImage)images.Images.Item(new Vb6Value(1)).Value!;
        await images.Images.CallAsync("Add", [Vb6Value.Missing, new Vb6Value("extra"), Read(first, "Picture")]);
        images.Images.Count.Should().Be(4);
        bar.TrySetProperty("ImageList", Vb6Value.Nothing);
        Read(button, "Image").Should().Be(new Vb6Value(0));
        await images.Images.CallAsync("Remove", [new Vb6Value(4)]);
        var assign = () => button.TrySetProperty("Image", new Vb6Value(1));
        assign.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(35613);
        bar.TrySetProperty("ImageList", new Vb6Value((Control)images));
        Read(button, "Image").Should().Be(new Vb6Value(0));
        bar.TrySetProperty("ImageList", Vb6Value.Nothing);
        await images.Images.CallAsync("Clear", []);
        images.Images.Count.Should().Be(0);
    }

    // F5 on VB6's own save. Shown, the form attaches its Toolbars to the window, and their inherited font arrives as they
    // attach; refreshing the buttons there stopped the form loading (#2334 live check).
    [AvaloniaFact]
    public void Vb6s_saved_form_runs_in_a_shown_window()
    {
        var root = RepoTree.Root();
        var path = Path.Join(root, "corpus", "inbox-controls", "saved", "toolbar", "Form1.frm");
        var source = Vb6TextFile.ReadAllText(path);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new Sink(),
            FrxDeserializer.Read(File.ReadAllBytes(Path.ChangeExtension(path, ".frx")), source))!;
        project.AddForm(form);
        using var cancel = new System.Threading.CancellationTokenSource();
        var context = System.Threading.ExecutionContext.Capture();
        HexIDE.Runtime.VBFormRuntime? window = null;
        try
        {
            var run = HexIDE.Runtime.VBLoader.RunForm(form, cancel.Token, out window);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            run.IsFaulted.Should().BeFalse(run.Exception?.ToString());
            Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<VBToolbar>().Should().HaveCount(2);
        }
        finally
        {
            cancel.Cancel();
            window?.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (context != null) System.Threading.ExecutionContext.Restore(context);
        }
    }
}
