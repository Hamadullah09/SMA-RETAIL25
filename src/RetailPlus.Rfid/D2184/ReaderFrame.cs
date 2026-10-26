namespace RetailPlus.Rfid.D2184;

/// <summary>
/// One packet of the UHF RFID Reader Serial Interface Protocol V3.1:
/// <c>A0 | Len | Address | Cmd | Data… | Check</c>, where Len counts the bytes after itself and Check is the two's
/// complement of the sum of every byte before it. Replies have the same shape (the command echoed in Cmd).
/// </summary>
public sealed record ReaderFrame(byte Address, byte Command, byte[] Data)
{
    public const byte Head = 0xA0;
    public const int MaxDataLength = 252;

    public static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        byte sum = 0;
        foreach (var b in bytes) sum += b;
        return (byte)((~sum + 1) & 0xFF);
    }

    public static byte[] Encode(byte address, byte command, ReadOnlySpan<byte> data = default)
    {
        if (data.Length > MaxDataLength) throw new ArgumentException("Too much data for one frame.", nameof(data));
        var frame = new byte[data.Length + 5];
        frame[0] = Head;
        frame[1] = (byte)(data.Length + 3);
        frame[2] = address;
        frame[3] = command;
        data.CopyTo(frame.AsSpan(4));
        frame[^1] = Checksum(frame.AsSpan(0, frame.Length - 1));
        return frame;
    }

    public byte[] ToBytes() => Encode(Address, Command, Data);

    public override string ToString() => $"cmd 0x{Command:X2} addr {Address} [{Convert.ToHexString(Data)}]";
}

/// <summary>
/// Cuts a byte stream into frames. TCP and serial deliver data in arbitrary pieces, so partial frames are kept until
/// the rest arrives, and noise or a frame with a bad checksum is skipped up to the next 0xA0.
/// </summary>
public sealed class FrameDecoder
{
    private const int MaxBuffer = 1 << 20;
    private byte[] _buffer = new byte[4096];
    private int _count;

    /// <summary>Bytes thrown away while looking for the start of a frame (noise, corrupted frames).</summary>
    public long DroppedBytes { get; private set; }

    public List<ReaderFrame> Feed(ReadOnlySpan<byte> chunk)
    {
        if (_count + chunk.Length > _buffer.Length)
        {
            if (_count + chunk.Length > MaxBuffer)
            {
                DroppedBytes += _count;
                _count = 0;
            }
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _count + chunk.Length));
        }
        chunk.CopyTo(_buffer.AsSpan(_count));
        _count += chunk.Length;

        var frames = new List<ReaderFrame>();
        int pos = 0;
        while (true)
        {
            while (pos < _count && _buffer[pos] != ReaderFrame.Head)
            {
                pos++;
                DroppedBytes++;
            }
            if (_count - pos < 2) break;
            int len = _buffer[pos + 1];
            if (len < 3)
            {
                pos++;
                DroppedBytes++;
                continue;
            }
            int total = len + 2;
            if (_count - pos < total) break;
            var span = _buffer.AsSpan(pos, total);
            if (ReaderFrame.Checksum(span[..^1]) != span[^1])
            {
                pos++;
                DroppedBytes++;
                continue;
            }
            frames.Add(new ReaderFrame(span[2], span[3], span[4..^1].ToArray()));
            pos += total;
        }
        if (pos > 0)
        {
            Buffer.BlockCopy(_buffer, pos, _buffer, 0, _count - pos);
            _count -= pos;
        }
        return frames;
    }
}
