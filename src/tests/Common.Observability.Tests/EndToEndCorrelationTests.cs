using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Common.Observability.Tests;

public sealed class EndToEndCorrelationTests
{
    [Fact]
    public void RunScope_PropagatesCorrelation_AcrossSpanLogAndMetric()
    {
        using var instrumentation = new ObservabilityInstrumentation($"svc-{Guid.NewGuid():N}");

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == instrumentation.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(activityListener);

        long metricTotal = 0;
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == instrumentation.Name)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        meterListener.SetMeasurementEventCallback<long>((_, measurement, _, _) => metricTotal += measurement);
        meterListener.Start();

        var logger = new CapturingLogger();
        var run = RunContext.NewRun();

        using (CorrelationScope.Begin(run))
        using (var activity = instrumentation.StartActivity("ingest.run"))
        using (logger.BeginCorrelationScope())
        {
            instrumentation.CreateCounter("records.consumed").Add(5);

            // Span carries the correlation.
            Assert.Equal(run.RunId, activity!.GetTagItem(ObservabilityInstrumentation.RunIdTag));
            Assert.Equal(run.CorrelationId, activity.GetTagItem(ObservabilityInstrumentation.CorrelationIdTag));

            // Log scope carries the correlation and the same trace id as the span.
            Assert.Equal(run.RunId, logger.LastScopeState![ObservabilityInstrumentation.RunIdTag]);
            Assert.Equal(run.CorrelationId, logger.LastScopeState[ObservabilityInstrumentation.CorrelationIdTag]);
            Assert.Equal(activity.TraceId.ToString(), logger.LastScopeState[CorrelationLoggerExtensions.TraceIdField]);
        }

        Assert.Equal(5, metricTotal);
        Assert.Null(CorrelationScope.Current);
    }

    private sealed class CapturingLogger : ILogger
    {
        public IReadOnlyDictionary<string, object>? LastScopeState { get; private set; }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            LastScopeState = state as IReadOnlyDictionary<string, object>;
            return new NoopScope();
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }

        private sealed class NoopScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
