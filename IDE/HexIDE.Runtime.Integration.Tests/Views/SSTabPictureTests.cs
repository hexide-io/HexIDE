using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Integration.Tests;
using HexIDE.Integration.Tests.Views;

namespace HexIDE.Runtime.Integration.Tests.Views;

// VB6 SP6 native/p-code measurements: #3426, including the corrected control rows (#4297).
public class SSTabPictureTests : WindowTestFixture
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin TabDlg.SSTab SSTab1
              Width = 1200
              Height = 600
           End
           Begin VB.PictureBox Picture1
              Width = 1200
              Height = 600
           End
           Begin VB.Image Image1
              Width = 1200
              Height = 600
           End
           Begin VB.TextBox Text1
              Width = 1200
              Height = 600
           End
           Begin VB.CommandButton Command1
              Width = 1200
              Height = 600
           End
           Begin VB.Label Label1
              Width = 1200
              Height = 600
           End
        End
        Attribute VB_Name = "Form1"
        """;

    internal static byte[] RedBitmap()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x4d42); writer.Write(822); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(16); writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)24);
        writer.Write(new byte[24]);
        for (var i = 0; i < 256; i++) { writer.Write((byte)0); writer.Write((byte)0); writer.Write((byte)255); }
        return stream.ToArray();
    }

    [AvaloniaFact]
    public async Task EmptyPicturesAndCurrentPictureAliasAndShrink()
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env, """
            Dim a As Object
            Dim missing As Object
            Set a = SSTab1.TabPicture(0)
            Debug.Print TypeName(a)
            Debug.Print a.Type
            Debug.Print a Is Nothing
            Debug.Print a Is SSTab1.TabPicture(0)
            Debug.Print a Is SSTab1.Picture
            Set SSTab1.TabPicture(1) = a
            Debug.Print SSTab1.TabPicture(1) Is a
            SSTab1.Tab = 2
            Set SSTab1.Picture = a
            Debug.Print SSTab1.TabPicture(2) Is a
            SSTab1.Tab = 0
            SSTab1.Tabs = 2
            On Error Resume Next
            Set missing = SSTab1.TabPicture(2)
            Debug.Print Err.Number
            Err.Clear
            SSTab1.Tabs = 3
            Debug.Print SSTab1.TabPicture(2).Type
            Debug.Print SSTab1.TabPicture(2) Is a
            Set SSTab1.Picture = Nothing
            Debug.Print SSTab1.Picture.Type
            Debug.Print SSTab1.Picture Is Nothing
            """);
        values.Select(v => v.Value).Should().Equal("Picture", (short)0, false, true, true, true, true, 381L, (short)0, false, (short)0, false);
    }

    [AvaloniaTheory]
    [InlineData("1.6", 2)]
    [InlineData("2.5", 2)]
    [InlineData("\"1\"", 1)]
    public async Task PictureIndexesConvertAsCInt(string index, int rounded)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env, $"""
            Dim a As Object
            Set a = LoadPicture()
            Set SSTab1.TabPicture({index}) = a
            Debug.Print SSTab1.TabPicture({rounded}) Is a
            Debug.Print SSTab1.TabPicture({index}) Is a
            """);
        values.Select(v => v.Value).Should().Equal(true, true);
    }

    [AvaloniaTheory]
    [InlineData("Set", "Picture1", false)]
    [InlineData("Set", "Picture1", true)]
    [InlineData("", "Picture1", false)]
    [InlineData("", "Picture1", true)]
    [InlineData("Set", "Image1", false)]
    [InlineData("Set", "Image1", true)]
    [InlineData("", "Image1", false)]
    [InlineData("", "Image1", true)]
    [InlineData("Set", "a", true)]
    [InlineData("", "a", true)]
    public async Task AssignmentsAcceptPicturesAndCopyControlPictures(string verb, string source, bool filled)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bmp");
        try
        {
            File.WriteAllBytes(path, RedBitmap());
            var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
            var values = await ContainerRuntimeHarness.Run(ctx, env, $"""
                Dim a As Object
                Set a = LoadPicture({(filled ? "\"" + path + "\"" : "")})
                Set Picture1.Picture = a
                Set Image1.Picture = a
                {verb} SSTab1.TabPicture(0) = {source}
                Debug.Print SSTab1.TabPicture(0).Type
                Debug.Print SSTab1.TabPicture(0) Is {source}{(source == "a" ? "" : ".Picture")}
                SSTab1.Picture = a
                Debug.Print SSTab1.Picture Is a
                Set SSTab1.TabPicture(0) = Nothing
                Debug.Print SSTab1.TabPicture(0).Type
                Set SSTab1.TabPicture(0) = LoadPicture()
                Debug.Print SSTab1.TabPicture(0).Type
                """);
            values.Select(v => v.Value).Should().Equal((short)(filled ? 1 : 0), source == "a", true, (short)0, (short)0);
        }
        finally { File.Delete(path); }
    }

    // Measured with #4297's probe, native and p-code: a control's Picture reads as one object (`self`, `selfi`), and a tab
    // given the control holds an object of its own (`a0is`). Each read making a new object passed the last row by accident.
    [AvaloniaFact]
    public async Task AControlsPictureReadsAsOneObjectAndTheTabHoldsItsOwn()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bmp");
        try
        {
            File.WriteAllBytes(path, RedBitmap());
            var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
            var values = await ContainerRuntimeHarness.Run(ctx, env, $"""
                Set Picture1.Picture = LoadPicture("{path}")
                Set Image1.Picture = LoadPicture("{path}")
                Debug.Print Picture1.Picture Is Picture1.Picture
                Debug.Print Image1.Picture Is Image1.Picture
                Set SSTab1.TabPicture(0) = Picture1
                Debug.Print SSTab1.TabPicture(0) Is Picture1.Picture
                Set SSTab1.TabPicture(1) = Image1
                Debug.Print SSTab1.TabPicture(1) Is Image1.Picture
                """);
            values.Select(v => v.Value).Should().Equal(true, true, false, false);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaTheory]
    [InlineData("\"x\"")]
    [InlineData("5")]
    [InlineData("Text1")]
    [InlineData("Label1")]
    public async Task InvalidPictureRaises13AndKeepsTheOldObject(string source)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env, $"""
            Dim a As Object
            Dim t As Object
            Set t = SSTab1
            Set a = t.TabPicture(0)
            On Error Resume Next
            {(source is "5" or "\"x\"" ? "" : "Set")} t.TabPicture(0) = {source}
            Debug.Print Err.Number
            Debug.Print t.TabPicture(0) Is a
            """);
        values.Select(v => v.Value).Should().Equal(13L, true);
    }

    [AvaloniaFact]
    public async Task OtherControlPictureAssignmentsRemainUnmeasured()
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        Func<Task> run = async () => await ContainerRuntimeHarness.Run(ctx, env,
            "Set SSTab1.TabPicture(0) = Command1");
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*unmeasured (#3426)");
    }

    private sealed class NullSink : HexIDE.Runtime.Serialization.IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    [AvaloniaFact]
    public void FormAssignmentRaises13()
    {
        var project = new HexIDE.Runtime.ProjectElements.ProjectDefinition(HexIDE.Runtime.ProjectElements.VBProjectType.EXE, "P");
        var form = new HexIDE.Runtime.Serialization.FormDeserializer().Deserialize(project, (Form + "\n" + """

            Private Sub Form_Load()
                Dim a As Object
                Set a = SSTab1.Picture
                On Error Resume Next
                Set SSTab1.TabPicture(0) = Me
                Debug.Print Err.Number
                Debug.Print SSTab1.Picture Is a
            End Sub
            """ + "\n").ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form);
        var printed = new System.Collections.Generic.List<string>();
        HexIDE.Runtime.VBDebugConsole.Output += printed.Add;
        try
        {
            HexIDE.Runtime.VBLoader.RunForm(form, System.Threading.CancellationToken.None, out var window);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.Close();
            printed.Select(s => s.Trim()).Should().Equal("13", "True");
        }
        finally { HexIDE.Runtime.VBDebugConsole.Output -= printed.Add; }
    }

    [AvaloniaTheory]
    [InlineData("Set a = SSTab1.TabPicture(-1)")]
    [InlineData("Set a = SSTab1.TabPicture(3)")]
    [InlineData("Set SSTab1.TabPicture(-1) = Nothing")]
    [InlineData("Set SSTab1.TabPicture(3) = Nothing")]
    public async Task InvalidIndexRaises381(string statement)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env,
            "Dim a As Object\nOn Error Resume Next\n" + statement + "\nDebug.Print Err.Number");
        values.Single().Value.Should().Be(381L);
    }
}
