using Common.FileIngestion.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Common.FileIngestion.Tests.Sources;

public sealed class FolderFileSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fsrc-" + Guid.NewGuid().ToString("N"));

    private string Incoming => Path.Combine(_root, "incoming");
    private string Processing => Path.Combine(_root, "processing");
    private string Done => Path.Combine(_root, "done");
    private string Failed => Path.Combine(_root, "failed");

    private FolderFileSource? _tracked;

    private FolderFileSource Source() => _tracked = new FolderFileSource(_root, NullLogger<FolderFileSource>.Instance);

    private FolderFileSource SourceWith(ILogger<FolderFileSource> logger) => _tracked = new FolderFileSource(_root, logger);

    private void DropIncoming(string name, string content = "x") =>
        File.WriteAllText(Path.Combine(Incoming, name), content);

    public void Dispose()
    {
        _tracked?.Dispose(); // release the ownership lock before removing the root
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Constructor_CreatesSubdirectories()
    {
        _ = Source();

        Assert.True(Directory.Exists(Incoming));
        Assert.True(Directory.Exists(Processing));
        Assert.True(Directory.Exists(Done));
        Assert.True(Directory.Exists(Failed));
    }

    [Fact]
    public void Claim_MovesIncomingToProcessing_InOrder()
    {
        var source = Source();
        DropIncoming("b.dat");
        DropIncoming("a.dat");

        var claimed = source.Claim();

        Assert.Equal(2, claimed.Count);
        Assert.Equal("a.dat", claimed[0].Name);
        Assert.Equal("b.dat", claimed[1].Name);
        Assert.Empty(Directory.EnumerateFiles(Incoming));
        Assert.Equal(2, Directory.EnumerateFiles(Processing).Count());
        Assert.All(claimed, c => Assert.True(File.Exists(c.ProcessingPath)));
    }

    [Fact]
    public void Claim_EmptyIncoming_ReturnsEmpty()
    {
        Assert.Empty(Source().Claim());
    }

    [Fact]
    public void Claim_SkipsFileAlreadyClaimed()
    {
        var source = Source();
        DropIncoming("dup.dat");
        File.WriteAllText(Path.Combine(Processing, "dup.dat"), "already"); // orphan with same name

        var claimed = source.Claim();

        Assert.Empty(claimed);
        Assert.True(File.Exists(Path.Combine(Incoming, "dup.dat"))); // left for the orphan to clear
    }

    [Fact]
    public void RecoverOrphans_ReturnsFilesLeftInProcessing()
    {
        var source = Source();
        File.WriteAllText(Path.Combine(Processing, "orphan.dat"), "interrupted");

        var orphans = source.RecoverOrphans();

        var orphan = Assert.Single(orphans);
        Assert.Equal("orphan.dat", orphan.Name);
    }

    [Fact]
    public void Complete_MovesProcessingToDone()
    {
        var source = Source();
        DropIncoming("f.dat");
        var claimed = source.Claim().Single();

        source.Complete(claimed);

        Assert.False(File.Exists(claimed.ProcessingPath));
        Assert.True(File.Exists(Path.Combine(Done, "f.dat")));
    }

    [Fact]
    public void Complete_SameNameArchiveExists_PreservesBothOriginals()
    {
        // Recurring daily file: the same name completes twice; the earlier archive must not be clobbered.
        var source = Source();

        DropIncoming("f.dat", "day1");
        source.Complete(source.Claim().Single());

        DropIncoming("f.dat", "day2");
        source.Complete(source.Claim().Single());

        Assert.Equal("day1", File.ReadAllText(Path.Combine(Done, "f.dat")));
        Assert.Equal("day2", File.ReadAllText(Path.Combine(Done, "f.dat.1")));
    }

    [Fact]
    public void Fail_MovesProcessingToFailed()
    {
        var source = Source();
        DropIncoming("f.dat");
        var claimed = source.Claim().Single();

        source.Fail(claimed);

        Assert.False(File.Exists(claimed.ProcessingPath));
        Assert.True(File.Exists(Path.Combine(Failed, "f.dat")));
    }

    [Fact]
    public void Constructor_BlankRoot_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new FolderFileSource("  ", NullLogger<FolderFileSource>.Instance));
    }

    [Fact]
    public void Constructor_SecondInstanceOnSameRoot_FailsClosed()
    {
        _ = Source(); // first instance owns the root (released by Dispose)

        Assert.Throws<InvalidOperationException>(() => new FolderFileSource(_root, NullLogger<FolderFileSource>.Instance));
    }

    [Fact]
    public void Constructor_AfterDispose_OwnershipReleased_AllowsNewInstance()
    {
        var first = new FolderFileSource(_root, NullLogger<FolderFileSource>.Instance);
        first.Dispose();

        using var second = new FolderFileSource(_root, NullLogger<FolderFileSource>.Instance); // lock released, ownership re-acquired

        Assert.Empty(second.Claim()); // usable: a fresh instance can operate on the root
    }

    [Fact]
    public void Complete_NullFile_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Source().Complete(null!));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FolderFileSource(_root, null!));
    }

    [Fact]
    public void Claim_SameNameAlreadyInProcessing_LogsAtDebug_NotSilent()
    {
        var logger = new CapturingLogger<FolderFileSource>();
        var source = SourceWith(logger);
        DropIncoming("dup.dat");
        File.WriteAllText(Path.Combine(Processing, "dup.dat"), "orphan"); // expected same-name collision

        var claimed = source.Claim();

        Assert.Empty(claimed);
        Assert.Contains(logger.Entries,
            e => e.Level == LogLevel.Debug && e.Message.Contains("dup.dat", StringComparison.Ordinal));
    }

    [Fact]
    public void Claim_UnexpectedMoveFailure_LogsWarning_NotSilent()
    {
        var logger = new CapturingLogger<FolderFileSource>();
        var source = SourceWith(logger);
        DropIncoming("weird.dat");
        // A directory where the claimed file would land makes File.Move fail with an IOException that is
        // not a same-name file collision (File.Exists(destination) is false) — the unexpected-fault path.
        Directory.CreateDirectory(Path.Combine(Processing, "weird.dat"));

        var claimed = source.Claim();

        Assert.Empty(claimed);
        Assert.Contains(logger.Entries,
            e => e.Level == LogLevel.Warning && e.Message.Contains("weird.dat", StringComparison.Ordinal));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
