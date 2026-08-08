using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Common.FileIngestion.Layouts;

/// <summary>
/// Loads a <see cref="DelimitedLayout"/> from soft-coded YAML. Generic across delimited formats — the model
/// it produces holds whatever the YAML declares, so a new feed is a new layout file and no code change.
/// Fail-closed: malformed YAML, an unknown row role, an unresolvable delimiter, or any structural invariant
/// violation is rejected.
/// </summary>
public static class DelimitedLayoutLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>Loads and validates a layout from a YAML string.</summary>
    /// <param name="yaml">The layout YAML; required, non-blank.</param>
    /// <exception cref="ArgumentException"><paramref name="yaml"/> is null, empty, or whitespace.</exception>
    /// <exception cref="FormatException">The YAML is malformed or violates a layout invariant.</exception>
    public static DelimitedLayout Load(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        DelimitedLayoutDto? dto;
        try
        {
            dto = Deserializer.Deserialize<DelimitedLayoutDto>(yaml);
        }
        catch (YamlException ex)
        {
            throw new FormatException("Invalid delimited layout YAML.", ex);
        }

        if (dto is null)
        {
            throw new FormatException("Delimited layout YAML is empty.");
        }

        if (dto.RowTypes is null || dto.RowTypes.Count == 0)
        {
            throw new FormatException("Layout must define at least one row type.");
        }

        var delimiter = ResolveDelimiter(dto.Delimiter);

        var rowTypes = new List<DelimitedRowDefinition>(dto.RowTypes.Count);
        foreach (var pair in dto.RowTypes)
        {
            rowTypes.Add(MapRow(pair.Key, pair.Value));
        }

        try
        {
            return new DelimitedLayout(dto.Version ?? string.Empty, delimiter, dto.Encoding ?? string.Empty, rowTypes);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"Invalid delimited layout: {ex.Message}", ex);
        }
    }

    /// <summary>Loads and validates a layout from a YAML file.</summary>
    /// <param name="path">Path to the layout YAML file; required, non-blank.</param>
    public static DelimitedLayout LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Load(File.ReadAllText(path));
    }

    private static char ResolveDelimiter(string? token)
    {
        if (token is null)
        {
            throw new FormatException("Layout must declare a delimiter.");
        }

        try
        {
            return DelimiterToken.Resolve(token);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"Invalid delimiter: {ex.Message}", ex);
        }
    }

    private static DelimitedRowDefinition MapRow(string name, RowTypeDto? dto)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new FormatException("Row type names must be non-blank.");
        }

        if (dto is null)
        {
            throw new FormatException($"Row type '{name}' is empty.");
        }

        var role = ParseRole(name, dto.Role);

        // A skipped row type is consumed for framing but never sliced, so it may omit fields — the same
        // exemption a skipped fixed-width record type gets.
        if (!dto.Skip && (dto.Fields is null || dto.Fields.Count == 0))
        {
            throw new FormatException($"Row type '{name}' must define fields.");
        }

        List<FieldDto> source = dto.Fields ?? [];
        var fields = new List<DelimitedFieldDefinition>(source.Count);
        foreach (var field in source)
        {
            if (field is null || string.IsNullOrWhiteSpace(field.Name))
            {
                throw new FormatException($"Row type '{name}' has a field without a name.");
            }

            try
            {
                fields.Add(new DelimitedFieldDefinition(field.Name, field.Index, field.Encrypt, field.Required, field.Skip));
            }
            catch (ArgumentException ex)
            {
                throw new FormatException($"Row type '{name}', field '{field.Name}': {ex.Message}", ex);
            }
        }

        try
        {
            return new DelimitedRowDefinition(name, role, dto.Rows, fields, dto.Skip);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"Row type '{name}': {ex.Message}", ex);
        }
    }

    private static RowRole ParseRole(string name, string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new FormatException($"Row type '{name}' must declare a role.");
        }

        return Enum.TryParse<RowRole>(role, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new FormatException(
                $"Row type '{name}' has unknown role '{role}'; expected one of: {string.Join(", ", Enum.GetNames<RowRole>()).ToLowerInvariant()}.");
    }

#pragma warning disable S1144, S3459, CA1812 // DTOs are populated by YamlDotNet via reflection.
    private sealed class DelimitedLayoutDto
    {
        public string? Version { get; set; }

        public string? Delimiter { get; set; }

        public string? Encoding { get; set; }

        public Dictionary<string, RowTypeDto>? RowTypes { get; set; }
    }

    private sealed class RowTypeDto
    {
        public string? Role { get; set; }

        // Absent means 0: correct for a data row type, and rejected by the model for header/trailer.
        public int Rows { get; set; }

        public bool Skip { get; set; }

        public List<FieldDto>? Fields { get; set; }
    }

    private sealed class FieldDto
    {
        public string? Name { get; set; }

        public int Index { get; set; }

        public bool Encrypt { get; set; }

        public bool Required { get; set; }

        public bool Skip { get; set; }
    }
#pragma warning restore S1144, S3459, CA1812
}
