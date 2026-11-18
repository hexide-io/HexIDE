using System.Runtime.InteropServices;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class LspMessageHandlerTests
{
    private static byte[] ArrayOf(Memory<byte> memory)
    {
        MemoryMarshal.TryGetArray((ReadOnlyMemory<byte>)memory, out var segment).Should().BeTrue();
        return segment.Array!;
    }

    [Fact]
    public void ConcurrentDisposalsReturnAnArrayOnlyOnceAndLogOnce()
    {
        var logger = Substitute.For<ILogger>();
        using var pool = new ConnectionMemoryPool(logger);
        var owner = pool.Rent();
        var array = ArrayOf(owner.Memory);
        Parallel.For(0, 1000, _ => owner.Dispose());
        pool.SuppressedDisposals.Should().Be(999);
        logger.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)).Should().Be(1);
        using var first = pool.Rent();
        using var second = pool.Rent();
        ArrayOf(first.Memory).Should().BeSameAs(array);
        ArrayOf(second.Memory).Should().NotBeSameAs(array, "a returned buffer cannot be rented twice concurrently");
        pool.RentsAfterSuppressed.Should().Be(2);
    }

    [Fact]
    public void ReleasedBuffersStayInsideTheirConnection()
    {
        using var first = new ConnectionMemoryPool(null);
        using var second = new ConnectionMemoryPool(null);
        var owner = first.Rent();
        var array = ArrayOf(owner.Memory);
        owner.Dispose();
        using var other = second.Rent();
        ArrayOf(other.Memory).Should().NotBeSameAs(array);
        using var reused = first.Rent();
        ArrayOf(reused.Memory).Should().BeSameAs(array);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalRetainsStreamOwnershipEvenIfStreamsThrow(bool separate)
    {
        var write = new ThrowingStream();
        var read = separate ? new ThrowingStream() : write;
        var handler = new LspMessageHandler(write, read, new SystemTextJsonFormatter());
        await handler.DisposeAsync();
        write.Closed.Should().BeTrue();
        read.Closed.Should().BeTrue();
    }

    private sealed class ThrowingStream : MemoryStream
    {
        public bool Closed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
            throw new IOException("test disposal failure");
        }
    }
}
