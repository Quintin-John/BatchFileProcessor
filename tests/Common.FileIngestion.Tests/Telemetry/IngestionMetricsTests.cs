using System.Diagnostics.Metrics;
using Common.FileIngestion.Telemetry;
using Common.Observability;

namespace Common.FileIngestion.Tests.Telemetry;

public sealed class IngestionMetricsTests
{
    private sealed record Measurement(string Instrument, long Value, string? RecordType);

    // Records every long measurement emitted on the given meter, capturing the record.type tag.
    private static (IngestionMetrics Metrics, List<Measurement> Captured, IDisposable Scope) Arrange()
    {
        var instrumentation = new ObservabilityInstrumentation("test-ingest-metrics");
        var metrics = new IngestionMetrics(instrumentation);
        var captured = new List<Measurement>();

        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == instrumentation.Name)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            string? recordType = null;
            foreach (var tag in tags)
            {
                if (tag.Key == IngestionTelemetryTags.RecordType)
                {
                    recordType = tag.Value as string;
                }
            }

            captured.Add(new Measurement(instrument.Name, value, recordType));
        });
        listener.Start();

        return (metrics, captured, new CompositeDisposable(listener, instrumentation));
    }

    [Fact]
    public void Counters_EmitExpectedMeasurementsAndTags()
    {
        var (metrics, captured, scope) = Arrange();
        using (scope)
        {
            metrics.RecordParsed("TRAN");
            metrics.RecordRejected("AUTH");
            metrics.BatchPublished();
            metrics.BytesRead(1200);
        }

        Assert.Contains(captured, m => m.Instrument == IngestionMetrics.RecordsParsedName && m.Value == 1 && m.RecordType == "TRAN");
        Assert.Contains(captured, m => m.Instrument == IngestionMetrics.RecordsRejectedName && m.Value == 1 && m.RecordType == "AUTH");
        Assert.Contains(captured, m => m.Instrument == IngestionMetrics.BatchesPublishedName && m.Value == 1);
        Assert.Contains(captured, m => m.Instrument == IngestionMetrics.BytesReadName && m.Value == 1200);
    }

    [Fact]
    public void Constructor_NullInstrumentation_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new IngestionMetrics(null!));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RecordParsed_BlankType_Throws(string recordType)
    {
        var (metrics, _, scope) = Arrange();
        using (scope)
        {
            Assert.ThrowsAny<ArgumentException>(() => metrics.RecordParsed(recordType));
        }
    }

    [Fact]
    public void BytesRead_Negative_Throws()
    {
        var (metrics, _, scope) = Arrange();
        using (scope)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => metrics.BytesRead(-1));
        }
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable[] _disposables;

        public CompositeDisposable(params IDisposable[] disposables) => _disposables = disposables;

        public void Dispose()
        {
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }
        }
    }
}
