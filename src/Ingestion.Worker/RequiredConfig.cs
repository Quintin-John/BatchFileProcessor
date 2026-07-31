using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Ingestion.Worker;

/// <summary>
/// Fail-closed configuration accessors for the composition root. Unlike
/// <see cref="ConfigurationBinder.GetValue{T}(IConfiguration, string)"/>, which silently coerces a
/// missing key to <c>default(T)</c> (0, first enum member, ...), these throw so a missing or malformed
/// setting stops startup rather than running on a silent default.
/// </summary>
internal static class RequiredConfig
{
    /// <summary>Returns a required, non-blank string.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="section"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The key is missing or blank.</exception>
    public static string Text(IConfigurationSection section, string key)
    {
        ArgumentNullException.ThrowIfNull(section);
        var raw = section[key];
        return string.IsNullOrWhiteSpace(raw) ? throw Missing(section, key) : raw;
    }

    /// <summary>Returns a required integer.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="section"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The key is missing or not an integer.</exception>
    public static int Integer(IConfigurationSection section, string key)
    {
        ArgumentNullException.ThrowIfNull(section);
        var raw = section[key] ?? throw Missing(section, key);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Invalid(section, key, raw, "an integer");
    }

    /// <summary>Returns a required, defined enum value (case-insensitive).</summary>
    /// <exception cref="ArgumentNullException"><paramref name="section"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The key is missing or not a defined member.</exception>
    public static TEnum Enum<TEnum>(IConfigurationSection section, string key) where TEnum : struct, System.Enum
    {
        ArgumentNullException.ThrowIfNull(section);
        var raw = section[key] ?? throw Missing(section, key);
        return System.Enum.TryParse<TEnum>(raw, ignoreCase: true, out var value) && System.Enum.IsDefined(value)
            ? value
            : throw Invalid(section, key, raw, "one of [" + string.Join(", ", System.Enum.GetNames<TEnum>()) + "]");
    }

    private static InvalidOperationException Missing(IConfigurationSection section, string key) =>
        new($"Missing required configuration '{section.Key}:{key}'.");

    private static InvalidOperationException Invalid(IConfigurationSection section, string key, string raw, string expected) =>
        new($"Configuration '{section.Key}:{key}' must be {expected}; got '{raw}'.");
}
