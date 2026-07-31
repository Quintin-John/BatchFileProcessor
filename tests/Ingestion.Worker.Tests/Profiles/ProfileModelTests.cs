using Ingestion.Worker.Profiles;

namespace Ingestion.Worker.Tests.Profiles;

public sealed class ProfileModelTests
{
    private static ProfileFolders Folders() => new("/in", "/proc", "/done", "/failed");

    private static CompletionSettings Completion() =>
        new(CompletionMode.StableSize, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));

    private static RoutingTargets Routing() => new("dest", "reject");

    private static BatchLimits Batch() => new(500, 200000);

    private static Profile ValidProfile(string name = "p", string? incoming = null) =>
        new(name,
            incoming is null ? Folders() : new ProfileFolders(incoming, "/proc/" + name, "/done/" + name, "/failed/" + name),
            "/cfg.yaml", RecordFormat.FixedLength, Completion(), Routing(), Batch());

    // ---- ProfileFolders ----

    [Fact]
    public void Folders_Valid_ExposesEachPath()
    {
        var folders = Folders();

        Assert.Equal("/in", folders.Incoming);
        Assert.Equal("/proc", folders.Processing);
        Assert.Equal("/done", folders.Done);
        Assert.Equal("/failed", folders.Failed);
    }

    [Fact]
    public void Folders_BlankPath_Throws() =>
        Assert.Throws<ArgumentException>(() => new ProfileFolders(" ", "/proc", "/done", "/failed"));

    [Fact]
    public void Folders_TwoRolesSamePath_Throws() =>
        Assert.Throws<ArgumentException>(() => new ProfileFolders("/x", "/x", "/done", "/failed"));

    // ---- CompletionSettings ----

    [Fact]
    public void Completion_Valid_ExposesModeAndPeriods()
    {
        var c = Completion();

        Assert.Equal(CompletionMode.StableSize, c.Mode);
        Assert.Equal(TimeSpan.FromSeconds(5), c.QuietPeriod);
        Assert.Equal(TimeSpan.FromSeconds(2), c.PollInterval);
    }

    [Fact]
    public void Completion_UndefinedMode_Throws() =>
        Assert.Throws<ArgumentException>(
            () => new CompletionSettings((CompletionMode)99, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

    [Fact]
    public void Completion_NonPositiveQuietPeriod_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CompletionSettings(CompletionMode.StableSize, TimeSpan.Zero, TimeSpan.FromSeconds(1)));

    [Fact]
    public void Completion_NonPositivePollInterval_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CompletionSettings(CompletionMode.StableSize, TimeSpan.FromSeconds(1), TimeSpan.Zero));

    // ---- RoutingTargets ----

    [Fact]
    public void Routing_Valid_ExposesTargets()
    {
        var r = new RoutingTargets("batches", "rejects");

        Assert.Equal("batches", r.Batches);
        Assert.Equal("rejects", r.Rejects);
    }

    [Fact]
    public void Routing_BlankBatches_Throws() =>
        Assert.Throws<ArgumentException>(() => new RoutingTargets(" ", "rejects"));

    [Fact]
    public void Routing_BlankRejects_Throws() =>
        Assert.Throws<ArgumentException>(() => new RoutingTargets("batches", " "));

    // ---- BatchLimits ----

    [Fact]
    public void BatchLimits_Valid_ExposesLimits()
    {
        var b = new BatchLimits(10, 2000);

        Assert.Equal(10, b.MaxRecords);
        Assert.Equal(2000, b.MaxContentBytes);
    }

    [Fact]
    public void BatchLimits_NonPositiveMaxRecords_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BatchLimits(0, 1));

    [Fact]
    public void BatchLimits_NonPositiveMaxContentBytes_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BatchLimits(1, 0));

    // ---- Profile ----

    [Fact]
    public void Profile_Valid_ExposesFields()
    {
        var p = ValidProfile();

        Assert.Equal("p", p.Name);
        Assert.Equal(RecordFormat.FixedLength, p.Format);
        Assert.Equal("dest", p.Routing.Batches);
        Assert.Equal(500, p.Batch.MaxRecords);
    }

    [Fact]
    public void Profile_UndefinedFormat_Throws() =>
        Assert.Throws<ArgumentException>(
            () => new Profile("p", Folders(), "/cfg.yaml", (RecordFormat)99, Completion(), Routing(), Batch()));

    [Fact]
    public void Profile_NullFolders_Throws() =>
        Assert.Throws<ArgumentNullException>(
            () => new Profile("p", null!, "/cfg.yaml", RecordFormat.FixedLength, Completion(), Routing(), Batch()));

    [Fact]
    public void Profile_NullCompletion_Throws() =>
        Assert.Throws<ArgumentNullException>(
            () => new Profile("p", Folders(), "/cfg.yaml", RecordFormat.FixedLength, null!, Routing(), Batch()));

    [Fact]
    public void Profile_NullRouting_Throws() =>
        Assert.Throws<ArgumentNullException>(
            () => new Profile("p", Folders(), "/cfg.yaml", RecordFormat.FixedLength, Completion(), null!, Batch()));

    [Fact]
    public void Profile_NullBatch_Throws() =>
        Assert.Throws<ArgumentNullException>(
            () => new Profile("p", Folders(), "/cfg.yaml", RecordFormat.FixedLength, Completion(), Routing(), null!));

    [Fact]
    public void Profile_BlankName_Throws() =>
        Assert.Throws<ArgumentException>(
            () => new Profile(" ", Folders(), "/cfg.yaml", RecordFormat.FixedLength, Completion(), Routing(), Batch()));

    // ---- ProfileSet ----

    [Fact]
    public void Set_Valid_ExposesProfiles()
    {
        var set = new ProfileSet([ValidProfile("a", "/in/a"), ValidProfile("b", "/in/b")]);

        Assert.Equal(2, set.Profiles.Count);
    }

    [Fact]
    public void Set_Empty_Throws() =>
        Assert.Throws<ArgumentException>(() => new ProfileSet([]));

    [Fact]
    public void Set_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new ProfileSet(null!));

    [Fact]
    public void Set_DuplicateName_Throws() =>
        Assert.Throws<ArgumentException>(() => new ProfileSet([ValidProfile("a", "/in/a"), ValidProfile("a", "/in/b")]));

    [Fact]
    public void Set_DuplicateIncoming_Throws() =>
        Assert.Throws<ArgumentException>(() => new ProfileSet([ValidProfile("a", "/in/x"), ValidProfile("b", "/in/x")]));
}
