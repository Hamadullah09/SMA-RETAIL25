using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Retail25.Contracts.Terminals;
using Retail25.Devices.Rfid;
using Xunit;

namespace Retail25.TerminalAgent.UnitTests.Rfid;

/// <summary>
/// What "connected" is allowed to mean.
///
/// <para>
/// Every screen that answers "is the reader up" answers from <c>IsConnected</c>, and it used to mean
/// only that a socket was open. That is the same mistake the firmware handshake was added to fix, one
/// property lower down: opening proves something is listening, not that the thing listening is a
/// reader.
/// </para>
/// <para>
/// A shop found the gap. A serial-to-Ethernet bridge whose reader board is dead accepts every
/// connection instantly, so its session opened, was refused by the firmware query a second later, and
/// retried — forever. Every few beats a heartbeat caught the open moment, and the settings screen
/// showed that reader as Connected with a fresh timestamp, while it had never answered a single
/// frame. A till reporting a dead reader as healthy is worse than one reporting an outage: an outage
/// at least sends somebody to look.
/// </para>
/// </summary>
public sealed class ReaderConnectedStateTests
{
    private static ReaderProfileContract Profile => new(
        Id: 1,
        Name: "Test",
        Host: "192.0.2.1",
        Port: 4001,
        Protocol: ReaderProtocol.UhfSerial,
        AntennaZones: "1=Checkout",
        RssiThresholdDbm: -80,
        MinimumReadCount: 1,
        DebounceMs: 1000,
        CoalesceMs: 100,
        FlushIntervalMs: 200,
        MaxBatchSize: 50,
        AutoAcceptBatches: false,
        ContinuousMode: false);

    private static ReaderConnectionOpener Opener(Stream stream)
        => (_, _, _, _) => Task.FromResult(
            new ReaderConnection(stream, new Nothing(), "fake wire", () => true));

    [Fact]
    public async Task A_device_that_opens_but_never_answers_is_never_reported_as_connected()
    {
        await using var wire = new SilentStream();
        var reader = new UhfSerialRfidReader(NullLogger<UhfSerialRfidReader>.Instance, Opener(wire));

        var connecting = reader.ConnectAsync(Profile, CancellationToken.None);

        // The window that mattered: the socket is open and the firmware query is outstanding. This is
        // where a heartbeat used to catch it and publish a dead reader as healthy.
        await Task.Delay(400);
        var duringHandshake = reader.IsConnected;

        var refused = false;

        try
        {
            await connecting;
        }
        catch (IOException)
        {
            refused = true;
        }

        duringHandshake.Should().BeFalse(
            "the socket was open but nothing had proved it was a reader, and that is exactly the "
            + "moment a check-in used to report one");

        refused.Should().BeTrue("a device that will not answer a firmware query is not a reader");
        reader.IsConnected.Should().BeFalse("and it must not read as connected afterwards either");
    }

    /// <summary>A stream that accepts everything written and never answers — a bridge with nothing behind it.</summary>
    private sealed class SilentStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            // Never answers, and never returns zero either: a closed stream would end the session for
            // a different reason and prove nothing about the handshake.
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
