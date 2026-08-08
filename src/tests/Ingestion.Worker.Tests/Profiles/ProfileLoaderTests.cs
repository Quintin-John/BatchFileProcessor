using Ingestion.Worker.Profiles;

namespace Ingestion.Worker.Tests.Profiles;

public sealed class ProfileLoaderTests
{
    private const string ValidYaml = """
        profiles:
          - name: feed-a
            incoming: /data/feed/incoming
            processing: /data/feed/processing
            done: /data/feed/done
            failed: /data/feed/failed
            layout: /config/layout.yaml
            format: fixed-length
            completion: { mode: stable-size, quietSeconds: 5, pollSeconds: 2 }
            destination: batches-dest
            rejectDestination: rejects-dest
            batch: { maxRecords: 500, maxContentBytes: 200000 }
        """;

    [Fact]
    public void Load_ValidSingleProfile_MapsAllFields()
    {
        var profile = Assert.Single(ProfileLoader.Load(ValidYaml).Profiles);

        Assert.Equal("feed-a", profile.Name);
        Assert.Equal("/data/feed/incoming", profile.Folders.Incoming);
        Assert.Equal("/data/feed/processing", profile.Folders.Processing);
        Assert.Equal("/data/feed/done", profile.Folders.Done);
        Assert.Equal("/data/feed/failed", profile.Folders.Failed);
        Assert.Equal(["/config/layout.yaml"], profile.LayoutPaths);
        Assert.Equal("fixed-length", profile.Format.Token);
        Assert.Equal(CompletionMode.StableSize, profile.Completion.Mode);
        Assert.Equal(TimeSpan.FromSeconds(5), profile.Completion.QuietPeriod);
        Assert.Equal(TimeSpan.FromSeconds(2), profile.Completion.PollInterval);
        Assert.Equal("batches-dest", profile.Routing.Batches);
        Assert.Equal("rejects-dest", profile.Routing.Rejects);
        Assert.Equal(500, profile.Batch.MaxRecords);
        Assert.Equal(200000, profile.Batch.MaxContentBytes);
    }

