using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Tests;

public class StatusBarServiceTests
{
    private static ILocalizationService MakeLoc()
    {
        var loc = Substitute.For<ILocalizationService>();
        loc.GetString("Str.StatusBar.Ready").Returns("Ready");
        return loc;
    }

    private readonly StatusBarService _sut = new(MakeLoc());

    // ── Default state ────────────────────────────────────────────

    [Fact]
    public void Message_DefaultIsReady()
    {
        _sut.Message.Should().Be("Ready");
    }

    [Fact]
    public void Line_DefaultIsNull()
    {
        _sut.Line.Should().BeNull();
    }

    [Fact]
    public void Column_DefaultIsNull()
    {
        _sut.Column.Should().BeNull();
    }

    [Fact]
    public void InsertMode_DefaultIsTrue()
    {
        _sut.InsertMode.Should().BeTrue();
    }

    // ── SetMessage ───────────────────────────────────────────────

    [Fact]
    public void SetMessage_UpdatesMessage()
    {
        _sut.SetMessage("Running…");
        _sut.Message.Should().Be("Running…");
    }

    [Fact]
    public void SetMessage_RaisesPropertyChanged()
    {
        var raised = false;
        _sut.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(StatusBarService.Message))
                raised = true;
        };

        _sut.SetMessage("Running…");
        raised.Should().BeTrue();
    }

    // ── ClearMessage ─────────────────────────────────────────────

    [Fact]
    public void ClearMessage_RevertsToReady()
    {
        _sut.SetMessage("Something");
        _sut.ClearMessage();
        _sut.Message.Should().Be("Ready");
    }

    // ── SetTemporaryMessage ──────────────────────────────────────

    [Fact]
    public async Task SetTemporaryMessage_RevertsAfterDuration()
    {
        _sut.SetTemporaryMessage("3 replacements made", TimeSpan.FromMilliseconds(100));
        _sut.Message.Should().Be("3 replacements made");

        // Poll for the timer-driven revert rather than asserting after a fixed delay: the revert runs on
        // a thread-pool thread, which can be starved under a parallel test run on a busy CI box, making a
        // fixed wait flaky. A generous poll stays deterministic without coupling to exact timing.
        (await WaitForAsync(() => _sut.Message == "Ready")).Should()
            .BeTrue("the 100 ms temporary message must revert to Ready");
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        for (var waited = 0; waited < timeoutMs; waited += 20)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    [Fact]
    public void SetTemporaryMessage_CanBeCancelledBySetMessage()
    {
        _sut.SetTemporaryMessage("Temp", TimeSpan.FromSeconds(10));
        _sut.SetMessage("Permanent");
        _sut.Message.Should().Be("Permanent");
    }

    [Fact]
    public void SetTemporaryMessage_CanBeCancelledByClearMessage()
    {
        _sut.SetTemporaryMessage("Temp", TimeSpan.FromSeconds(10));
        _sut.ClearMessage();
        _sut.Message.Should().Be("Ready");
    }

    // ── Line / Column ────────────────────────────────────────────

    [Fact]
    public void Line_SetAndGet()
    {
        _sut.Line = 42;
        _sut.Line.Should().Be(42);
    }

    [Fact]
    public void Column_SetAndGet()
    {
        _sut.Column = 17;
        _sut.Column.Should().Be(17);
    }

    [Fact]
    public void Line_RaisesPropertyChanged()
    {
        var raised = false;
        _sut.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(StatusBarService.Line))
                raised = true;
        };

        _sut.Line = 10;
        raised.Should().BeTrue();
    }

    [Fact]
    public void Line_DoesNotRaise_WhenValueUnchanged()
    {
        _sut.Line = 5;
        var raised = false;
        _sut.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(StatusBarService.Line))
                raised = true;
        };

        _sut.Line = 5;
        raised.Should().BeFalse();
    }

    // ── InsertMode ───────────────────────────────────────────────

    [Fact]
    public void InsertMode_SetToFalse()
    {
        _sut.InsertMode = false;
        _sut.InsertMode.Should().BeFalse();
    }

    [Fact]
    public void InsertMode_RaisesPropertyChanged()
    {
        var raised = false;
        _sut.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(StatusBarService.InsertMode))
                raised = true;
        };

        _sut.InsertMode = false;
        raised.Should().BeTrue();
    }
}
