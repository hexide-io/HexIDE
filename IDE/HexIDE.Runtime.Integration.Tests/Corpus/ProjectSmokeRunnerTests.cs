using HexIDE.Runtime.Interpreter;
using HexIDE.Tests.Corpus;

namespace HexIDE.Runtime.Integration.Tests.Corpus;

public class ProjectSmokeRunnerTests
{
    // A call to a procedure the program Declares from a DLL is HexIDE's refusal to call native code, not a crash; the
    // same error for a name nothing declares is still a crash (#4774). NeonAurora's Form_Load reaches winmm's midiOutOpen.
    [Theory]
    [InlineData("midiOutOpen", "refused")]
    [InlineData("MIDIOUTOPEN", "refused")]
    [InlineData("midiOutClose", "refused")]
    [InlineData("NotDeclared", "crashed")]
    public void A_call_to_a_Declared_procedure_is_a_refusal(string called, string expected)
    {
        var declared = SmokeStage.DeclaredProcedures([
            "Option Explicit\r\nPrivate Declare Function midiOutOpen Lib \"winmm.dll\" (lphMidiOut As Long) As Long\r\n",
            "Public Declare PtrSafe Sub midiOutClose Lib \"winmm.dll\" (ByVal h As LongPtr)\r\n' Declare Sub InAComment Lib \"x\" ()\r\n"
        ]);
        declared.Should().BeEquivalentTo(["midiOutOpen", "midiOutClose"]);
        var stage = SmokeStage.FromException(new VBSubOrFunctionNotDefinedException(called), declared);
        stage.Status.Should().Be(expected, stage.Detail);
        SmokeStage.FromException(new VBSubOrFunctionNotDefinedException(called)).Status.Should().Be("crashed",
            "with no declared names, as before");
    }

    // Off Windows the same call raises 53 naming its Lib string, as VBA on a Mac does (#4820), and NeonAurora's
    // Form_Load doesn't trap its gdi32 call. Only a library the project declares counts.
    [Theory]
    [InlineData(53, "File not found: winmm.dll", "refused")]
    [InlineData(53, "File not found: gdi32", "refused")]
    [InlineData(53, "File not found: other.dll", "crashed")]
    [InlineData(53, "File not found: kernel32", "crashed")]
    [InlineData(53, "File not found", "crashed")]
    [InlineData(5, "File not found: gdi32", "crashed")]
    public void A_missing_Declared_library_is_a_refusal(int number, string description, string expected)
    {
        var libraries = SmokeStage.DeclaredLibraries([
            "Private Declare Function midiOutOpen Lib \"winmm.dll\" (lphMidiOut As Long) As Long\r\n",
            "Public Declare Function CreateCompatibleDC LIB \"gdi32\" (ByVal hdc As Long) As Long\r\n"
            + "Private Declare Function Wrapped Lib _\r\n    \"kernel32\" () As Long\r\n' Declare Sub InAComment Lib \"x\" ()\r\n"
        ]);
        libraries.Should().BeEquivalentTo(["winmm.dll", "gdi32"], "a Lib on a continuation line is missed");
        var error = new VBRunTimeException(new VBStandardError(number, description));
        SmokeStage.FromException(error, libraries: libraries).Status.Should().Be(expected, description);
        SmokeStage.FromException(error).Status.Should().Be("crashed", "with no declared libraries");
    }

    [Fact]
    public void Slow_host_does_not_spend_the_workers_preparation_budget()
    {
        var budget = new ProjectSmokeRunner.PreparationBudget();
        budget.Timeout(false, TimeSpan.FromSeconds(90)).Should().BeNull();
        budget.Timeout(true, TimeSpan.FromSeconds(98)).Should().BeNull();
        budget.Timeout(true, TimeSpan.FromSeconds(157)).Should().BeNull();
        budget.Timeout(true, TimeSpan.FromSeconds(158)).Should()
            .Be("Startup worker did not become ready within 60 seconds of entering the worker.");
    }

    [Fact]
    public void Host_that_never_enters_the_worker_has_a_separate_bound()
    {
        var budget = new ProjectSmokeRunner.PreparationBudget();
        budget.Timeout(false, TimeSpan.FromSeconds(299)).Should().BeNull();
        budget.Timeout(false, TimeSpan.FromSeconds(300)).Should()
            .Be("Test host did not enter the startup worker within 300 seconds.");
    }

