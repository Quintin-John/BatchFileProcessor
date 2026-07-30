using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Common.Security.DataProtection;

/// <summary>
/// Loads a <see cref="DataProtectionPolicy"/> from soft-coded YAML. Fail-closed: unknown actions,
/// missing actions, invalid flags, or a policy with no fields are rejected.
/// </summary>
public static class DataProtectionPolicyLoader
{
    private const string FieldsKey = "fields";
    private const string ActionKey = "action";
    private const string MaskKey = "mask";
    private const string RedactKey = "redactInLogs";

    /// <summary>Loads and validates a policy from a YAML string.</summary>
    /// <param name="yaml">The policy YAML; required, non-blank.</param>
    /// <exception cref="ArgumentException"><paramref name="yaml"/> is null, empty, or whitespace.</exception>
    /// <exception cref="FormatException">The YAML is malformed or a field's action/flags are missing or invalid.</exception>
    public static DataProtectionPolicy Load(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var deserializer = new DeserializerBuilder().Build();

        Dictionary<string, Dictionary<string, Dictionary<string, string>>>? root;
        try
        {
            root = deserializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(yaml);
        }
        catch (YamlException ex)
        {
            throw new FormatException("Invalid data-protection policy YAML.", ex);
        }

        if (root is null
            || !root.TryGetValue(FieldsKey, out var fieldNodes)
            || fieldNodes is null
            || fieldNodes.Count == 0)
        {
            throw new FormatException("Data-protection policy must define at least one field.");
        }

        var fields = new Dictionary<string, FieldProtection>(StringComparer.Ordinal);
        foreach (var (name, props) in fieldNodes)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new FormatException("Field names must be non-blank.");
            }

            fields[name] = ParseField(name, props);
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

    private static FieldProtection ParseField(string name, Dictionary<string, string>? props)
    {
        if (props is null
            || !props.TryGetValue(ActionKey, out var actionText)
            || string.IsNullOrWhiteSpace(actionText))
        {
            throw new FormatException($"Field '{name}' must specify an action.");
        }

        if (!Enum.TryParse<ProtectionAction>(actionText, ignoreCase: true, out var action) || !Enum.IsDefined(action))
        {
            throw new FormatException($"Field '{name}' has unknown action '{actionText}'.");
        }

        props.TryGetValue(MaskKey, out var maskText);
        var mask = string.IsNullOrWhiteSpace(maskText) ? null : maskText;

        var redact = false;
        if (props.TryGetValue(RedactKey, out var redactText)
            && !string.IsNullOrWhiteSpace(redactText)
            && !bool.TryParse(redactText, out redact))
        {
            throw new FormatException($"Field '{name}' has invalid {RedactKey} '{redactText}'.");
        }

        return new FieldProtection(action, mask, redact);
    }
}
