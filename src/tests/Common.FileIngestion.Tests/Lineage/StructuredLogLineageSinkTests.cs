using Common.FileIngestion.Lineage;
using Common.Messaging.Contracts;
using Microsoft.Extensions.Logging;

namespace Common.FileIngestion.Tests.Lineage;

public sealed class StructuredLogLineageSinkTests
{
    // Bytes one fixture record occupies, terminator included; offsets in this fixture advance by it.
    private const int RecordExtent = 10;

    private static readonly DateTimeOffset When = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static LineageEvent Event(LineageState state, string? reasonCode = null) =>
        new("run-1", "FILE1", new RecordLocator(7, 70, RecordExtent, "AUTH"), state, When, reasonCode: reasonCode);

    [Fact]
    public async Task ExportAsync_Rejected_LogsWarning_WithStateAndReasonCode()
    {
        var logger = new CapturingLogger<StructuredLogLineageSink>();

        await new StructuredLogLineageSink(logger).ExportAsync(Event(LineageState.Rejected, "NON_NUMERIC"), CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("\"State\":\"Rejected\"", entry.Message, StringComparison.Ordinal);
        Assert.Contains("\"ReasonCode\":\"NON_NUMERIC\"", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_Failed_LogsError()
    {
        var logger = new CapturingLogger<StructuredLogLineageSink>();

        await new StructuredLogLineageSink(logger).ExportAsync(Event(LineageState.Failed, "PUBLISH_FAILED"), CancellationToken.None);

        Assert.Equal(LogLevel.Error, Assert.Single(logger.Entries).Level);
    }

    [Fact]
    public async Task ExportAsync_ProgressState_LogsDebug_AndOmitsNullOptionalFields()
    {
        var logger = new CapturingLogger<StructuredLogLineageSink>();

        await new StructuredLogLineageSink(logger).ExportAsync(Event(LineageState.Consumed), CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Debug, entry.Level); // per-record progress is Debug — off by default
        Assert.DoesNotContain("ReasonCode", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_LevelDisabled_DoesNotSerializeOrLog()
    {
        var logger = new CapturingLogger<StructuredLogLineageSink>(enabled: false);

        await new StructuredLogLineageSink(logger).ExportAsync(Event(LineageState.Consumed), CancellationToken.None);

        Assert.Empty(logger.Entries); // a disabled level skips the (unnecessary) serialize + log (CA1873)
    }

    [Fact]
    public void Constructor_NullLogger_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new StructuredLogLineageSink(null!));

    [Fact]
    public async Task ExportAsync_NullEvent_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new StructuredLogLineageSink(new CapturingLogger<StructuredLogLineageSink>())
                .ExportAsync(null!, CancellationToken.None));

    private sealed class CapturingLogger<T>(bool enabled = true) : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => enabled;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
