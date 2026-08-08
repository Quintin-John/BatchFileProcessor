using System.Security.Cryptography;
using System.Text;
using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Reading;

namespace Common.FileIngestion.Tests.Reading;

public sealed class FileIdHasherTests
{
    private static async Task<string> ReaderFileId(byte[] data) =>
        await new StreamRecordReader(4, 1, Encoding.ASCII).ReadAsync(
            new MemoryStream(data), (_, _) => ValueTask.CompletedTask, CancellationToken.None);

    [Fact]
    public async Task Hasher_And_Reader_AgreeOnFileId_ForSameContent()
    {
        // The whole point of the shared definition: the pre-read hash and the read-pass integrity
        // guard must produce the identical FileId, and it must be canonical uppercase-hex SHA-256.
        var data = Encoding.ASCII.GetBytes("AAAA\nBBBB\nCCCC\n");

        var hasherId = await FileIdHasher.ComputeAsync(new MemoryStream(data), CancellationToken.None);
        var readerId = await ReaderFileId(data);

        Assert.Equal(readerId, hasherId);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(data)), hasherId);
    }

    [Fact]
    public async Task Hasher_MatchesCanonical_ForContentLargerThanTheReadBuffer()
    {
        // Larger than FileContentHash.StreamBufferBytes so the chunked read loop runs many iterations;
        // the accumulated digest must still equal the one-shot canonical hash.
        var data = new byte[81920 * 2 + 7];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 251);
        }

        var hasherId = await FileIdHasher.ComputeAsync(new MemoryStream(data), CancellationToken.None);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(data)), hasherId);
    }

    [Fact]
    public async Task Hasher_DifferentContent_ProducesDifferentFileId()
    {
        var a = await FileIdHasher.ComputeAsync(new MemoryStream(Encoding.ASCII.GetBytes("AAAA")), CancellationToken.None);
        var b = await FileIdHasher.ComputeAsync(new MemoryStream(Encoding.ASCII.GetBytes("BBBB")), CancellationToken.None);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task Hasher_NullStream_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => FileIdHasher.ComputeAsync(null!, CancellationToken.None));
}
