using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Common.FileIngestion.Layouts;

/// <summary>
/// Loads a <see cref="Layout"/> from soft-coded YAML. Generic across formats — the model it
/// produces holds whatever the YAML declares. Fail-closed: malformed YAML, unknown field types,
/// or any structural invariant violation is rejected.
/// </summary>
public static class LayoutLoader
{
    /// <summary>Loads and validates a layout from a YAML string.</summary>
    /// <param name="yaml">The layout YAML; required, non-blank.</param>
    /// <exception cref="ArgumentException"><paramref name="yaml"/> is null, empty, or whitespace.</exception>
    /// <exception cref="FormatException">The YAML is malformed or violates a layout invariant.</exception>
    public static Layout Load(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        LayoutDto? dto;
        try
        {
            dto = deserializer.Deserialize<LayoutDto>(yaml);
        }
        catch (YamlException ex)
        {
            throw new FormatException("Invalid layout YAML.", ex);
        }

        if (dto is null)
        {
            throw new FormatException("Layout YAML is empty.");
        }

        if (dto.Discriminator is null)
        {
            throw new FormatException("Layout must define a discriminator.");
        }

        if (dto.RecordTypes is null || dto.RecordTypes.Count == 0)
        {
            throw new FormatException("Layout must define at least one record type.");
        }

        var recordTypes = new List<RecordDefinition>(dto.RecordTypes.Count);
        foreach (var pair in dto.RecordTypes)
        {
            recordTypes.Add(MapRecord(pair.Key, pair.Value));
        }

        try
        {
            return new Layout(
                dto.Version ?? string.Empty,
                dto.RecordLength,
                dto.Encoding ?? string.Empty,
                dto.Terminator,
                dto.Discriminator.Start,
                dto.Discriminator.Length,
                recordTypes);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"Invalid layout: {ex.Message}", ex);
        }
    }

    /// <summary>Loads and validates a layout from a YAML file.</summary>
    /// <param name="path">Path to the layout YAML file; required, non-blank.</param>
    public static Layout LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Load(File.ReadAllText(path));
    }

    private static RecordDefinition MapRecord(string name, RecordTypeDto? dto)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new FormatException("Record type names must be non-blank.");
        }

        if (dto is null)
        {
            throw new FormatException($"Record type '{name}' is empty.");
        }

        if (string.IsNullOrWhiteSpace(dto.Match))
        {
            throw new FormatException($"Record type '{name}' must define a match value.");
        }

        // A skipped record (header/trailer) is consumed for framing but never sliced, so it may omit fields.
        if (!dto.Skip && (dto.Fields is null || dto.Fields.Count == 0))
        {
            throw new FormatException($"Record type '{name}' must define fields.");
        }

        List<FieldDto> source = dto.Fields ?? [];
        var fields = new List<FieldDefinition>(source.Count);
        foreach (var field in source)
        {
            if (field is null || string.IsNullOrWhiteSpace(field.Name))
            {
                throw new FormatException($"Record type '{name}' has a field without a name.");
            }

            try
            {
                fields.Add(new FieldDefinition(field.Name, field.Start, field.Length, field.Encrypt, field.Required, field.Skip));
            }
            catch (ArgumentException ex)
            {
                throw new FormatException($"Record type '{name}', field '{field.Name}': {ex.Message}", ex);
            }
        }

        try
        {
            return new RecordDefinition(name, dto.Match, fields, dto.Skip);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"Record type '{name}': {ex.Message}", ex);
        }
    }

#pragma warning disable S1144, S3459, CA1812 // DTOs are populated by YamlDotNet via reflection.
    private sealed class LayoutDto
    {
        public string? Version { get; set; }

        public int RecordLength { get; set; }

        public string? Encoding { get; set; }

        // Absent in the YAML means 0 — a fixed-width layout with no record terminator.
        public int Terminator { get; set; }

        public DiscriminatorDto? Discriminator { get; set; }

        public Dictionary<string, RecordTypeDto>? RecordTypes { get; set; }
    }

    private sealed class DiscriminatorDto
    {
        public int Start { get; set; }

        public int Length { get; set; }
    }

    private sealed class RecordTypeDto
    {
        public string? Match { get; set; }

        public bool Skip { get; set; }

        public List<FieldDto>? Fields { get; set; }
    }

    private sealed class FieldDto
    {
        public string? Name { get; set; }

        public int Start { get; set; }

        public int Length { get; set; }

        public bool Encrypt { get; set; }

        public bool Required { get; set; }

        public bool Skip { get; set; }
    }
#pragma warning restore S1144, S3459, CA1812
}
