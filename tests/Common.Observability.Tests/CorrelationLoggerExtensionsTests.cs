using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Common.Observability.Tests;

public sealed class CorrelationLoggerExtensionsTests
{
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

    [Fact]
    public void BeginCorrelationScope_WithinScopeAndActivity_AddsAllIds()
    {
        var logger = new CapturingLogger();
        var run = RunContext.NewRun();
        using var activity = new Activity("op").Start();

        using (CorrelationScope.Begin(run))
        using (logger.BeginCorrelationScope())
        {
            Assert.NotNull(logger.LastScopeState);
            Assert.Equal(run.RunId, logger.LastScopeState![ObservabilityInstrumentation.RunIdTag]);
            Assert.Equal(run.CorrelationId, logger.LastScopeState[ObservabilityInstrumentation.CorrelationIdTag]);
            Assert.Equal(activity.TraceId.ToString(), logger.LastScopeState[CorrelationLoggerExtensions.TraceIdField]);
            Assert.Equal(activity.SpanId.ToString(), logger.LastScopeState[CorrelationLoggerExtensions.SpanIdField]);
        }
    }

    [Fact]
    public void BeginCorrelationScope_WithNoScopeOrActivity_ReturnsNull()
    {
        var logger = new CapturingLogger();

        var scope = logger.BeginCorrelationScope();

        Assert.Null(scope);
        Assert.Null(logger.LastScopeState);
    }

    [Fact]
    public void BeginCorrelationScope_NullLogger_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((ILogger)null!).BeginCorrelationScope());
    }
}
