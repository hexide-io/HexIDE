using HexIDE.Lsp;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// The tap the stdio and named-pipe transports put on their channel, so the record can say when bytes actually
/// reached the server and came back rather than only when a message was serialized (#390).
/// </summary>
public class ChannelTapStreamTests
{
    private static (ChannelTapStream Tap, List<string> Notes) Tapped(Stream inner)
    {
        var notes = new List<string>();
        return (new ChannelTapStream(inner, text => { lock (notes) notes.Add(text); }), notes);
    }

    [Fact]
    public async Task A_write_says_when_it_began_and_when_its_bytes_were_flushed_through_once_each()
    {
        var inner = new MemoryStream();
        var (tap, notes) = Tapped(inner);

        await tap.WriteAsync("Content-Length: 2\r\n\r\n"u8.ToArray(), TestContext.Current.CancellationToken);
        notes.Should().Equal("began writing 21 bytes to the server");

        await tap.WriteAsync("{}"u8.ToArray(), TestContext.Current.CancellationToken);
        await tap.FlushAsync(TestContext.Current.CancellationToken);
        await tap.WriteAsync("{}"u8.ToArray(), TestContext.Current.CancellationToken);
        await tap.FlushAsync(TestContext.Current.CancellationToken);

        notes.Should().Equal("began writing 21 bytes to the server", "the first 23 bytes reached the server's channel");
        inner.ToArray().Should().Equal("Content-Length: 2\r\n\r\n{}{}"u8.ToArray(), "the tap passes every byte through");
    }

    [Fact]
    public async Task A_flush_with_nothing_written_says_nothing()
    {
        var (tap, notes) = Tapped(new MemoryStream());

        await tap.FlushAsync(TestContext.Current.CancellationToken);

        notes.Should().BeEmpty("no bytes reached anything");
    }

    [Fact]
    public async Task A_read_says_when_it_began_waiting_and_when_the_first_bytes_arrived_once_each()
    {
        var (tap, notes) = Tapped(new MemoryStream("abcdef"u8.ToArray()));
        var buffer = new byte[4];

        (await tap.ReadAsync(buffer, TestContext.Current.CancellationToken)).Should().Be(4);
        (await tap.ReadAsync(buffer, TestContext.Current.CancellationToken)).Should().Be(2);
        (await tap.ReadAsync(buffer, TestContext.Current.CancellationToken)).Should().Be(0);

        notes.Should().Equal("began waiting for the server's first bytes", "the first 4 bytes arrived from the server");
    }

    [Fact]
    public async Task A_read_that_is_never_answered_still_says_it_began_waiting()
    {
        // The fault #390 needs told apart: a read our side never issued, and one the server never answered.
        var pipe = new System.IO.Pipelines.Pipe();
        var (tap, notes) = Tapped(pipe.Reader.AsStream());

        var pending = tap.ReadAsync(new byte[4], TestContext.Current.CancellationToken).AsTask();
        await Task.Yield();

        pending.IsCompleted.Should().BeFalse();
        notes.Should().Equal("began waiting for the server's first bytes");
        await pipe.Writer.CompleteAsync();
        (await pending).Should().Be(0);
    }
}
