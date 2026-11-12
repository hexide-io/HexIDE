using System;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

using HexIDE.Integration.Tests.Views;

namespace HexIDE.Runtime.Integration.Tests.Views;

// Measurements: docs/vb6-fidelity-oracle/in-box-controls-the-listview.md, Icons and Drawing (#1085).
public class ListViewImageListTests
{
    private static Vb6Value Read(ICSharpPropertyBag bag, string name)
    {
        bag.TryGetProperty(name, out var value).Should().BeTrue();
        return value;
    }

    [AvaloniaTheory]
    [InlineData("Icons")]
    [InlineData("SmallIcons")]
    [InlineData("ColumnHeaderIcons")]
    public async Task Bindings_keep_the_object_and_prevent_removal(string member)
    {
        var list = new VBListView();
        var images = ToolbarImageListTests.Images();
        list.TrySetProperty(member, new Vb6Value((Control)images)).Should().BeTrue();
        Read(list, member).Value.Should().BeSameAs(images);
        var replace = () => list.TrySetProperty(member, new Vb6Value((Control)ToolbarImageListTests.Images()));
        replace.Should().Throw<NotImplementedException>();
        if (member != "Icons")
        {
            var clear = () => list.TrySetProperty(member, Vb6Value.Nothing);
            clear.Should().Throw<NotImplementedException>();
        }
        Read(list, member).Value.Should().BeSameAs(images);
        var unmeasured = ToolbarImageListTests.Images();
        unmeasured.ImageWidth = unmeasured.ImageHeight = 24;
        var other = new VBListView();
        var non16 = () => other.TrySetProperty(member, new Vb6Value((Control)unmeasured));
        non16.Should().Throw<NotImplementedException>();
        var empty = new VBImageList(new HexIDE.Runtime.Components.ComponentInstance(
            HexIDE.Runtime.Components.InBox.ImageListComponentClass.Instance, "ImageList1"));
        var bindEmpty = () => other.TrySetProperty(member, new Vb6Value((Control)empty));
        bindEmpty.Should().Throw<NotImplementedException>();
        var remove = () => images.Images.CallAsync("Remove", [new(3)]);
        (await remove.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(35617);
        if (member == "Icons")
        {
            list.TrySetProperty("Icons", Vb6Value.Nothing);
            await images.Images.CallAsync("Remove", [new(3)]);
            images.Images.Count.Should().Be(2);
        }
    }

    [AvaloniaFact]
    public void A_foreign_object_is_35610()
    {
        var list = new VBListView();
        var bind = () => list.TrySetProperty("Icons", new Vb6Value(new VBPictureBox()));
        bind.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(35610);
    }

    [AvaloniaFact]
    public async Task Selectors_preserve_types_and_unbinding_Icons_clears_only_Icon()
    {
        var list = new VBListView();
        var images = ToolbarImageListTests.Images();
        list.TrySetProperty("Icons", new Vb6Value((Control)images));
        list.TrySetProperty("SmallIcons", new Vb6Value((Control)images));
        list.TrySetProperty("ColumnHeaderIcons", new Vb6Value((Control)images));
        var item = (ListViewItem)(await list.Items.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Item"), new(1), new("red")]))!.Value.Value!;
        Read(item, "Icon").Should().Be(new Vb6Value(1));
        Read(item, "SmallIcon").Should().Be(new Vb6Value("red"));
        item.TrySetProperty("Icon", new("blue"));
        Read(item, "Icon").Should().Be(new Vb6Value("blue"));
        var bad = () => item.TrySetProperty("Icon", new(99));
        bad.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(35600);
        Read(item, "Icon").Should().Be(new Vb6Value("blue"));
        var badAdd = () => list.Items.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Bad"), new(99)]);
        (await badAdd.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(35600);
        list.Items.Entries.Should().ContainSingle();
        var column = (ListViewColumn)(await list.Columns.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Name"), Vb6Value.Missing, Vb6Value.Missing, new("ico")]))!.Value.Value!;
        Read(column, "Icon").Should().Be(new Vb6Value("ico"));
        var sub = (ListViewSubItem)(await item.SubItems.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Detail"), new(2)]))!.Value.Value!;
        Read(sub, "ReportIcon").Should().Be(new Vb6Value(2));
        list.TrySetProperty("Icons", Vb6Value.Nothing);
        Read(item, "Icon").Should().Be(new Vb6Value(0));
        Read(item, "SmallIcon").Should().Be(new Vb6Value("red"));
        var clearSelector = () => item.TrySetProperty("SmallIcon", new(0));
        clearSelector.Should().Throw<NotImplementedException>();
    }

    [AvaloniaTheory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(3L)]
    public async Task Each_view_draws_its_bound_image(long view)
    {
        var list = new VBListView();
        list.TrySetProperty("View", new(view));
        list.TrySetProperty("Icons", new Vb6Value((Control)ToolbarImageListTests.Images()));
        list.TrySetProperty("SmallIcons", new Vb6Value((Control)ToolbarImageListTests.Images()));
        await list.Columns.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Name")]);
        var item = (ListViewItem)(await list.Items.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Item"), new(1), new(1)]))!.Value.Value!;
        list.Measure(new Size(333, 180));
        list.Arrange(new Rect(0, 0, 333, 180));
        byte[] Draw(PixelRect? region = null)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(333, 180));
            bitmap.Render(list);
            if (region is { } rect)
            {
                var pixels = new byte[rect.Width * rect.Height * 4];
                var buffer = Marshal.AllocHGlobal(pixels.Length);
                try
                {
                    bitmap.CopyPixels(rect, buffer, pixels.Length, rect.Width * 4);
                    Marshal.Copy(buffer, pixels, 0, pixels.Length);
                    return pixels;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            using var stream = new MemoryStream();
            bitmap.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            return stream.ToArray();
        }
        var inset = HexIDE.Runtime.BuiltinControls.VBProps.GetClientInset(list);
        var iconBounds = list.GetItemIconBounds(0);
        iconBounds.Size.Should().Be(new Size(16, 16));
        iconBounds.Position.Should().Be(new Point(inset + (view == 0 ? 21 : 2), inset + (view == 0 ? 2 : view == 3 ? 17 : 0)));
        list.GetItemLabelBounds(0).Y.Should().BeGreaterThanOrEqualTo(iconBounds.Y);
        if (view != 0)
            list.GetItemLabelBounds(0).X.Should().BeGreaterThanOrEqualTo(iconBounds.Right);
        var before = Draw();
        item.TrySetProperty(view == 0 ? "Icon" : "SmallIcon", new("blue"));
        Draw().Should().NotEqual(before);
        var imageRegion = new PixelRect((int)iconBounds.X, (int)iconBounds.Y, 16, 16);
        var plainImage = Draw(imageRegion);
        item.TrySetProperty("Ghosted", new(true));
        Draw(imageRegion).Should().NotEqual(plainImage);
        item.TrySetProperty("Ghosted", new(false));
        list.TrySetProperty("HideSelection", new(false));
        Draw(imageRegion).Should().NotEqual(plainImage);
        for (var i = 1; i < 6; i++)
            await list.Items.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Item"), new(1), new(1)]);
        if (view == 0)
        {
            (list.GetItemIconBounds(1).X - iconBounds.X).Should().Be(59);
            (list.GetItemIconBounds(5).Y - iconBounds.Y).Should().Be(59);
        }
        else if (view == 1)
        {
            list.GetItemIconBounds(4).Y.Should().Be(iconBounds.Y);
            (list.GetItemIconBounds(5).Y - iconBounds.Y).Should().Be(17);
        }
        else
            (list.GetItemIconBounds(1).Y - iconBounds.Y).Should().Be(17);
    }
    [AvaloniaTheory]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public async Task Designed_bindings_resolve_and_the_form_code_completes(int appearance, int border)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Join(root.FullName, "corpus"))) root = root.Parent;
        var path = Path.Join(root!.FullName, "corpus", "inbox-controls", "saved", "imagelist", "three", "Form1.frm");
        var source = Vb6TextFile.ReadAllText(path).ReplaceLineEndings("\n");
        var start = source.IndexOf("   Begin MSComctlLib.ImageList", StringComparison.Ordinal);
        var end = source.IndexOf("\n   End", start, StringComparison.Ordinal) + "\n   End".Length;
        var images = source[start..end];
        source = $$"""
            VERSION 5.00
            Begin VB.Form Form1
               ClientHeight = 4500
               ClientWidth = 6000
               Begin MSComctlLib.ListView ListView1
                  Height = 2500
                  Width = 5000
                  Appearance = {{appearance}}
                  BorderStyle = {{border}}
                  View = 3
                  Icons = "ImageList1"
                  SmallIcons = "ImageList1"
                  ColHdrIcons = "ImageList1"
                  NumItems = 1
                  BeginProperty ColumnHeader(1) {BDD1F052-858B-11D1-B16A-00C0F0283628}
                     Text = "Name"
                     ImageIndex = 1
                  EndProperty
               End
            {{images}}
            End
            Attribute VB_Name = "Form1"
            """;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(
            project, source,
            new Sink(), FrxDeserializer.Read(File.ReadAllBytes(Path.ChangeExtension(path, ".frx")), source))!;
        var context = new ModuleExecutionContext();
        var environment = new ExecutionEnvironment();
        var panel = (DockPanel)HexIDE.Runtime.VBLoader.SpawnComponents(form, context, environment);
        var canvas = panel.Children.OfType<Canvas>().Single();
        var list = canvas.Children.OfType<VBListView>().Single();
        var bound = Read(list, "Icons").Value;
        Read(list, "SmallIcons").Value.Should().BeSameAs(bound);
        Read(list, "ColumnHeaderIcons").Value.Should().BeSameAs(bound);
        Read(list.Columns.Entries[0], "Icon").Should().Be(new Vb6Value(1));
        var printed = await ContainerRuntimeHarness.Run(context, environment, """
            ListView1.Icons = ImageList1
            Set ListView1.SmallIcons = ImageList1
            Set ListView1.ColumnHeaderIcons = ImageList1
            Set i = ListView1.ListItems.Add(, , "Item", 1, "red")
            i.ListSubItems.Add , , "Detail", 2
            Debug.Print i.Text, i.Icon, i.SmallIcon
            """);
        printed.Should().Equal(new Vb6Value("Item"), new Vb6Value(1), new Vb6Value("red"));
        foreach (var member in new[] { "SmallIcons", "ColumnHeaderIcons" })
        {
            var unmeasuredLet = () => ContainerRuntimeHarness.Run(context, environment, $"ListView1.{member} = ImageList1");
            await unmeasuredLet.Should().ThrowAsync<NotImplementedException>();
        }
        project.AddForm(form);
        form.UpdateCode("""
            Private Sub Form_Load()
                Dim i
                Set i = ListView1.ListItems.Add(, , "Loaded", 1, "red")
                i.ListSubItems.Add , , "Detail", 2
            End Sub
            """);
        using var cancel = new System.Threading.CancellationTokenSource();
        var execution = System.Threading.ExecutionContext.Capture();
        HexIDE.Runtime.VBFormRuntime? window = null;
        try
        {
            var run = HexIDE.Runtime.VBLoader.RunForm(form, cancel.Token, out window);
            Dispatcher.UIThread.RunJobs();
            if (execution is not null) System.Threading.ExecutionContext.Restore(execution);
            run.IsFaulted.Should().BeFalse(run.Exception?.ToString());
            var running = window.GetVisualDescendants().OfType<VBListView>().Single();
            running.Items.Entries.Should().ContainSingle().Which.Caption.Should().Be("Loaded");
            (await window.UnloadAsync(1)).Should().BeTrue();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            cancel.Cancel();
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            if (execution is not null) System.Threading.ExecutionContext.Restore(execution);
        }
    }

    [AvaloniaTheory]
    [InlineData("listview", 2)]
    [InlineData("listview-columns", 4)]
    public async Task Corpus_forms_run_to_completion(string folder, int columns)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Join(root.FullName, "corpus"))) root = root.Parent;
        var path = Path.Join(root!.FullName, "corpus", "inbox-controls", "saved", folder, "Form1.frm");
        var source = Vb6TextFile.ReadAllText(path);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new Sink(),
            FrxDeserializer.Read(File.ReadAllBytes(Path.ChangeExtension(path, ".frx")), source))!;
        project.AddForm(form);
        using var cancel = new System.Threading.CancellationTokenSource();
        var execution = System.Threading.ExecutionContext.Capture();
        HexIDE.Runtime.VBFormRuntime? window = null;
        try
        {
            var run = HexIDE.Runtime.VBLoader.RunForm(form, cancel.Token, out window);
            Dispatcher.UIThread.RunJobs();
            if (execution is not null) System.Threading.ExecutionContext.Restore(execution);
            run.IsFaulted.Should().BeFalse(run.Exception?.ToString());
            var lists = window.GetVisualDescendants().OfType<VBListView>().ToArray();
            lists.Should().HaveCount(3);
            var list = lists.Single(view => VBProps.GetName(view) == "ListView1");
            list.Columns.Entries.Should().HaveCount(columns);
            foreach (var member in new[] { "Icons", "SmallIcons", "ColumnHeaderIcons" })
                Read(list, member).Value.Should().BeOfType<VBImageList>();
            VBProps.GetClientInset(lists.Single(view => VBProps.GetName(view) == "ListView2")).Should().Be(2);
            (await window.UnloadAsync(1)).Should().BeTrue();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            cancel.Cancel();
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            if (execution is not null) System.Threading.ExecutionContext.Restore(execution);
        }
    }

    [AvaloniaFact]
    public async Task Header_and_report_cell_images_draw_at_the_left_even_with_alignment()
    {
        var list = new VBListView();
        list.TrySetProperty("View", new(3L));
        list.TrySetProperty("ColumnHeaderIcons", new Vb6Value((Control)ToolbarImageListTests.Images()));
        list.TrySetProperty("SmallIcons", new Vb6Value((Control)ToolbarImageListTests.Images()));
        await list.Columns.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Name")]);
        await list.Columns.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Size")]);
        var item = (ListViewItem)(await list.Items.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Item")]))!.Value.Value!;
        var sub = (ListViewSubItem)(await item.SubItems.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Detail"), new(2)]))!.Value.Value!;
        string Region(RenderTargetBitmap bitmap, PixelRect rect)
        {
            var pixels = new byte[rect.Width * rect.Height * 4];
            var buffer = Marshal.AllocHGlobal(pixels.Length);
            try
            {
                bitmap.CopyPixels(rect, buffer, pixels.Length, rect.Width * 4);
                Marshal.Copy(buffer, pixels, 0, pixels.Length);
                return Convert.ToHexString(SHA256.HashData(pixels));
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        string? header = null, cell = null;
        var column = list.Columns.Entries[1];
        column.TrySetProperty("Icon", new("ico"));
        foreach (var alignment in new[] { 0, 1, 2 })
        {
            column.TrySetProperty("Alignment", new(alignment));
            list.Measure(new Size(333, 180));
            list.Arrange(new Rect(0, 0, 333, 180));
            using var bitmap = new RenderTargetBitmap(new PixelSize(333, 180));
            bitmap.Render(list);
            var headerRegion = Region(bitmap, new PixelRect(101, 3, 16, 16));
            var cellRegion = Region(bitmap, new PixelRect(101, 20, 16, 16));
            if (header is not null) headerRegion.Should().Be(header);
            if (cell is not null) cellRegion.Should().Be(cell);
            header = headerRegion;
            cell = cellRegion;
            Read(column, "Icon").Should().Be(new Vb6Value("ico"));
            Read(sub, "ReportIcon").Should().Be(new Vb6Value(2));
        }
        column.TrySetProperty("Icon", new("blue"));
        sub.TrySetProperty("ReportIcon", new(1));
        using var changed = new RenderTargetBitmap(new PixelSize(333, 180));
        changed.Render(list);
        Region(changed, new PixelRect(101, 3, 16, 16)).Should().NotBe(header);
        Region(changed, new PixelRect(101, 20, 16, 16)).Should().NotBe(cell);
    }

    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) => throw new InvalidOperationException(message);
    }

}
