using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Common.FileIngestion.Lineage;

/// <summary>
/// Default <see cref="ILineageSink"/>: writes each lineage event as one structured JSON log line to
/// stdout (design §8 — the Datadog log pipeline scrapes stdout). No backend is a code dependency; an
/// OTLP sink can replace this behind the same seam. Enum states are written as names for readability.
/// </summary>
public sealed partial class StructuredLogLineageSink : ILineageSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ILogger<StructuredLogLineageSink> _logger;

    /// <summary>Creates the sink.</summary>
    /// <param name="logger">The structured logger; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is null.</exception>
    public StructuredLogLineageSink(ILogger<StructuredLogLineageSink> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task ExportAsync(LineageEvent lineageEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lineageEvent);

        // Serialize only when the sink category is actually logging (CA1873): the JSON is an expensive
        // argument evaluated at the call site, before the source-generated LogLineage checks IsEnabled.
        if (_logger.IsEnabled(LogLevel.Information))
        {
            LogLineage(JsonSerializer.Serialize(lineageEvent, JsonOptions));
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "lineage {Lineage}")]
    private partial void LogLineage(string lineage);
}