    [Fact]
    public void Preparation_hang_is_bounded_even_while_the_started_marker_stays_present()
    {
        var budget = new ProjectSmokeRunner.PreparationBudget();
        budget.Timeout(true, TimeSpan.FromSeconds(5)).Should().BeNull();
        budget.Timeout(true, TimeSpan.FromSeconds(64)).Should().BeNull();
        budget.Timeout(true, TimeSpan.FromSeconds(65)).Should()
            .Be("Startup worker did not become ready within 60 seconds of entering the worker.");
    }

    // Err.Raise 5: docs/vb6-fidelity-oracle/when-err-is-cleared-and-what-a-running-handler-traps.md (#877).
    [Theory]
    [InlineData(null, "refused", false)]
    [InlineData("", "ran to completion", false)]
    [InlineData("Do\r\nLoop", "ran to the time limit", false)]
    [InlineData("AppActivate \"missing\"", "refused", false)]
    [InlineData("Err.Raise 5", "crashed", false)]
    [InlineData("MsgBox \"hello\"\r\nDim answer As String\r\nanswer = InputBox(\"prompt\", , \"scripted\")", "ran to completion", false)]
    [InlineData("", "ran to completion", true)]
    [InlineData("AppActivate \"missing\"", "refused", true)]
    [InlineData("Err.Raise 5", "crashed", true)]
    public async Task Isolated_startup_reports_completion_timeout_refusal_fault_and_answers_dialogs(string? code, string expected, bool form)
    {
        var directory = Directory.CreateTempSubdirectory("hexide-startup-test-").FullName;
        try
        {
            var project = Path.Join(directory, "Test.vbp");
            var marker = Path.Join(directory, "marker.txt");
            File.WriteAllText(project, "Type=Exe\r\nModule=Module1; Module1.bas\r\nStartup=\"Sub Main\"\r\nName=\"Test\"\r\n");
            File.WriteAllText(Path.Join(directory, "Module1.bas"),
                "Attribute VB_Name = \"Module1\"\r\nSub Main()\r\n" + code
                + $"\r\nOpen \"{marker}\" For Output As #1\r\nPrint #1, \"executed\"\r\nClose #1\r\nEnd Sub\r\n");
            if (form)
            {
                File.WriteAllText(project, "Type=Exe\r\nForm=Form1.frm\r\nStartup=\"Form1\"\r\nName=\"Test\"\r\n");
                var body = File.ReadAllText(Path.Join(directory, "Module1.bas"))
                    .Replace("Sub Main()", "Private Sub Form_Load()")
                    .Replace("End Sub", "Unload Me\r\nEnd Sub")
                    .Replace("Attribute VB_Name = \"Module1\"", "Attribute VB_Name = \"Form1\"");
                File.WriteAllText(Path.Join(directory, "Form1.frm"),
                    "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption = \"Test\"\r\nEnd\r\n" + body);
            }
            if (code is null)
                File.WriteAllText(project, "Type=Exe\r\nStartup=\"(None)\"\r\nName=\"Test\"\r\n");
            var result = await ProjectSmokeRunner.Run(project, expected == "ran to the time limit" ? ProjectSmokeRunner.StartupSeconds : 30);
            result.Status.Should().Be(expected, result.Detail);
            if (expected == "ran to completion") File.ReadAllText(marker).Should().Contain("executed");
            if (expected == "refused") result.Detail.Should().Contain(code is null ? "no startup path" : "AppActivate");
            if (expected == "crashed") result.Detail.Should().Contain("Run-time error '5'");
        }
        finally { Directory.Delete(directory, true); }
    }

