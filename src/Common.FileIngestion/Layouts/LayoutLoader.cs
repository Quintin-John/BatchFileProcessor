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
    // Layout type vocabulary (as written in the YAML) mapped to the model's field types.
    private static readonly Dictionary<string, FieldType> FieldTypeTokens =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["string"] = FieldType.Text,
            ["decimal"] = FieldType.Number,
            ["date"] = FieldType.Date,
            ["time"] = FieldType.Time,
            ["filler"] = FieldType.Filler,
        };

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

        if (dto?.Fields is null || dto.Fields.Count == 0)
        {
            throw new FormatException($"Record type '{name}' must define fields.");
        }

        if (string.IsNullOrWhiteSpace(dto.Match))
        {
            throw new FormatException($"Record type '{name}' must define a match value.");
        }

        var fields = new List<FieldDefinition>(dto.Fields.Count);
        foreach (var field in dto.Fields)
        {
            if (field is null || string.IsNullOrWhiteSpace(field.Name))
            {
                throw new FormatException($"Record type '{name}' has a field without a name.");
            }

            if (field.Type is null || !FieldTypeTokens.TryGetValue(field.Type, out var type))
            {
                throw new FormatException($"Record type '{name}', field '{field.Name}': unknown type '{field.Type}'.");
            }

            try
            {
                fields.Add(new FieldDefinition(field.Name, field.Start, field.Length, type));
            }
            catch (ArgumentException ex)
            {
                throw new FormatException($"Record type '{name}', field '{field.Name}': {ex.Message}", ex);
            }
        }

        try
        {
            return new RecordDefinition(name, dto.Match, fields);
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

        public List<FieldDto>? Fields { get; set; }
    }

    private sealed class FieldDto
    {
        public string? Name { get; set; }

        public int Start { get; set; }

        public int Length { get; set; }

        public string? Type { get; set; }
    }
#pragma warning restore S1144, S3459, CA1812
}
