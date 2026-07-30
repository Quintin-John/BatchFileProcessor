using Common.FileIngestion.Profiles;

namespace Common.FileIngestion.Tests.Profiles;

public sealed class ProfileResolverTests
{
    private static Profile Profile(string id, string match) =>
        new(id, match, IngestionFormat.FixedLength, "layout.yaml", "queue");

    [Fact]
    public void Resolve_FirstMatchingProfile_Wins()
    {
        var resolver = new ProfileResolver(new[]
        {
            Profile("g266", "**/g266*"),
            Profile("csv", "**/*.csv"),
        });

        Assert.Equal("g266", resolver.Resolve("/data/in/g266_T1_20221107")!.Id);
        Assert.Equal("csv", resolver.Resolve("/data/in/report.csv")!.Id);
    }

    [Fact]
    public void Resolve_OrderMatters_FirstWins()
    {
        var resolver = new ProfileResolver(new[]
        {
            Profile("all", "**/*"),
            Profile("g266", "**/g266*"),
        });

        Assert.Equal("all", resolver.Resolve("/x/g266file")!.Id); // 'all' listed first
    }

    [Fact]
    public void Resolve_NormalisesBackslashes()
    {
        var resolver = new ProfileResolver(new[] { Profile("g266", "**/g266*") });

        Assert.NotNull(resolver.Resolve(@"C:\data\in\g266_file"));
    }

    [Fact]
    public void Resolve_SingleStar_DoesNotCrossSeparators()
    {
        var resolver = new ProfileResolver(new[] { Profile("p", "/in/*") });

        Assert.NotNull(resolver.Resolve("/in/file"));
        Assert.Null(resolver.Resolve("/in/sub/file")); // '*' must not cross '/'
    }

    [Fact]
    public void Resolve_NoMatch_ReturnsNull()
    {
        var resolver = new ProfileResolver(new[] { Profile("g266", "**/g266*") });

        Assert.Null(resolver.Resolve("/data/in/other.dat"));
    }

    [Fact]
    public void Resolve_BlankPath_Throws()
    {
        var resolver = new ProfileResolver(new[] { Profile("g266", "**/g266*") });

        Assert.ThrowsAny<ArgumentException>(() => resolver.Resolve("  "));
    }

    [Fact]
    public void Constructor_NullProfiles_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new ProfileResolver(null!));

    [Fact]
    public void Constructor_EmptyProfiles_Throws() =>
        Assert.Throws<ArgumentException>(() => new ProfileResolver(Array.Empty<Profile>()));

    [Fact]
    public void Constructor_NullProfileElement_Throws() =>
        Assert.Throws<ArgumentException>(() => new ProfileResolver(new Profile[] { null! }));

    [Fact]
    public void Profile_BlankArguments_Throw() =>
        Assert.ThrowsAny<ArgumentException>(() => new Profile("", "m", IngestionFormat.FixedLength, "l", "d"));

    [Fact]
    public void Profile_UndefinedFormat_Throws() =>
        Assert.Throws<InvalidOperationException>(
            () => new Profile("id", "m", (IngestionFormat)99, "l", "d"));
}
