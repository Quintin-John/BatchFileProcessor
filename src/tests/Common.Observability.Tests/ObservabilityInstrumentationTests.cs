using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Common.Observability.Tests;

public sealed class ObservabilityInstrumentationTests
{
    private static ObservabilityInstrumentation NewInstrumentation() =>
        new($"test-{Guid.NewGuid():N}", "1.0.0");

    private static ActivityListener ListenTo(string sourceName)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public void Constructor_SetsName()
    {
        using var instrumentation = new ObservabilityInstrumentation("svc");
        Assert.Equal("svc", instrumentation.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_BlankName_Throws(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ObservabilityInstrumentation(name!));
    }

    [Fact]
    public void StartActivity_WithinScope_TagsCorrelation()
    {
        using var instrumentation = NewInstrumentation();
        using var listener = ListenTo(instrumentation.Name);
        var run = RunContext.NewRun();

        using (CorrelationScope.Begin(run))
        using (var activity = instrumentation.StartActivity("work"))
        {
            Assert.NotNull(activity);
            Assert.Equal(run.RunId, activity!.GetTagItem(ObservabilityInstrumentation.RunIdTag));
            Assert.Equal(run.CorrelationId, activity.GetTagItem(ObservabilityInstrumentation.CorrelationIdTag));
        }
    }

    [Fact]
    public void StartActivity_WithoutScope_HasNoCorrelationTags()
    {
        using var instrumentation = NewInstrumentation();
        using var listener = ListenTo(instrumentation.Name);

        using var activity = instrumentation.StartActivity("work");

        Assert.NotNull(activity);
        Assert.Null(activity!.GetTagItem(ObservabilityInstrumentation.RunIdTag));
    }

    [Fact]
    public void StartActivity_BlankName_Throws()
    {
        using var instrumentation = NewInstrumentation();
        Assert.ThrowsAny<ArgumentException>(() => instrumentation.StartActivity(" "));
    }

    [Fact]
    public void CreateCounter_RecordsMeasurements()
    {
        using var instrumentation = NewInstrumentation();
        long total = 0;

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
        meterListener.SetMeasurementEventCallback<long>((_, measurement, _, _) => total += measurement);
        meterListener.Start();

        var counter = instrumentation.CreateCounter("records.consumed");
        counter.Add(3);
        counter.Add(4);

        Assert.Equal(7, total);
    }

    [Fact]
    public void CreateCounter_BlankName_Throws()
    {
        using var instrumentation = NewInstrumentation();
        Assert.ThrowsAny<ArgumentException>(() => instrumentation.CreateCounter(""));
    }

    [Fact]
    public void ExposesActivitySourceAndMeter_NamedAndVersioned()
    {
        using var instrumentation = new ObservabilityInstrumentation("svc", "2.0.0");

        Assert.Equal("svc", instrumentation.ActivitySource.Name);
        Assert.Equal("svc", instrumentation.Meter.Name);
        Assert.Equal("2.0.0", instrumentation.Meter.Version);
    }
}
