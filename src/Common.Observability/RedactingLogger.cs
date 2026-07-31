using Microsoft.Extensions.Logging;

namespace Common.Observability;

/// <summary>
/// An <see cref="ILogger"/> decorator that redacts sensitive structured values before they reach any sink.
/// For each log entry and log scope whose state is a key/value list, any value whose key is in the configured
/// sensitive-key set is replaced with <see cref="RedactionMarker"/> — in both the structured state and the
/// re-rendered message text — so a field the layout marks <c>encrypt</c> can never appear in clear in the
/// logs. State that is not a key/value list, or that contains no sensitive key, passes through untouched.
/// A value interpolated into the message string before logging carries no key and cannot be matched here;
/// that path is covered separately by the field value's own redacted rendering.
/// </summary>
internal sealed class RedactingLogger : ILogger
{
    /// <summary>Replacement token substituted for a sensitive value.</summary>
    internal const string RedactionMarker = "[REDACTED]";

    private const string OriginalFormatKey = "{OriginalFormat}";

    private readonly ILogger _logger;
    private readonly IReadOnlySet<string> _sensitiveKeys;

    /// <summary>Creates the decorator over an inner logger.</summary>
    /// <param name="inner">The logger to delegate to; required.</param>
    /// <param name="sensitiveKeys">Keys whose values must be redacted; required (may be empty).</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public RedactingLogger(ILogger inner, IReadOnlySet<string> sensitiveKeys)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(sensitiveKeys);
        _logger = inner;
        _sensitiveKeys = sensitiveKeys;
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull =>
        state is IReadOnlyList<KeyValuePair<string, object?>> kvps && ContainsSensitive(kvps)
            ? _logger.BeginScope(Scrub(kvps))
            : _logger.BeginScope(state);

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => _logger.IsEnabled(logLevel);

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (state is IReadOnlyList<KeyValuePair<string, object?>> kvps && ContainsSensitive(kvps))
        {
            _logger.Log(logLevel, eventId, Scrub(kvps), exception, static (scrubbed, _) => Render(scrubbed));
            return;
        }

        _logger.Log(logLevel, eventId, state, exception, formatter);
    }

    private bool ContainsSensitive(IReadOnlyList<KeyValuePair<string, object?>> kvps)
    {
        for (var i = 0; i < kvps.Count; i++)
        {
            if (_sensitiveKeys.Contains(kvps[i].Key))
            {
                return true;
            }
        }

        return false;
    }

    private List<KeyValuePair<string, object?>> Scrub(IReadOnlyList<KeyValuePair<string, object?>> kvps)
    {
        var scrubbed = new List<KeyValuePair<string, object?>>(kvps.Count);
        foreach (var kvp in kvps)
        {
            scrubbed.Add(_sensitiveKeys.Contains(kvp.Key)
                ? new KeyValuePair<string, object?>(kvp.Key, RedactionMarker)
                : kvp);
        }

        return scrubbed;
    }

    // Rebuilds the human-readable message from the scrubbed values so a redacted value never survives in the
    // rendered text. A placeholder that does not match a bare "{Key}" (e.g. one with a format specifier) is
    // left literal — fail-safe, since the scrubbed value is already the marker and no clear value can appear.
    private static string Render(IReadOnlyList<KeyValuePair<string, object?>> kvps)
    {
        var template = kvps
            .FirstOrDefault(kvp => string.Equals(kvp.Key, OriginalFormatKey, StringComparison.Ordinal))
            .Value?.ToString();

        if (template is null)
        {
            return string.Join(", ", kvps.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }

        var message = template;
        foreach (var kvp in kvps.Where(kvp => !string.Equals(kvp.Key, OriginalFormatKey, StringComparison.Ordinal)))
        {
            message = message.Replace("{" + kvp.Key + "}", kvp.Value?.ToString() ?? string.Empty, StringComparison.Ordinal);
        }

        return message;
    }
}
