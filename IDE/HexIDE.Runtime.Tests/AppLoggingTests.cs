using System.Text;
using System.Text.RegularExpressions;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6, native and p-code: https://github.com/hexide-io/HexIDE/issues/2552.
public class AppLoggingTests : BaseVBTestFixture, IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "hexide-app-log-" + Guid.NewGuid());
    private VbApp App() => new(new AppInfo(Path: folder));
    private static Vb6Value Read(VbApp app, string name)
    {
        app.TryGetProperty(name, out var value).Should().BeTrue();
        return value;
    }
    public AppLoggingTests() => Directory.CreateDirectory(folder);
    public void Dispose() => Directory.Delete(folder, true);

    [Fact]
    public void Defaults_are_Long_one_and_empty_String()
    {
        var app = App();
        Read(app, "LogMode").Should().Be(new Vb6Value(1L));
        Read(app, "LogPath").Should().Be(new Vb6Value(""));
    }

    [Fact]
    public void Default_event_selects_NT_without_writing_a_file()
    {
        var app = App();
        app.Call("LogEvent", [new("hello")]);
        Read(app, "LogMode").Should().Be(new Vb6Value(3L));
        Read(app, "LogPath").Value.Should().Be("");
        Directory.GetFiles(folder).Should().BeEmpty();
    }

    [Theory]
    [InlineData("hello", 4, "Information ")]
    [InlineData("careful", 2, "Warning     ")]
    [InlineData("boom", 1, "Error       ")]
    [InlineData("plain", 0, "Information ")]
    [InlineData(42, 0, "Information ")]
    public void File_events_match_the_measured_bytes(object message, int type, string prefix)
    {
        var path = Path.Combine(folder, "applog.txt");
        var app = App();
        app.Call("StartLogging", [new(path), new(2)]);
        Read(app, "LogMode").Should().Be(new Vb6Value(2L));
        Read(app, "LogPath").Value.Should().Be(path);
        var value = message is int number ? new Vb6Value(number) : new Vb6Value((string)message);
        app.Call("LogEvent", type == 0 ? [value] : [value, new(type)]);
        var bytes = File.ReadAllBytes(path);
        var line = Encoding.ASCII.GetString(bytes);
        var match = Regex.Match(line, @": Thread ID: (-?\d+) ,Logged: ");
        match.Success.Should().BeTrue();
        bytes.Should().Equal(Encoding.ASCII.GetBytes($"{prefix}Application {path}: Thread ID: {match.Groups[1].Value} ,Logged: {message}\r\n"));
    }

    [Fact]
    public void Type_nine_raises_380_without_a_line()
    {
        var app = App();
        var path = Path.Combine(folder, "applog.txt");
        app.Call("StartLogging", [new(path), new(2)]);
        var call = () => app.Call("LogEvent", [new("bad"), new(9)]);
        call.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(380);
        (!File.Exists(path) || File.ReadAllBytes(path).Length == 0).Should().BeTrue();
    }

    [Fact]
    public void Existing_file_is_appended_to()
    {
        var path = Path.Combine(folder, "applog.txt");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("old line\r\n"));
        var app = App();
        app.Call("StartLogging", [new(path), new(2)]);
        app.Call("LogEvent", [new("new")]);
        File.ReadAllText(path).Should().StartWith("old line\r\nInformation Application ").And.EndWith(" ,Logged: new\r\n");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(18)]
    public void Off_and_overwrite_disable_logging(int mode)
    {
        var path = Path.Combine(folder, "applog.txt");
        if (mode == 18) File.WriteAllBytes(path, Encoding.ASCII.GetBytes("old line\r\n"));
        var app = App();
        app.Call("StartLogging", [new(path), new(mode)]);
        Read(app, "LogMode").Should().Be(new Vb6Value(1L));
        app.Call("LogEvent", [new("ignored")]);
        if (mode == 18) File.ReadAllBytes(path).Should().Equal(Encoding.ASCII.GetBytes("old line\r\n"));
        else File.Exists(path).Should().BeFalse();
    }

    [Theory]
    [InlineData(2, "vbevents.log")]
    [InlineData(3, "")]
    public void Empty_path_selects_the_measured_default(int mode, string expected)
    {
        var app = App();
        app.Call("StartLogging", [new(""), new(mode)]);
        Read(app, "LogMode").Should().Be(new Vb6Value((long)mode));
        Read(app, "LogPath").Value.Should().Be(expected);
        app.Call("LogEvent", [new("hello")]);
        File.Exists(Path.Combine(folder, "vbevents.log")).Should().Be(mode == 2);
    }

    [Fact]
    public void LogPath_assignment_is_rejected_as_read_only()
    {
        var app = App();
        var set = () => app.TrySetProperty("LogPath", new("x.txt"));
        set.Should().Throw<VBCompileErrorException>().WithMessage("*Can't assign to read-only property*");
    }

    [Theory]
    [InlineData("StartLogging", 0)]
    [InlineData("StartLogging", 32)]
    [InlineData("LogEvent", 3)]
    public void Unmeasured_modes_and_event_types_are_explicitly_unimplemented(string method, int option)
    {
        var app = App();
        var call = () => app.Call(method, [new("text"), new(option)]);
        call.Should().Throw<NotImplementedException>();
    }

    [Theory]
    [InlineData("omitted mode")]
    [InlineData("path conversion")]
    [InlineData("mode conversion")]
    [InlineData("NT path")]
    [InlineData("event type conversion")]
    [InlineData("message conversion")]
    [InlineData("encoding")]
    [InlineData("file failure")]
    [InlineData("unsaved relative path")]
    public void Unmeasured_edges_are_refused_without_writing(string edge)
    {
        var app = edge == "unsaved relative path" ? new VbApp(AppInfo.None) : App();
        var path = Path.Combine(folder, "applog.txt");
        Action call = edge switch
        {
            "omitted mode" => () => app.Call("StartLogging", [new(path)]),
            "path conversion" => () => app.Call("StartLogging", [new(42), new(2)]),
            "mode conversion" => () => app.Call("StartLogging", [new(path), new("2")]),
            "NT path" => () => app.Call("StartLogging", [new(path), new(3)]),
            "event type conversion" => () => app.Call("LogEvent", [new("hello"), new("4")]),
            "message conversion" => () => app.Call("LogEvent", [new(1.5)]),
            _ => () =>
            {
                var file = edge == "file failure" ? Path.Combine(folder, "missing", "log.txt")
                    : edge == "unsaved relative path" ? "applog.txt" : path;
                app.Call("StartLogging", [new(file), new(2)]);
                app.Call("LogEvent", [new(edge == "encoding" ? "é" : "hello")]);
            },
        };
        call.Should().Throw<NotImplementedException>();
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task Interpreter_dispatches_logging_methods_and_properties()
    {
        var path = Path.Combine(folder, "applog.txt");
        await Run($"""
            App.StartLogging "{path}", vbLogToFile
            App.LogEvent 42
            Debug.Print App.LogMode
            Debug.Print App.LogPath
            """);
        AssertDebugLog([new(2L), new(path)]);
        File.ReadAllText(path).Should().EndWith(" ,Logged: 42\r\n");
    }
}
