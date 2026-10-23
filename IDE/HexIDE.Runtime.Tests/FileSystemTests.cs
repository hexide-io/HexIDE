using System.IO;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class FileSystemTests : BaseVBTestFixture, IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hexide-fs-").FullName;

    public FileSystemTests()
    {
        foreach (var (name, contents) in new[] { ("a.txt", "abc"), ("b.txt", "ab"), ("c.htm", "c"), ("d.html", "d"), ("x.tmp", "x"), ("y.tmp", "y"), ("q1.dat", "q"), ("q2.dat", "q"), ("h.txt", "h"), ("r.txt", "r"), ("s.txt", "s") })
            File.WriteAllText(Path.Combine(dir, name), contents);
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(Path.Combine(dir, "h.txt"), FileAttributes.Hidden);
            File.SetAttributes(Path.Combine(dir, "r.txt"), FileAttributes.ReadOnly);
            File.SetAttributes(Path.Combine(dir, "s.txt"), FileAttributes.System);
        }
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "sub", "in.txt"), "i");
        Directory.CreateDirectory(Path.Combine(dir, "empty"));
    }

    public void Dispose()
    {
        foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(dir, true);
    }

    private static Vb6Value Long(int value) => new((long)value);

    private Task Execute(string code) => Run($"ChDir \"{dir}\"\n" + code);

    [Theory]
    [InlineData("MkDir \"new\"", "Dir(\"new\", 16)", "new")]
    [InlineData("MkDir \"slash\\\"", "Dir(\"slash\", 16)", "slash")]
    [InlineData("RmDir \"empty\"", "Dir(\"empty\", 16)", "")]
    [InlineData("FileCopy \"a.txt\", \"e.txt\"", "Dir(\"e.txt\")", "e.txt")]
    [InlineData("FileCopy \"a.txt\", \"b.txt\"", "CStr(FileLen(\"b.txt\"))", "3")]
    [InlineData("Name \"a.txt\" As \"f.txt\"", "Dir(\"f.txt\")", "f.txt")]
    [InlineData("Name \"a.txt\" As \"a.txt\"", "Dir(\"a.txt\")", "a.txt")]
    [InlineData("Name \"a.txt\" As \"A.TXT\"", "Dir(\"a.txt\")", "A.TXT")]
    [InlineData("Name \"empty\" As \"renamed\"", "Dir(\"renamed\",16)", "renamed")]
    [InlineData("Name \"a.txt\" As \"sub\\moved.txt\"", "Dir(\"sub\\moved.txt\")", "moved.txt")]
    [InlineData("Kill \"a.txt\"", "Dir(\"a.txt\")", "")]
    [InlineData("Kill \"*.tmp\"", "Dir(\"*.tmp\")", "")]
    [InlineData("Kill \"q?.dat\"", "Dir(\"q?.dat\")", "")]
    public async Task Mutation_rows(string statement, string expression, string expected)
    {
        await Execute(statement + "\nDebug.Print " + expression);
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("MkDir \"empty\"",75)]
    [InlineData("MkDir \"a.txt\"",75)]
    [InlineData("MkDir \"missing\\new\"",76)]
    [InlineData("MkDir \"\"",76)]
    [InlineData("RmDir \"sub\"",75)]
    [InlineData("RmDir \"missing\"",76)]
    [InlineData("RmDir \"a.txt\"",76)]
    [InlineData("RmDir \"e*\"",76)]
    [InlineData("RmDir \"\"",76)]
    [InlineData("Debug.Print FileLen(\"missing\")",53)]
    [InlineData("Debug.Print FileLen(\"\")",53)]
    [InlineData("Debug.Print FileLen(\"*.txt\")",52)]
    [InlineData("Debug.Print FileDateTime(\"missing\")",53)]
    [InlineData("Debug.Print FileDateTime(\"*.txt\")",52)]
    [InlineData("Debug.Print GetAttr(\"missing\")",53)]
    [InlineData("Debug.Print GetAttr(\"\")",53)]
    [InlineData("Debug.Print GetAttr(\"*.txt\")",52)]
    [InlineData("SetAttr \"a.txt\",16",5)]
    [InlineData("SetAttr \"a.txt\",8",5)]
    [InlineData("SetAttr \"missing\",0",53)]
    [InlineData("SetAttr \"*.txt\",0",52)]
    [InlineData("Open \"a.txt\" For Append As #1\nSetAttr \"a.txt\",0",55)]
    [InlineData("FileCopy \"missing\",\"e.txt\"",53)]
    [InlineData("FileCopy \"a.txt\",\"missing\\e.txt\"",76)]
    [InlineData("FileCopy \"a.txt\",\"empty\"",75)]
    [InlineData("FileCopy \"empty\",\"e.txt\"",75)]
    [InlineData("FileCopy \"a.txt\",\"a.txt\"",70)]
    [InlineData("FileCopy \"*.txt\",\"e.txt\"",52)]
    [InlineData("Open \"a.txt\" For Output As #1\nFileCopy \"a.txt\",\"e.txt\"",55)]
    [InlineData("Open \"b.txt\" For Output As #1\nFileCopy \"a.txt\",\"b.txt\"",55)]
    [InlineData("Name \"a.txt\" As \"b.txt\"",58)]
    [InlineData("Name \"missing\" As \"b.txt\"",53)]
    [InlineData("Name \"a.txt\" As \"missing\\b.txt\"",53)]
    [InlineData("Name \"*.txt\" As \"e.txt\"",52)]
    [InlineData("Open \"a.txt\" For Input As #1\nName \"a.txt\" As \"e.txt\"",55)]
    [InlineData("Kill \"missing\"",53)]
    [InlineData("Kill \"\"",53)]
    [InlineData("Kill \"*.zzz\"",53)]
    [InlineData("Kill \"empty\"",53)]
    [InlineData("Open \"a.txt\" For Input As #1\nKill \"a.txt\"",55)]
    [InlineData("Debug.Print Dir()",5)]
    [InlineData("Dim s As String\ns=Dir(\"missing\")\ns=Dir()",5)]
    [InlineData("Debug.Print Dir(\"*\",1024)",5)]
    [InlineData("Debug.Print Dir(\"*\",-1)",5)]
    [InlineData("Debug.Print Dir(Null)",13)]
    [InlineData("Debug.Print CurDir(\"1\")",5)]
    [InlineData("Debug.Print CurDir(\"Q\")",68)]
    public async Task Error_rows(string statement, int number)
    {
        await Execute("On Error Resume Next\n" + statement + "\nDebug.Print Err.Number\nClose");
        AssertDebugLog([Long(number)]);
    }

    [Theory]
    [InlineData("Dir(\"A.TXT\")", "a.txt")]
    [InlineData("Dir(\"empty\",16)", "empty")]
    [InlineData("Dir(\"empty\")", "")]
    [InlineData("Dir(\"missing\\*\")", "")]
    [InlineData("Dir(\"*.zzz\")", "")]
    [InlineData("Dir(\"\")", "a.txt")]
    [InlineData("Dir(\".\\\")", "a.txt")]
    [InlineData("Dir(\".\\\",16)", ".")]
    [InlineData("Dir(\"*\",24)", ".")]
    [InlineData("Dir(\"*\",8)", "")]
    [InlineData("Dir(\"*.htm\")", "c.htm")]
    [InlineData("Dir$(\"A.TXT\")", "a.txt")]
    [InlineData("Dir(\"*.txt\",0)", "a.txt")]
    [InlineData("Dir(\"*.txt\",1)", "a.txt")]
    public async Task Dir_rows(string expression, string expected)
    {
        await Execute("Debug.Print " + expression);
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task FileLen_reads_disk_before_and_after_Close()
    {
        await Execute("Open \"new\" For Output As #1\nPrint #1, \"abc\"\nDebug.Print FileLen(\"new\")\nClose\nDebug.Print FileLen(\"new\")");
        AssertDebugLog([Long(0),Long(5)]);
    }

    [Fact]
    public async Task Types_lengths_dates_and_current_directory()
    {
        await Execute("Debug.Print FileLen(\"a.txt\")\nDebug.Print FileLen(\"empty\")\nDebug.Print GetAttr(\"sub\\\")\nDebug.Print TypeName(FileDateTime(\"a.txt\"))\nDebug.Print TypeName(FileDateTime(\"sub\"))\nDebug.Print CurDir\nDebug.Print CurDir$(\"\")");
        AssertDebugLog([Long(3),Long(0),Long(16),new Vb6Value("Date"),new Vb6Value("Date"),new Vb6Value(dir),new Vb6Value(dir)]);
    }

    [Fact]
    public async Task Listing_is_sorted_snapshot_and_new_pattern_restarts_it()
    {
        await Execute("Debug.Print Dir(\"*.tmp\")\nKill \"x.tmp\"\nDebug.Print Dir()\nDebug.Print Dir()\nDebug.Print Dir(\"?.htm\")\nDebug.Print Dir(\"?.dat\")");
        AssertDebugLog([new Vb6Value("x.tmp"),new Vb6Value("y.tmp"),new Vb6Value(""),new Vb6Value("c.htm"),new Vb6Value("")]);
    }

    [Fact]
    public async Task Input_allows_SetAttr_and_FileCopy()
    {
        await Execute("Open \"a.txt\" For Input As #1\nSetAttr \"a.txt\",0\nFileCopy \"a.txt\",\"e.txt\"\nClose\nDebug.Print FileLen(\"e.txt\")");
        AssertDebugLog([Long(3)]);
    }
    [WindowsOnlyFact]
    public async Task Windows_attribute_rows()
    {
        await Execute("SetAttr \"h.txt\",2\nSetAttr \"r.txt\",1\nSetAttr \"s.txt\",4\nDebug.Print GetAttr(\"h.txt\")\nDebug.Print GetAttr(\"r.txt\")\nDebug.Print GetAttr(\"s.txt\")\nDebug.Print FileLen(\"h.txt\")");
        AssertDebugLog([Long(2),Long(1),Long(4),Long(1)]);
    }

    [Theory]
    [InlineData("Debug.Print FileLen(\"a.txt\")", 3)]
    [InlineData("Debug.Print FileLen(\"empty\")", 0)]
    [InlineData("Debug.Print GetAttr(\"sub\")", 16)]
    [InlineData("Debug.Print GetAttr(\"sub\\\")", 16)]
    [InlineData("SetAttr \"a.txt\",0\nDebug.Print GetAttr(\"a.txt\")", 0)]
    [InlineData("SetAttr \"sub\",0\nDebug.Print GetAttr(\"sub\")", 16)]
    public async Task Numeric_rows(string code, int expected)
    {
        await Execute(code);
        AssertDebugLog([Long(expected)]);
    }

    [Theory]
    [InlineData("a.txt")]
    [InlineData("sub")]
    public async Task Date_rows(string path)
    {
        var date = File.GetLastWriteTime(Path.Combine(dir,path));
        date = new DateTime(date.Ticks - date.Ticks % TimeSpan.TicksPerSecond);
        await Execute($"Debug.Print FileDateTime(\"{path}\")");
        AssertDebugLog([new Vb6Value(date)]);
    }

    [Fact]
    public async Task Dir_question_mark_and_sorted_order()
    {
        await Execute("Dim s As String, result As String\ns=Dir(\"?.txt\")\nDo While s<>\"\"\nresult=result & s & \",\"\ns=Dir()\nLoop\nDebug.Print result");
        AssertDebugLog([new Vb6Value(OperatingSystem.IsWindows() ? "a.txt,b.txt,r.txt," : "a.txt,b.txt,h.txt,r.txt,s.txt,")]);
    }

    [Fact]
    public async Task Dir_directory_names_follow_dot_entries_in_sorted_order()
    {
        await Execute("Debug.Print Dir(\"*\",16)\nDebug.Print Dir()\nDebug.Print Dir()\nDebug.Print Dir(\"*.tmp\")\nDebug.Print Dir(\"*.htm\")\nDebug.Print Dir()");
        AssertDebugLog([new Vb6Value("."),new Vb6Value(".."),new Vb6Value("a.txt"),new Vb6Value("x.tmp"),new Vb6Value("c.htm"),new Vb6Value("")]);
    }

    [WindowsOnlyTheory]
    [InlineData("Debug.Print GetAttr(\"a.txt\")",32)]
    [InlineData("SetAttr \"a.txt\",32\nDebug.Print GetAttr(\"a.txt\")",32)]
    [InlineData("SetAttr \"a.txt\",39\nDebug.Print GetAttr(\"a.txt\")",39)]
    [InlineData("SetAttr \"a.txt\",39\nSetAttr \"a.txt\",0\nDebug.Print GetAttr(\"a.txt\")",0)]
    [InlineData("SetAttr \"sub\",2\nDebug.Print GetAttr(\"sub\")",18)]
    [InlineData("SetAttr \"sub\",2\nSetAttr \"sub\",0\nDebug.Print GetAttr(\"sub\")",16)]
    [InlineData("SetAttr \"h.txt\",2\nDebug.Print FileLen(\"h.txt\")",1)]
    [InlineData("SetAttr \"a.txt\",1\nFileCopy \"a.txt\",\"e.txt\"\nDebug.Print GetAttr(\"e.txt\")",32)]
    public async Task Windows_numeric_rows(string code, int expected)
    {
        await Execute(code);
        AssertDebugLog([Long(expected)]);
    }

    [WindowsOnlyTheory]
    [InlineData("SetAttr \"b.txt\",1\nFileCopy \"a.txt\",\"b.txt\"",75)]
    [InlineData("SetAttr \"b.txt\",2\nFileCopy \"a.txt\",\"b.txt\"",75)]
    [InlineData("SetAttr \"r.txt\",1\nKill \"r.txt\"",75)]
    [InlineData("SetAttr \"h.txt\",2\nKill \"h.txt\"",53)]
    [InlineData("SetAttr \"s.txt\",4\nKill \"s.txt\"",53)]
    public async Task Windows_error_rows(string code, int expected)
    {
        await Execute("On Error Resume Next\n" + code + "\nDebug.Print Err.Number");
        AssertDebugLog([Long(expected)]);
    }

    [WindowsOnlyTheory]
    [InlineData("SetAttr \"h.txt\",2\nDebug.Print Dir(\"h.txt\")", "")]
    [InlineData("SetAttr \"h.txt\",2\nDebug.Print Dir(\"h.txt\",2)", "h.txt")]
    [InlineData("SetAttr \"s.txt\",4\nDebug.Print Dir(\"s.txt\")", "")]
    [InlineData("SetAttr \"s.txt\",4\nDebug.Print Dir(\"s.txt\",4)", "s.txt")]
    [InlineData("SetAttr \"r.txt\",1\nDebug.Print Dir(\"r.txt\")", "r.txt")]
    [InlineData("SetAttr \"r.txt\",1\nName \"r.txt\" As \"new.txt\"\nDebug.Print Dir(\"new.txt\")", "new.txt")]
    [InlineData("FileCopy \"a.txt\",\"h.log\"\nFileCopy \"a.txt\",\"n.log\"\nSetAttr \"h.log\",2\nKill \"*.log\"\nDebug.Print Dir(\"*.log\",2)", "h.log")]
    public async Task Windows_name_rows(string code, string expected)
    {
        await Execute(code);
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [UnixOnlyTheory]
    [InlineData("Debug.Print GetAttr(\".hidden\")",2)]
    [InlineData("SetAttr \"a.txt\",38\nDebug.Print GetAttr(\"a.txt\")",0)]
    [InlineData("SetAttr \".hidden\",0\nDebug.Print GetAttr(\".hidden\")",2)]
    [InlineData("On Error Resume Next\nKill \".hidden\"\nDebug.Print Err.Number",53)]
    [InlineData("SetAttr \"a.txt\",1\nOn Error Resume Next\nKill \"a.txt\"\nDebug.Print Err.Number",75)]
    [InlineData("FileCopy \"a.txt\",\"e.txt\"\nDebug.Print GetAttr(\"e.txt\")",0)]
    [InlineData("SetAttr \"a.txt\",1\nFileCopy \"a.txt\",\"e.txt\"\nDebug.Print GetAttr(\"e.txt\")",0)]
    [InlineData("SetAttr \"b.txt\",1\nOn Error Resume Next\nFileCopy \"a.txt\",\"b.txt\"\nDebug.Print Err.Number",75)]
    [InlineData("SetAttr \"r.txt\",1\nName \"r.txt\" As \"renamed.txt\"\nDebug.Print FileLen(\"renamed.txt\")",1)]
    public async Task Unix_attribute_rows(string code, int expected)
    {
        File.WriteAllText(Path.Combine(dir,".hidden"),"h");
        await Execute(code);
        AssertDebugLog([Long(expected)]);
    }

    [UnixOnlyTheory]
    [InlineData("Dir(\".hidden\")", "")]
    [InlineData("Dir(\".hidden\",2)", ".hidden")]
    [InlineData("Dir(\".hidden\",4)", "")]
    public async Task Unix_Dir_hidden_rows(string expression, string expected)
    {
        File.WriteAllText(Path.Combine(dir,".hidden"),"h");
        await Execute("Debug.Print " + expression);
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task ChDir_uses_Open_resolution_and_is_local_to_the_interpreter()
    {
        var before = Environment.CurrentDirectory;
        await Execute("ChDir \"sub\"\nDebug.Print CurDir\nOpen \"in.txt\" For Input As #1\nDebug.Print FileLen(\"in.txt\")\nClose");
        AssertDebugLog([new Vb6Value(Path.Combine(dir,"sub")),Long(1)]);
        Environment.CurrentDirectory.Should().Be(before);
    }

    [WindowsOnlyTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CurDir_uses_only_the_first_drive_character(int form)
    {
        var drive = Path.GetPathRoot(dir)![..1];
        if (form == 1) drive = drive.ToLowerInvariant();
        if (form == 2) drive += ":\\Windows";
        await Execute($"Debug.Print CurDir(\"{drive}\")");
        AssertDebugLog([new Vb6Value(dir)]);
    }

    [Theory]
    [InlineData("CurDir")]
    [InlineData("CurDir(\"\")")]
    [InlineData("CurDir$")]
    public async Task Current_directory_rows(string expression)
    {
        await Execute("Debug.Print " + expression);
        AssertDebugLog([new Vb6Value(dir)]);
    }

    [UnixOnlyTheory]
    [InlineData("CurDir(\"C\")")]
    [InlineData("CurDir(\"c\")")]
    [InlineData("CurDir(\"C:\\Windows\")")]
    public async Task Unix_drive_queries_are_unavailable(string expression)
    {
        await Execute("On Error Resume Next\nDebug.Print " + expression + "\nDebug.Print Err.Number");
        AssertDebugLog([Long(68)]);
    }

    [Theory]
    [InlineData("CurDir(Drive:=\"C\")")]
    [InlineData("Dir(PathName:=\"*\")")]
    [InlineData("FileLen(PathName:=\"a.txt\")")]
    [InlineData("FileDateTime(PathName:=\"a.txt\")")]
    [InlineData("GetAttr(PathName:=\"a.txt\")")]
    public async Task Unmeasured_named_calls_are_explicitly_refused(string expression)
    {
        var act = () => Execute("Debug.Print " + expression);
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("Named arguments to * are not implemented yet");
    }

    [Fact]
    public async Task Unix_hidden_is_excluded_from_wildcard_Kill_unless_the_host_has_real_hidden_attributes()
    {
        // A leading dot is reported as Hidden by .NET on Unix and an ordinary name on Windows.
        File.WriteAllText(Path.Combine(dir,".hidden.log"),"h");
        File.WriteAllText(Path.Combine(dir,"normal.log"),"n");
        await Execute("Kill \"*.log\"\nDebug.Print Dir(\"normal.log\")");
        AssertDebugLog([new Vb6Value("")]);
        File.Exists(Path.Combine(dir,".hidden.log")).Should().Be(!OperatingSystem.IsWindows());
    }

    [Theory]
    [InlineData("Debug.Print Dir()", "Invalid procedure call or argument")]
    [InlineData("Debug.Print Dir(Null)", "Type mismatch")]
    [InlineData("Debug.Print FileLen(\"*\")", "Bad file name or number")]
    [InlineData("Kill \"missing\"", "File not found")]
    [InlineData("Open \"a.txt\" For Input As #1\nKill \"a.txt\"", "File already open")]
    [InlineData("Name \"a.txt\" As \"b.txt\"", "File already exists")]
    [InlineData("Debug.Print CurDir(\"Q\")", "Device unavailable")]
    [InlineData("FileCopy \"a.txt\",\"a.txt\"", "Permission denied")]
    [InlineData("RmDir \"sub\"", "Path/File access error")]
    [InlineData("MkDir \"missing\\new\"", "Path not found")]
    public async Task Errors_use_the_measured_descriptions(string code, string description)
    {
        await Execute("On Error Resume Next\n" + code + "\nDebug.Print Err.Description\nClose");
        AssertDebugLog([new Vb6Value(description)]);
    }

}
