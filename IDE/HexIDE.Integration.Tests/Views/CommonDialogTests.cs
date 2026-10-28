using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The CommonDialog control (#1084): ShowOpen, ShowSave and ShowFont, through the IDE's own dialogs. Every expected value
/// is what vb6.exe gave for the same program, as p-code on Windows 10 (the oracle's "The CommonDialog control").
/// </summary>
public class CommonDialogTests
{
    [AvaloniaFact]
    public void Open_passes_every_selected_file_from_the_IDE_picker()
    {
        var windows = Opening();
        windows.OpenFilePickerAsync(Arg.Any<FilePickerOpenOptions>()).Returns(
            Task.FromResult<IReadOnlyList<string>?>([@"C:\hexoracle-cdfiles\a.txt", @"C:\hexoracle-cdfiles\b.txt"]));
        var ran = Run(Form(Dialog,
            "    dlg.Flags = &H200\r\n"
          + "    dlg.ShowOpen\r\n"
          + "    Debug.Print dlg.FileName\r\n"), windows);
        ran.Failures.Should().BeEmpty();
        ran.Printed.Should().Equal(@"C:\hexoracle-cdfiles\ a.txt b.txt");
    }

    [AvaloniaFact]
    public void Unknown_font_keeps_previous_name_and_real_face_keeps_spelling()
    {
        var dialog = new VBCommonDialog(null!);
        dialog.TrySetProperty("FontName", new Vb6Value("arial")).Should().BeTrue();
        dialog.TryGetProperty("FontName", out var before).Should().BeTrue();
        before.Should().Be(new Vb6Value("arial"));
        dialog.TrySetProperty("FontName", new Vb6Value("HexIDE nonexistent font 1084")).Should().BeTrue();
        dialog.TryGetProperty("FontName", out var after).Should().BeTrue();
        after.Should().Be(before);
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private const string Dialog =
        "   Begin MSComDlg.CommonDialog dlg \r\n"
      + "      Left            =   120\r\n"
      + "      Top             =   120\r\n"
      + "      _ExtentX        =   847\r\n"
      + "      _ExtentY        =   847\r\n"
      + "      _Version        =   393216\r\n"
      + "   End\r\n";

    private static string Form(string controls, string load) =>
        ("VERSION 5.00\r\n"
       + "Object = \"{F9043C88-F6F2-101A-A3C9-08002B2F49FB}#1.2#0\"; \"COMDLG32.OCX\"\r\n"
       + "Begin VB.Form Form1 \r\n"
       + "   Caption         =   \"Form1\"\r\n"
       + "   ClientHeight    =   3300\r\n"
       + "   ClientWidth     =   4000\r\n"
       + controls
       + "End\r\n"
       + "Attribute VB_Name = \"Form1\"\r\n"
       + "Private Sub Form_Load()\r\n"
       + "    On Error Resume Next\r\n"
       + load
       + "    End\r\n"
       + "End Sub\r\n").ReplaceLineEndings("\r\n");

    private static ProjectDefinition Project(string form)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddForm(new FormDeserializer().Deserialize(project, form, NullSink.Instance)!);
        return project;
    }

    private sealed class ListSink : IDeserializeErrorSink
    {
        public readonly List<string> Errors = [];
        public void LogError(string error) => Errors.Add(error);
    }

    [AvaloniaFact]
    public void Opening_a_form_that_holds_one_warns_of_nothing()
    {
        var sink = new ListSink();
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), Form(Dialog, ""), sink);

        sink.Errors.Should().BeEmpty();
    }

    private sealed record Ran(List<string> Printed, List<string> Failures);

    // Runs the form as the IDE runs it, over a window manager whose dialogs answer as given.
    private static Ran Run(string form, IWindowManager windows)
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns("{0}");
        localization.GetString("Str.ProjectRunner.StartupFormFailedToLoad").Returns("{1}");
        var runner = new ProjectRunnerService(
            Substitute.For<IEventBus>(), windows, Substitute.For<IProjectManager>(),
            localization, new DebugController(), Substitute.For<IBreakpointService>(), new WatchService(), new RunScope());
        var ran = new Ran([], []);
        runner.StartFailed += ran.Failures.Add;
        runner.RunFailed += ran.Failures.Add;
        Action<string> print = ran.Printed.Add;
        VBDebugConsole.Output += print;
        try
        {
            runner.RunProject(Project(form));
            for (var i = 0; i < 40; i++)
                Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            VBDebugConsole.Output -= print;
        }
        return ran;
    }

    private static IWindowManager Opening(params string?[] answers) => Answering(_ => { }, answers);

    // A window manager whose pickers answer in turn, telling `asked` what each was asked with.
    private static IWindowManager Answering(Action<object> asked, params string?[] answers)
    {
        var windows = Substitute.For<IWindowManager>();
        var queue = new Queue<string?>(answers);
        windows.OpenFilePickerAsync(Arg.Any<FilePickerOpenOptions>()).Returns(call =>
        {
            asked(call.Arg<FilePickerOpenOptions>());
            return Task.FromResult<IReadOnlyList<string>?>(queue.Dequeue() is { } path ? [path] : null);
        });
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(call =>
        {
            asked(call.Arg<FilePickerSaveOptions>());
            return Task.FromResult(queue.Dequeue());
        });
        return windows;
    }

    [AvaloniaFact]
    public void Its_properties_start_as_vb6_exe_s_with_vb6_exe_s_types()
    {
        var ran = Run(Form(Dialog,
            "    Debug.Print TypeName(dlg) & \" \" & TypeName(dlg.FileName) & \" \" & TypeName(dlg.Flags) & \" \" & TypeName(dlg.FilterIndex) _\r\n"
          + "        & \" \" & TypeName(dlg.CancelError) & \" \" & TypeName(dlg.FontSize) & \" \" & TypeName(dlg.Color) & \" \" & TypeName(dlg.FontName)\r\n"
          + "    Debug.Print \"[\" & dlg.FileName & \"][\" & dlg.FileTitle & \"][\" & dlg.Filter & \"] \" & dlg.FilterIndex & \" \" & dlg.Flags _\r\n"
          + "        & \" \" & dlg.CancelError & \" [\" & dlg.DialogTitle & \"][\" & dlg.DefaultExt & \"][\" & dlg.InitDir & \"][\" & dlg.FontName _\r\n"
          + "        & \"] \" & dlg.FontSize & \" \" & dlg.FontBold & \" \" & dlg.Color\r\n"), Opening());

        ran.Failures.Should().BeEmpty();
        ran.Printed.Should().Equal(
            "CommonDialog String Long Integer Boolean Single Long String",
            "[][][] 0 0 False [][][][] 8 False 0");
    }

    [AvaloniaFact]
    public void Cancel_raises_32755_with_CancelError_and_nothing_without_and_leaves_FileName_as_it_was()
    {
        var ran = Run(Form(Dialog,
            "    dlg.CancelError = True\r\n"
          + "    dlg.FileName = \"keep.txt\"\r\n"
          + "    dlg.ShowOpen\r\n"
          + "    Debug.Print Err.Number & \" [\" & Err.Description & \"] [\" & dlg.FileName & \"][\" & dlg.FileTitle & \"]\"\r\n"
          + "    Err.Clear\r\n"
          + "    dlg.CancelError = False\r\n"
          + "    dlg.FileName = \"keep2.txt\"\r\n"
          + "    dlg.ShowOpen\r\n"
          + "    Debug.Print Err.Number & \" [\" & dlg.FileName & \"]\"\r\n"), Opening(null, null));

        ran.Failures.Should().BeEmpty();
        ran.Printed.Should().Equal("32755 [Cancel was selected.] [keep.txt][]", "0 [keep2.txt]");
    }

    [AvaloniaFact]
    public void OK_sets_the_full_path_and_the_file_s_name_and_Save_As_adds_DefaultExt_to_a_name_without_one()
    {
        FilePickerSaveOptions? asked = null;
        var windows = Answering(o => asked = o as FilePickerSaveOptions ?? asked, @"C:\docs\sample.txt", @"C:\docs\abc", @"C:\docs\notes.log");
        var ran = Run(Form(Dialog,
            "    dlg.Filter = \"Text Documents (*.txt)|*.txt\"\r\n"
          + "    dlg.FilterIndex = 1\r\n"
          + "    dlg.Flags = cdlOFNFileMustExist Or cdlOFNHideReadOnly\r\n"
          + "    dlg.ShowOpen\r\n"
          + "    Debug.Print dlg.FileName & \" \" & dlg.FileTitle & \" \" & dlg.FilterIndex & \" \" & dlg.Flags\r\n"
          + "    dlg.Flags = cdlOFNOverwritePrompt\r\n"
          + "    dlg.DefaultExt = \"txt\"\r\n"
          + "    dlg.DialogTitle = \"Save As\"\r\n"
          + "    dlg.ShowSave\r\n"
          + "    Debug.Print dlg.FileName & \" \" & dlg.FileTitle & \" \" & dlg.Flags\r\n"
          + "    dlg.ShowSave\r\n"
          + "    Debug.Print dlg.FileName & \" \" & dlg.FileTitle\r\n"), windows);

        ran.Failures.Should().BeEmpty();
        // The Open result adds PathMustExist and ExtensionDifferent (DefaultExt was empty).
        ran.Printed.Should().Equal(
            @"C:\docs\sample.txt sample.txt 1 7172",
            @"C:\docs\abc.txt abc.txt 2",
            @"C:\docs\notes.log notes.log");
        asked!.Title.Should().Be("Save As");
        asked.ShowOverwritePrompt.Should().BeTrue();
        asked.DefaultExtension.Should().Be("txt");
        asked.SuggestedFileName.Should().Be("abc.txt", "the last answer's name is offered again");
    }

    [AvaloniaFact]
    public void The_filter_offered_is_the_program_s_and_FilterIndex_is_refused_when_the_user_could_have_changed_it()
    {
        FilePickerOpenOptions? asked = null;
        var windows = Answering(o => asked = o as FilePickerOpenOptions ?? asked, @"C:\docs\sample.txt");
        var ran = Run(Form(Dialog,
            "    dlg.Filter = \"Text Documents (*.txt)|*.txt|All Files (*.*)|*.*\"\r\n"
          + "    dlg.FilterIndex = 2\r\n"
          + "    dlg.ShowOpen\r\n"
          + "    Debug.Print dlg.FileName\r\n"
          + "    Debug.Print dlg.FilterIndex\r\n"), windows);

        ran.Printed.Should().Equal(@"C:\docs\sample.txt");
        ran.Failures.Should().ContainSingle().Which.Should().Contain("FilterIndex");
        asked!.FileTypeFilter.Should().HaveCount(2);
        asked.FileTypeFilter![0].Name.Should().Be("Text Documents (*.txt)");
        asked.FileTypeFilter[0].Patterns.Should().Equal("*.txt");
        asked.SuggestedFileType!.Name.Should().Be("All Files (*.*)");
    }

    [AvaloniaFact]
    public void ShowFont_takes_the_font_chosen_and_Cancel_leaves_it()
    {
        var windows = Opening();
        windows.ShowFontDialog(Arg.Any<FontDialogResult?>()).Returns(
            Task.FromResult<FontDialogResult?>(new FontDialogResult(new FontFamily("Courier New"), FontStyle.Italic, FontWeight.Bold, 10)),
            Task.FromResult<FontDialogResult?>(null));
        var ran = Run(Form(Dialog,
            "    dlg.Flags = cdlCFScreenFonts\r\n"
          + "    dlg.FontName = \"Arial\"\r\n"
          + "    dlg.FontSize = 12\r\n"
          + "    dlg.ShowFont\r\n"
          + "    Debug.Print dlg.FontName & \" \" & dlg.FontSize & \" \" & dlg.FontBold & \" \" & dlg.FontItalic & \" \" & dlg.Flags\r\n"
          + "    dlg.CancelError = True\r\n"
          + "    dlg.ShowFont\r\n"
          + "    Debug.Print Err.Number & \" \" & dlg.FontName\r\n"), windows);

        ran.Failures.Should().BeEmpty();
        ran.Printed.Should().Equal("Courier New 10 True True 1", "32755 Courier New");
    }

    [AvaloniaTheory]
    [InlineData(0, 0)]
    [InlineData(1, 255)]
    public void ShowColor_runs_through_the_IDE_host(int flags, int expected)
    {
        var windows = Opening();
        windows.ShowColorDialog(expected).Returns(Task.FromResult<int?>(expected));
        var ran = Run(Form(Dialog,
            $"    dlg.Flags = {flags}\r\n"
          + "    dlg.Color = vbRed\r\n"
          + "    dlg.ShowColor\r\n"
          + "    Debug.Print CStr(VarType(dlg.Color))\r\n"), windows);

        ran.Failures.Should().BeEmpty();
        ran.Printed.Should().Equal("19");
        windows.Received(1).ShowColorDialog(expected);
    }

    [AvaloniaTheory]
    [InlineData("dlg.ShowPrinter")]
    [InlineData("dlg.Action = 5")]
    public void Printer_runs_through_the_IDE_host(string statement)
    {
        var windows = Opening();
        windows.ShowPrintDialog().Returns(Task.FromResult(true), Task.FromResult(false));
        var ran = Run(Form(Dialog,
            "    dlg.Copies = 3\r\n"
          + "    dlg.PrinterDefault = False\r\n"
          + $"    {statement}\r\n"
          + "    Debug.Print dlg.Copies & \" \" & dlg.Orientation & \" \" & dlg.hDC\r\n"
          + "    dlg.CancelError = True\r\n"
          + $"    {statement}\r\n"
          + "    Debug.Print Err.Number & \" \" & dlg.Copies & \" \" & dlg.Orientation & \" \" & dlg.hDC\r\n"), windows);
        ran.Failures.Should().BeEmpty();
        ran.Printed.Should().Equal("1 1 0", "32755 1 1 0");
        windows.Received(2).ShowPrintDialog();
    }

    [AvaloniaFact]
    public void What_the_form_s_file_sets_is_read_and_a_member_it_does_not_model_is_refused()
    {
        var withProperties = Dialog.Replace("      _ExtentX",
            "      CancelError     =   -1  'True\r\n      DialogTitle     =   \"Pick one\"\r\n      _ExtentX");
        var ran = Run(Form(withProperties,
            "    Debug.Print dlg.CancelError & \" \" & dlg.DialogTitle\r\n"
          + "    Debug.Print dlg.hDC\r\n"), Opening());

        ran.Printed.Should().Equal("True Pick one");
        ran.Failures.Should().ContainSingle().Which.Should().Contain("hDC");
    }

    [AvaloniaFact]
    public void Its_constants_are_the_type_library_s_and_resolve_only_where_the_project_references_it()
    {
        var referenced = Run(Form("", "    Debug.Print cdlCancel & \" \" & TypeName(cdlOFNFileMustExist) & \" \" & cdlCFBoth\r\n"), Opening());
        var unreferenced = Run(Form("", "    Debug.Print IsEmpty(cdlCancel)\r\n")
            .Replace("Object = \"{F9043C88-F6F2-101A-A3C9-08002B2F49FB}#1.2#0\"; \"COMDLG32.OCX\"\r\n", ""), Opening());

        referenced.Printed.Should().Equal("32755 Long 3");
        unreferenced.Printed.Should().Equal("True");
    }

    [AvaloniaFact]
    public void Beside_a_control_HexIDE_cannot_create_only_that_control_is_named()
    {
        var data =
            "   Begin VB.Data Data1 \r\n"
          + "      Caption         =   \"Data1\"\r\n"
          + "   End\r\n";
        var ran = Run(Form(Dialog + data, ""), Opening());

        ran.Failures.Should().ContainSingle().Which.Should().Be("HexIDE cannot create Data1 (VB.Data).");
    }
}
