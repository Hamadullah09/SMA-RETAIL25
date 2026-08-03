using System.Buffers.Binary;
using System.IO.Compression;
using FluentAssertions;
using Retail25.Domain.Catalog;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// The hand-written PNG encoder the demo seeder uses.
/// <para>
/// Worth testing precisely because nothing else can catch it. A wrong chunk length or a bad CRC still
/// stores in the database and still serves with a 200; the only symptom is a broken image icon on a
/// till, days later, on somebody else's machine. So the bytes are checked against the format here.
/// </para>
/// <para>
/// No database and no Docker: this is arithmetic.
/// </para>
/// </summary>
public sealed class DemoImageFactoryTests
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private const int Size = 96;

    [Fact]
    public void The_bytes_are_a_well_formed_png()
    {
        var png = DemoImageFactory.Create("DEMO0001");

        png.Take(8).Should().Equal(Signature);

        var chunks = Chunks(png).ToList();

        chunks.Select(c => c.Type).Should().Equal(["IHDR", "IDAT", "IEND"]);

        var header = chunks[0].Data;
        BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, 4)).Should().Be(Size);
        BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4, 8 - 4)).Should().Be(Size);
        header[8].Should().Be(8, "eight bits per channel");
        header[9].Should().Be(2, "truecolour, no alpha");
        header[12].Should().Be(0, "not interlaced");
    }

    /// <summary>
    /// Every chunk carries a CRC of its own type and payload. Reading it back is the check that the
    /// length written into the stream matches the bytes that followed it — the failure this encoder
    /// is most likely to have.
    /// </summary>
    [Fact]
    public void Every_chunk_checksum_matches_its_contents()
    {
        var png = DemoImageFactory.Create("DEMO0002");

        foreach (var chunk in Chunks(png))
        {
            var typeAndData = chunk.Type.Select(c => (byte)c).Concat(chunk.Data).ToArray();
            Crc32(typeAndData).Should().Be(chunk.Crc, $"the {chunk.Type} chunk should not be corrupt");
        }
    }

    /// <summary>
    /// The image data inflates to exactly one filter byte plus three bytes per pixel, per row. A
    /// scanline short of that is a picture that decodes as far as the truncation and then stops.
    /// </summary>
    [Fact]
    public void The_image_data_inflates_to_a_full_set_of_scanlines()
    {
        var png = DemoImageFactory.Create("DEMO0003");
        var idat = Chunks(png).Single(c => c.Type == "IDAT").Data;

        using var compressed = new MemoryStream(idat);
        using var inflate = new ZLibStream(compressed, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflate.CopyTo(raw);

        raw.Length.Should().Be(Size * (1 + (Size * 3)));

        // Filter byte 0 ("none") at the start of every scanline, which is what the encoder claims.
        var bytes = raw.ToArray();
        for (var y = 0; y < Size; y++)
        {
            bytes[y * (1 + (Size * 3))].Should().Be(0, $"scanline {y} should be unfiltered");
        }
    }

    /// <summary>
    /// The same item gets the same picture on every machine — otherwise two tills seeded from the
    /// same catalogue show the same product in different colours, and an ETag means nothing.
    /// </summary>
    [Fact]
    public void The_same_seed_gives_the_same_picture_and_a_different_seed_does_not()
    {
        DemoImageFactory.Create("DEMO0004").Should().Equal(DemoImageFactory.Create("DEMO0004"));
        DemoImageFactory.Create("DEMO0004").Should().NotEqual(DemoImageFactory.Create("DEMO0005"));
    }

    /// <summary>
    /// The end that matters: the domain's own upload gate accepts it. That gate checks the magic
    /// number rather than the declared type, so this is the encoder and the validator agreeing.
    /// </summary>
    [Fact]
    public void The_domain_accepts_it_as_a_real_png()
    {
        var created = ProductImage.Create(
            Guid.NewGuid(), DemoImageFactory.Create("DEMO0006"), DemoImageFactory.ContentType);

        created.IsSuccess.Should().BeTrue($"it should pass validation, but failed with '{created.Error.Code}'");
        created.Value.ContentType.Should().Be("image/png");
        created.Value.Content.Length.Should().BeLessThan(ProductImage.MaximumBytes);
    }

    // ---------------------------------------------------------------------------------------------

    private static IEnumerable<(string Type, byte[] Data, uint Crc)> Chunks(byte[] png)
    {
        var offset = 8;

        while (offset < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            var type = new string(png.Skip(offset + 4).Take(4).Select(b => (char)b).ToArray());
            var data = png.Skip(offset + 8).Take(length).ToArray();
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length, 4));

            yield return (type, data, crc);

            offset += 12 + length;
        }
    }

    private static uint Crc32(byte[] bytes)
    {
        var c = 0xFFFFFFFFu;

        foreach (var b in bytes)
        {
            c ^= b;

            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
        }

        return c ^ 0xFFFFFFFFu;
    }
}
