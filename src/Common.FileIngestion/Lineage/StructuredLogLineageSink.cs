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

        // Pass a cheap wrapper, not a pre-serialized string: the JSON is produced by the wrapper's
        // ToString(), which the source-generated LogLineage calls only after its own IsEnabled gate.
        // So serialization never runs when the sink category is disabled (CA1873), with no manual guard.
        LogLineage(new LineageLogValue(lineageEvent));

        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "lineage {Lineage}")]
    private partial void LogLineage(LineageLogValue lineage);

    /// <summary>
    /// Deferred JSON rendering of a lineage event. The serialize runs in <see cref="ToString"/>, which the
    /// logging pipeline invokes only when the message is actually emitted — never on the disabled path.
    /// </summary>
    private readonly record struct LineageLogValue(LineageEvent Event)
    {
        public override string ToString() => JsonSerializer.Serialize(Event, JsonOptions);
    }
}