    // #4863: preparation after .ready must not spend Sub Main's window. Err.Raise 5 is measured in
    // docs/vb6-fidelity-oracle/when-err-is-cleared-and-what-a-running-handler-traps.md (#877).
    [Theory]
    [InlineData("Err.Raise 5", 4000, "crashed", "Run-time error '5'")]
    [InlineData("Do\r\nLoop", 0, "ran to the time limit", "Sub Main started.")]
    public async Task Sub_Main_is_watched_from_when_it_starts(string code, int preparationDelayMilliseconds,
        string expected, string detail)
    {
        var directory = Directory.CreateTempSubdirectory("hexide-startup-main-").FullName;
        try
        {
            var project = Path.Join(directory, "Test.vbp");
            File.WriteAllText(project, "Type=Exe\r\nModule=Module1; Module1.bas\r\nStartup=\"Sub Main\"\r\nName=\"Test\"\r\n");
            File.WriteAllText(Path.Join(directory, "Module1.bas"),
                "Attribute VB_Name = \"Module1\"\r\nSub Main()\r\n" + code + "\r\nEnd Sub\r\n");
            var result = await ProjectSmokeRunner.Run(project, preparationDelayMilliseconds: preparationDelayMilliseconds);
            result.Status.Should().Be(expected, result.Detail);
            result.Detail.Should().Contain(detail);
        }
        finally { Directory.Delete(directory, true); }
    }

    // A Timer on the startup form, enabled in Form_Load as the corpus demos enable theirs: the Timer is saved with no
    // Interval, or disabled (#4774). The startup window opens when the form shows, and an armed Timer is watched until one
    // of its events has finished, so an error in its handler fails the run whenever its first tick comes.
    // A run that reaches a limit names what it saw, which each such row checks. Without the form's window marker the
    // clean tick would still pass, 30 seconds slower, so its detail is what keeps the marker honest.
    [Theory]
    [InlineData(true, "Timer1.Interval = 5000", "Err.Raise 5", "crashed", null)]
    [InlineData(false, "Timer1.Interval = 5000\r\nTimer1.Enabled = True", "Err.Raise 5", "crashed", null)]
    [InlineData(true, "Timer1.Interval = 5000", "Caption = \"ticked\"", "ran to the time limit",
        "the startup form showed; a Timer armed; a Timer event finished.")]
    [InlineData(true, "Dim t As Single\r\nt = Timer\r\nDo While Timer < t + 4\r\nLoop\r\nTimer1.Interval = 100", "Err.Raise 5",
        "crashed", null)]
    [InlineData(true, "Timer1.Interval = 60000", "Err.Raise 5", "timer did not fire",
        "the startup form showed; a Timer armed; none of its events finished within 5 seconds of the form showing.")]
    [InlineData(true, "Do\r\nLoop", "Err.Raise 5", "ran to the time limit", "the startup form did not show within 5 seconds.")]
    [InlineData(true, "Timer1.Interval = 100", null, "ran to the time limit", "the startup form showed.")]
    public async Task A_startup_Timer_is_watched_until_one_of_its_events_finishes(bool savedEnabled, string load, string? tick,
        string expected, string? watched)
    {
        var directory = Directory.CreateTempSubdirectory("hexide-startup-timer-").FullName;
        try
        {
            var project = Path.Join(directory, "Test.vbp");
            File.WriteAllText(project, "Type=Exe\r\nForm=Form1.frm\r\nStartup=\"Form1\"\r\nName=\"Test\"\r\n");
            File.WriteAllText(Path.Join(directory, "Form1.frm"),
                "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption = \"Test\"\r\n   Begin VB.Timer Timer1 \r\n"
                + (savedEnabled ? "" : "      Enabled = 0   'False\r\n")
                + "      Left = 0\r\n      Top = 0\r\n   End\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n"
                + "Private Sub Form_Load()\r\n" + load + "\r\nEnd Sub\r\n"
                + (tick is null ? "" : "Private Sub Timer1_Timer()\r\n" + tick + "\r\nEnd Sub\r\n"));
            // The two bounds are shortened only where a case runs to them, to keep it quick.
            var result = await ProjectSmokeRunner.Run(project,
                showSeconds: load == "Do\r\nLoop" ? 5 : ProjectSmokeRunner.ShowSeconds,
                timerSeconds: expected == "timer did not fire" ? 5 : ProjectSmokeRunner.TimerSeconds);
            result.Status.Should().Be(expected, result.Detail);
            if (expected == "crashed") result.Detail.Should().Contain("Run-time error '5'");
            else result.Detail.Should().Be(watched);
        }
        finally { Directory.Delete(directory, true); }
    }
}