    [Fact]
    public void Load_MultipleProfiles_LoadsEach()
    {
        const string yaml = """
            profiles:
              - name: a
                incoming: /in/a
                processing: /proc/a
                done: /done/a
                failed: /failed/a
                layout: /cfg/a.yaml
                format: fixed-length
                completion: { mode: stable-size, quietSeconds: 3, pollSeconds: 1 }
                destination: a-batches
                rejectDestination: a-rejects
                batch: { maxRecords: 10, maxContentBytes: 1000 }
              - name: b
                incoming: /in/b
                processing: /proc/b
                done: /done/b
                failed: /failed/b
                layout: /cfg/b.yaml
                format: fixed-length
                completion: { mode: stable-size, quietSeconds: 3, pollSeconds: 1 }
                destination: b-batches
                rejectDestination: b-rejects
                batch: { maxRecords: 10, maxContentBytes: 1000 }
            """;

        var set = ProfileLoader.Load(yaml);

        Assert.Equal(2, set.Profiles.Count);
        Assert.Equal("a", set.Profiles[0].Name);
        Assert.Equal("b", set.Profiles[1].Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Load_BlankYaml_Throws(string? yaml) =>
        Assert.ThrowsAny<ArgumentException>(() => ProfileLoader.Load(yaml!));

    [Fact]
    public void Load_NoProfiles_Throws() =>
        Assert.Throws<FormatException>(() => ProfileLoader.Load("profiles: []"));

    [Fact]
    public void Load_MalformedYaml_Throws() =>
        Assert.Throws<FormatException>(() => ProfileLoader.Load("profiles: {oops"));

    [Fact]
    public void Load_UnknownFormat_Throws()
    {
        var ex = Assert.Throws<FormatException>(
            () => ProfileLoader.Load(ValidYaml.Replace("fixed-length", "punched-card", StringComparison.Ordinal)));

        // The message lists what is registered, so a typo is diagnosable from the failure alone.
        Assert.Contains("fixed-length", ex.Message, StringComparison.Ordinal);
        Assert.Contains("delimited", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_DelimitedFormat_IsAccepted()
    {
        var set = ProfileLoader.Load(ValidYaml.Replace("fixed-length", "delimited", StringComparison.Ordinal));

        Assert.Equal("delimited", set.Profiles[0].Format.Token);
    }

    [Fact]
    public void Load_UnknownCompletionMode_Throws() =>
        Assert.Throws<FormatException>(() => ProfileLoader.Load(ValidYaml.Replace("stable-size", "magic", StringComparison.Ordinal)));

    [Fact]
    public void Load_BlankRequiredField_Throws() =>
        Assert.Throws<FormatException>(() => ProfileLoader.Load(ValidYaml.Replace("destination: batches-dest", "destination: \"\"", StringComparison.Ordinal)));

    [Fact]
    public void Load_NonPositiveBatchLimit_Throws() =>
        Assert.Throws<FormatException>(() => ProfileLoader.Load(ValidYaml.Replace("maxRecords: 500", "maxRecords: 0", StringComparison.Ordinal)));

    [Fact]
    public void Load_NonPositiveQuietSeconds_Throws() =>
        Assert.Throws<FormatException>(() => ProfileLoader.Load(ValidYaml.Replace("quietSeconds: 5", "quietSeconds: 0", StringComparison.Ordinal)));

    [Fact]
    public void Load_SameDirectoryForTwoRoles_Throws() =>
        Assert.Throws<FormatException>(() => ProfileLoader.Load(ValidYaml.Replace("processing: /data/feed/processing", "processing: /data/feed/incoming", StringComparison.Ordinal)));

    [Fact]
    public void Load_MissingCompletion_Throws()
    {
        const string yaml = """
            profiles:
              - name: feed-a
                incoming: /in
                processing: /proc
                done: /done
                failed: /failed
                layout: /cfg.yaml
                format: fixed-length
                destination: d
                rejectDestination: r
                batch: { maxRecords: 1, maxContentBytes: 1 }
            """;

        Assert.Throws<FormatException>(() => ProfileLoader.Load(yaml));
    }

    [Fact]
    public void Load_MissingBatch_Throws()
    {
        const string yaml = """
            profiles:
              - name: feed-a
                incoming: /in
                processing: /proc
                done: /done
                failed: /failed
                layout: /cfg.yaml
                format: fixed-length
                completion: { mode: stable-size, quietSeconds: 1, pollSeconds: 1 }
                destination: d
                rejectDestination: r
            """;

        Assert.Throws<FormatException>(() => ProfileLoader.Load(yaml));
    }

    [Fact]
    public void Load_DuplicateName_Throws() =>
        Assert.Throws<FormatException>(() => ProfileLoader.Load(TwoProfiles(nameB: "a", incomingB: "/in/b")));

    [Fact]
    public void Load_DuplicateIncoming_Throws() =>
        Assert.Throws<FormatException>(() => ProfileLoader.Load(TwoProfiles(nameB: "b", incomingB: "/in/a")));

    [Fact]
    public void LoadFromFile_BlankPath_Throws() =>
        Assert.ThrowsAny<ArgumentException>(() => ProfileLoader.LoadFromFile("   "));

    private static string TwoProfiles(string nameB, string incomingB) => $$"""
        profiles:
          - name: a
            incoming: /in/a
            processing: /proc/a
            done: /done/a
            failed: /failed/a
            layout: /cfg/a.yaml
            format: fixed-length
            completion: { mode: stable-size, quietSeconds: 1, pollSeconds: 1 }
            destination: a-batches
            rejectDestination: a-rejects
            batch: { maxRecords: 1, maxContentBytes: 1 }
          - name: {{nameB}}
            incoming: {{incomingB}}
            processing: /proc/b
            done: /done/b
            failed: /failed/b
            layout: /cfg/b.yaml
            format: fixed-length
            completion: { mode: stable-size, quietSeconds: 1, pollSeconds: 1 }
            destination: b-batches
            rejectDestination: b-rejects
            batch: { maxRecords: 1, maxContentBytes: 1 }
        """;
}
