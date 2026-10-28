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

namespace HexIDE.Integration.Tests.Views;

// #1088 and #2334 ImageList probes, native and p-code. Painting is a separate slice.
public class ToolbarImageListTests
{
    private sealed class Sink : IDeserializeErrorSink { public void LogError(string _) { } }

    internal static VBImageList Images()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Join(root.FullName, "corpus"))) root = root.Parent;
        var path = Path.Join(root!.FullName, "corpus", "inbox-controls", "saved", "imagelist", "three", "Form1.frm");
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

    [AvaloniaFact]
    public void Unmeasured_missing_saved_list_refuses()
    {
        var load = () => ContainerRuntimeHarness.Spawn("""
            VERSION 5.00
            Begin VB.Form Form1
               Begin MSComctlLib.Toolbar Toolbar1
                  ImageList = "MissingList"
               End
            End
            Attribute VB_Name = "Form1"
            """);
        load.Should().Throw<NotImplementedException>();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_binding_resolves_in_either_file_order_and_preserves_the_files(bool imagesFirst)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Join(root.FullName, "corpus"))) root = root.Parent;
        var path = Path.Join(root!.FullName, "corpus", "inbox-controls", "saved", "toolbar", "Form1.frm");
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
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Join(root.FullName, "corpus"))) root = root.Parent;
        var path = Path.Join(root!.FullName, "corpus", "inbox-controls", "saved", "toolbar", "Form1.frm");
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
        var bar = new VBToolbar { Width = 800 };
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

    [AvaloniaFact]
    public void Alternate_image_bindings_remain_unimplemented()
    {
        var bar = new VBToolbar();
        foreach (var member in new[] { "HotImageList", "DisabledImageList" })
        {
            var alternate = () => bar.TrySetProperty(member, new Vb6Value((Control)Images()));
            alternate.Should().Throw<NotImplementedException>();
        }
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
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Join(root.FullName, "corpus"))) root = root.Parent;
        var path = Path.Join(root!.FullName, "corpus", "inbox-controls", "saved", "toolbar", "Form1.frm");
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
