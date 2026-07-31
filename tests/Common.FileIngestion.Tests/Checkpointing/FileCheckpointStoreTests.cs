using Common.FileIngestion.Abstractions;
using Common.FileIngestion.Checkpointing;

namespace Common.FileIngestion.Tests.Checkpointing;

public sealed class FileCheckpointStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ckpt-" + Guid.NewGuid().ToString("N"));

    private FileCheckpointStore Store() => new(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveThenLoad_RoundTrips()
    {
        var store = Store();
        var watermark = new Watermark("SRC1", "F", 1200, 1, 0);

        await store.SaveAsync(watermark, CancellationToken.None);
        var loaded = await store.LoadAsync("SRC1", CancellationToken.None);

        Assert.Equal(watermark, loaded);
    }

    [Fact]
    public async Task MultipleSources_HaveIndependentWatermarks()
    {
        var store = Store();

        await store.SaveAsync(new Watermark("SRC1", "F", 100, 1, 0), CancellationToken.None);
        await store.SaveAsync(new Watermark("SRC2", "F", 200, 2, 1), CancellationToken.None);

        Assert.Equal(100, (await store.LoadAsync("SRC1", CancellationToken.None))!.ByteOffset);
        Assert.Equal(200, (await store.LoadAsync("SRC2", CancellationToken.None))!.ByteOffset);
    }

    [Fact]
    public async Task Load_Missing_ReturnsNull()
    {
        Assert.Null(await Store().LoadAsync("NOPE", CancellationToken.None));
    }

    [Fact]
    public async Task Save_Overwrites_PreviousWatermark()
    {
        var store = Store();

        await store.SaveAsync(new Watermark("S", "F", 100, 1, 0), CancellationToken.None);
        await store.SaveAsync(new Watermark("S", "F", 300, 3, 1), CancellationToken.None);

        Assert.Equal(300, (await store.LoadAsync("S", CancellationToken.None))!.ByteOffset);
    }

    [Fact]
    public async Task Clear_RemovesWatermark()
    {
        var store = Store();
        await store.SaveAsync(new Watermark("S", "F", 100, 1, 0), CancellationToken.None);

        await store.ClearAsync("S", CancellationToken.None);

        Assert.Null(await store.LoadAsync("S", CancellationToken.None));
    }

    [Fact]
    public async Task Clear_Missing_IsNoOp()
    {
        var store = Store();

        await store.ClearAsync("missing", CancellationToken.None); // does not throw

        Assert.Null(await store.LoadAsync("missing", CancellationToken.None));
    }

    [Fact]
    public void Constructor_BlankDirectory_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new FileCheckpointStore("  "));
    }

    [Fact]
    public async Task Save_NullWatermark_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Store().SaveAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task Load_BlankSourceKey_Throws()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Store().LoadAsync("  ", CancellationToken.None));
    }

    [Fact]
    public async Task Save_SourceKeyWithInvalidPathChars_Throws()
    {
        var store = Store();

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.SaveAsync(new Watermark("bad/key", "F", 0, 0, 0), CancellationToken.None));
    }
}
