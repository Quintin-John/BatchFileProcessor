using System.Diagnostics.CodeAnalysis;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Common.Security.DataProtection;

/// <summary>
/// Loads a <see cref="DataProtectionPolicy"/> from soft-coded YAML. Fail-closed: unknown actions,
/// missing actions, or a policy with no fields are rejected.
/// </summary>
public static class DataProtectionPolicyLoader
{
    /// <summary>Loads and validates a policy from a YAML string.</summary>
    /// <param name="yaml">The policy YAML; required, non-blank.</param>
    /// <exception cref="ArgumentException"><paramref name="yaml"/> is null, empty, or whitespace.</exception>
    /// <exception cref="FormatException">The YAML is malformed or a field's action is missing/unknown.</exception>
    public static DataProtectionPolicy Load(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        PolicyDto? dto;
        try
        {
            dto = deserializer.Deserialize<PolicyDto>(yaml);
        }
        catch (YamlException ex)
        {
            throw new FormatException("Invalid data-protection policy YAML.", ex);
        }

        if (dto?.Fields is null || dto.Fields.Count == 0)
        {
            throw new FormatException("Data-protection policy must define at least one field.");
        }

        var fields = new Dictionary<string, FieldProtection>(StringComparer.Ordinal);
        foreach (var pair in dto.Fields)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                throw new FormatException("Field names must be non-blank.");
            }

            var entry = pair.Value;
            if (entry is null || string.IsNullOrWhiteSpace(entry.Action))
            {
                throw new FormatException($"Field '{pair.Key}' must specify an action.");
            }

            if (!Enum.TryParse<ProtectionAction>(entry.Action, ignoreCase: true, out var action)
                || !Enum.IsDefined(action))
            {
                throw new FormatException($"Field '{pair.Key}' has unknown action '{entry.Action}'.");
            }

            var mask = string.IsNullOrWhiteSpace(entry.Mask) ? null : entry.Mask;
            fields[pair.Key] = new FieldProtection(action, mask, entry.RedactInLogs);
        }

        return new DataProtectionPolicy(fields);
    }

    /// <summary>Loads and validates a policy from a YAML file.</summary>
    /// <param name="path">Path to the policy YAML file; required, non-blank.</param>
    public static DataProtectionPolicy LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Load(File.ReadAllText(path));
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instantiated by YamlDotNet via reflection.")]
    private sealed class PolicyDto
    {
        public Dictionary<string, FieldDto>? Fields { get; set; }
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instantiated by YamlDotNet via reflection.")]
    private sealed class FieldDto
    {
        public string? Action { get; set; }

        public string? Mask { get; set; }

        public bool RedactInLogs { get; set; }
    }
}
