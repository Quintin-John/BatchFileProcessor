using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ingestion.Worker.Profiles;

/// <summary>
/// Loads the operational routing profiles from soft-coded YAML (folders → layouts/format/completion/
/// destinations). Generic and fail-closed: malformed YAML, an unknown format/completion token, or any
/// invariant violation is rejected. Parsing/mapping of records is not here — that stays in each profile's
/// own layout; this only routes.
/// </summary>
internal static class ProfileLoader
{
    // Format tokens come from the format registry, so this loader has no second list to keep in step with it.
    private static readonly Dictionary<string, CompletionMode> CompletionTokens =
        new(StringComparer.OrdinalIgnoreCase) { ["stable-size"] = CompletionMode.StableSize };

    /// <summary>Loads and validates the profile set from a YAML string.</summary>
    /// <param name="yaml">The profiles YAML; required, non-blank.</param>
    /// <exception cref="ArgumentException"><paramref name="yaml"/> is null, empty, or whitespace.</exception>
    /// <exception cref="FormatException">The YAML is malformed or violates a profile invariant.</exception>
    public static ProfileSet Load(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        RootDto? dto;
        try
        {
            dto = deserializer.Deserialize<RootDto>(yaml);
        }
        catch (YamlException ex)
        {
            throw new FormatException("Invalid profiles YAML.", ex);
        }

        if (dto?.Profiles is null || dto.Profiles.Count == 0)
        {
            throw new FormatException("At least one profile must be defined.");
        }

        var profiles = new List<Profile>(dto.Profiles.Count);
        foreach (var profile in dto.Profiles)
        {
            profiles.Add(MapProfile(profile));
        }

        try
        {
            return new ProfileSet(profiles);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"Invalid profiles: {ex.Message}", ex);
        }
    }

    /// <summary>Loads and validates the profile set from a YAML file.</summary>
    /// <param name="path">Path to the profiles YAML file; required, non-blank.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty, or whitespace.</exception>
    public static ProfileSet LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Load(File.ReadAllText(path));
    }

    private static Profile MapProfile(ProfileDto? dto)
    {
        if (dto is null)
        {
            throw new FormatException("A profile entry is empty.");
        }

        var name = dto.Name ?? string.Empty;

        var format = dto.Format is null ? null : RecordFormats.Resolve(dto.Format);
        if (format is null)
        {
            throw new FormatException(
                $"Profile '{name}': unknown format '{dto.Format}'; expected one of: {string.Join(", ", RecordFormats.Tokens)}.");
        }

        var completion = MapCompletion(name, dto.Completion);

        if (dto.Batch is null)
        {
            throw new FormatException($"Profile '{name}': batch limits are required.");
        }

        try
        {
            var folders = new ProfileFolders(
                dto.Incoming ?? string.Empty,
                dto.Processing ?? string.Empty,
                dto.Done ?? string.Empty,
                dto.Failed ?? string.Empty);

            var routing = new RoutingTargets(dto.Destination ?? string.Empty, dto.RejectDestination ?? string.Empty);
            var batch = new BatchLimits(dto.Batch.MaxRecords, dto.Batch.MaxContentBytes);

            return new Profile(name, folders, MapLayouts(name, dto), format, completion, routing, batch);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"Profile '{name}': {ex.Message}", ex);
        }
    }

    // A profile names the layouts a file in its folder might match. One folder can receive more than one
    // version of a format, so 'layout' declares the single case and 'layouts' the several — each shape has
    // exactly one spelling, so a profile cannot be read two ways.
    private static List<string> MapLayouts(string name, ProfileDto dto)
    {
        if (dto.Layout is not null && dto.Layouts is not null)
        {
            throw new FormatException(
                $"Profile '{name}': declares both 'layout' and 'layouts'; use 'layout' for one or 'layouts' for several.");
        }

        if (dto.Layout is not null)
        {
            return [dto.Layout];
        }

        if (dto.Layouts is null)
        {
            throw new FormatException(
                $"Profile '{name}': a layout is required — 'layout' for one, 'layouts' for several.");
        }

        // A single-entry list would be a second way to say what 'layout' already says.
        if (dto.Layouts.Count < 2)
        {
            throw new FormatException(
                $"Profile '{name}': 'layouts' declares {dto.Layouts.Count}; use 'layout' when there is only one.");
        }

        return dto.Layouts;
    }

    private static CompletionSettings MapCompletion(string profileName, CompletionDto? dto)
    {
        if (dto is null)
        {
            throw new FormatException($"Profile '{profileName}': completion settings are required.");
        }

        if (dto.Mode is null || !CompletionTokens.TryGetValue(dto.Mode, out var mode))
        {
            throw new FormatException($"Profile '{profileName}': unknown completion mode '{dto.Mode}'.");
        }

        try
        {
            return new CompletionSettings(
                mode, TimeSpan.FromSeconds(dto.QuietSeconds), TimeSpan.FromSeconds(dto.PollSeconds));
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"Profile '{profileName}': {ex.Message}", ex);
        }
    }

#pragma warning disable S1144, S3459, CA1812 // DTOs are populated by YamlDotNet via reflection.
    private sealed class RootDto
    {
        public List<ProfileDto>? Profiles { get; set; }
    }

    private sealed class ProfileDto
    {
        public string? Name { get; set; }

        public string? Incoming { get; set; }

        public string? Processing { get; set; }

        public string? Done { get; set; }

        public string? Failed { get; set; }

        public string? Layout { get; set; }

        public List<string>? Layouts { get; set; }

        public string? Format { get; set; }

        public CompletionDto? Completion { get; set; }

        public string? Destination { get; set; }

        public string? RejectDestination { get; set; }

        public BatchDto? Batch { get; set; }
    }

    private sealed class CompletionDto
    {
        public string? Mode { get; set; }

        public int QuietSeconds { get; set; }

        public int PollSeconds { get; set; }
    }

    private sealed class BatchDto
    {
        public int MaxRecords { get; set; }

        public int MaxContentBytes { get; set; }
    }
#pragma warning restore S1144, S3459, CA1812
}
