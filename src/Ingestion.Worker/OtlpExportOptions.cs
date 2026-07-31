namespace Ingestion.Worker;

/// <summary>
/// Host OTLP telemetry-export configuration. OTLP is downstream-agnostic — an OpenTelemetry Collector (or any
/// OTLP-capable backend) receives it and fans out to Datadog / Prometheus / Grafana / etc. Bound from the
/// <c>Otlp</c> configuration section; when no endpoint is set, telemetry is collected but not exported.
/// </summary>
public sealed class OtlpExportOptions
{
    /// <summary>OTLP endpoint as an absolute URI (e.g. <c>http://collector:4317</c>). Optional; empty disables export.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Returns the validated absolute endpoint URI, or null when no endpoint is configured.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Endpoint"/> is set but is not an absolute URI (fail-closed).</exception>
    public Uri? ResolveEndpoint()
    {
        if (string.IsNullOrWhiteSpace(Endpoint))
        {
            return null;
        }

        return Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException($"OTLP endpoint '{Endpoint}' is not an absolute URI.");
    }
}
