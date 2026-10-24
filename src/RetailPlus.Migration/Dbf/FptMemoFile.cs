using System.Buffers.Binary;

namespace RetailPlus.Migration.Dbf;

/// <summary>
/// Read-only access to a FoxPro FPT memo file. Header: next free block (big-endian) at 0, block size (big-endian) at
/// 6. Each memo starts on a block boundary with a big-endian type and length, followed by the data.
/// </summary>
internal sealed class FptMemoFile : IDisposable
{
    private readonly FileStream _stream;

    public FptMemoFile(string path)
    {
        Path = path;
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> header = stackalloc byte[8];
        BlockSize = _stream.Length >= 8 && _stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) == 8
            ? BinaryPrimitives.ReadUInt16BigEndian(header[6..8])
            : 0;
    }

    public string Path { get; }
    public int BlockSize { get; }

    /// <summary>Reads the memo at a block number, or returns the reason it can't be read (the oracle's wording).</summary>
    public (DbfMemo? Memo, string? Error) Read(long block)
    {
        long offset = block * BlockSize;
        long length = _stream.Length;
        if (BlockSize == 0 || offset + 8 > length)
            return (null, $"block {block} past end of memo file");
        Span<byte> head = stackalloc byte[8];
        _stream.Position = offset;
        _stream.ReadExactly(head);
        int type = (int)BinaryPrimitives.ReadUInt32BigEndian(head[..4]);
        long size = BinaryPrimitives.ReadUInt32BigEndian(head[4..8]);
        if (offset + 8 + size > length)
            return (null, $"memo in block {block} runs past end of memo file");
        var data = new byte[size];
        _stream.ReadExactly(data);
        return (new DbfMemo(type, Cp1252.Decode(data)), null);
    }

    public void Dispose() => _stream.Dispose();
}
