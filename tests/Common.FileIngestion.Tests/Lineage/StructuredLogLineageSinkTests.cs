using Common.FileIngestion.Lineage;
using Common.Messaging.Contracts;
using Microsoft.Extensions.Logging;

namespace Common.FileIngestion.Tests.Lineage;

public sealed class StructuredLogLineageSinkTests
{
    private static readonly DateTimeOffset When = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExportAsync_WritesStructuredJson_WithStateAndReasonCode()
    {
        var logger = new CapturingLogger<StructuredLogLineageSink>();
        var sink = new StructuredLogLineageSink(logger);
        var lineageEvent = new LineageEvent(
            "run-1", "FILE1", new RecordLocator(7, 70, "AUTH"), LineageState.Rejected, When, reasonCode: "NON_NUMERIC");

        await sink.ExportAsync(lineageEvent, CancellationToken.None);

        var message = Assert.Single(logger.Messages);
        Assert.Contains("\"State\":\"Rejected\"", message, StringComparison.Ordinal);
        Assert.Contains("\"ReasonCode\":\"NON_NUMERIC\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_OmitsNullOptionalFields()
    {
        var logger = new CapturingLogger<StructuredLogLineageSink>();
        var sink = new StructuredLogLineageSink(logger);
        var lineageEvent = new LineageEvent("run-1", "FILE1", new RecordLocator(1, 0, "TRAN"), LineageState.Consumed, When);

        await sink.ExportAsync(lineageEvent, CancellationToken.None);

        Assert.DoesNotContain("ReasonCode", Assert.Single(logger.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_NullLogger_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new StructuredLogLineageSink(null!));

    [Fact]
    public async Task ExportAsync_NullEvent_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new StructuredLogLineageSink(new CapturingLogger<StructuredLogLineageSink>())
                .ExportAsync(null!, CancellationToken.None));

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
