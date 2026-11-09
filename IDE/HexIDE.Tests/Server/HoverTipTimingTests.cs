using HexIDE.Desktop.Server;

namespace HexIDE.Tests.Server;

/// <summary>
/// hover says how long a tip took to open, and quotes the delay when none opened, so a caller need not find a tip's
/// delay by trying several dwell times (#2949).
/// </summary>
public class HoverTipTimingTests
{
    [Fact]
    public void An_opened_tip_says_how_long_after_the_move_it_was_seen()
    {
        HexIdeTools.TipOpenedAfter(450).Should().Be("; seen open about 450ms after the move");
    }

    [Theory]
    [InlineData(400, 300)]
    [InlineData(400, 400)]
    public void A_delay_the_watch_did_not_outlast_says_to_pass_a_longer_dwell(int showDelay, int dwell)
    {
        HexIdeTools.ShowDelayNote(showDelay, dwell).Should()
            .Be("its ToolTip.ShowDelay is 400ms, longer than the watch: pass a dwellMs above that");
    }

    [Fact]
    public void A_delay_the_watch_outlasted_is_only_quoted()
    {
        HexIdeTools.ShowDelayNote(400, 600).Should().Be("its ToolTip.ShowDelay is 400ms");
    }
}
