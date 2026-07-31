using Microsoft.Extensions.Logging;

namespace Common.Observability.Tests;

public sealed class RedactingLoggerTests
{
    private const string Marker = RedactingLogger.RedactionMarker;

    private static RedactingLogger Sut(CapturingLogger inner, params string[] sensitive) =>
        new(inner, new HashSet<string>(sensitive, StringComparer.Ordinal));

    // Builds a structured log state (values + the {OriginalFormat} template) as MEL would.
    private static List<KeyValuePair<string, object?>> State(string template, params (string Key, object? Value)[] values)
    {
        var list = values.Select(v => new KeyValuePair<string, object?>(v.Key, v.Value)).ToList();
        list.Add(new KeyValuePair<string, object?>("{OriginalFormat}", template));
        return list;
    }

    [Fact]
    public void Log_SensitiveKey_RedactsValueInStateAndMessage()
    {
        var inner = new CapturingLogger();

        // The original formatter (which would render the clear value) must NOT be used.
        Sut(inner, "Acct").Log(
            LogLevel.Information, default, State("acct {Acct}", ("Acct", "4111111111111111")), null, (_, _) => "orig");

        Assert.Equal(Marker, inner.LastState!.Single(k => k.Key == "Acct").Value);
        Assert.Equal("acct " + Marker, inner.LastMessage);
        Assert.DoesNotContain("4111111111111111", inner.LastMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_NoSensitiveKey_PassesThroughUnchanged()
    {
        var inner = new CapturingLogger();

        Sut(inner, "Acct").Log(
            LogLevel.Information, default, State("Ingested {File}", ("File", "x.dat")), null, (_, _) => "Ingested x.dat");

        Assert.Equal("Ingested x.dat", inner.LastMessage);
        Assert.Equal("x.dat", inner.LastState!.Single(k => k.Key == "File").Value);
    }

    [Fact]
    public void Log_MixedKeys_RedactsOnlySensitive()
    {
        var inner = new CapturingLogger();

        Sut(inner, "Acct").Log(
            LogLevel.Information, default, State("{File} acct {Acct}", ("File", "x.dat"), ("Acct", "555")), null, (_, _) => "orig");

        Assert.Equal("x.dat acct " + Marker, inner.LastMessage);
        Assert.DoesNotContain("555", inner.LastMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_SensitiveKey_NoTemplate_JoinsScrubbedPairs()
    {
        var inner = new CapturingLogger();
        IReadOnlyList<KeyValuePair<string, object?>> state =
            [new("Acct", "555"), new("File", "x.dat")];

        Sut(inner, "Acct").Log(LogLevel.Information, default, state, null, (_, _) => "orig");

        Assert.Contains("Acct=" + Marker, inner.LastMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain("555", inner.LastMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_EmptySensitiveSet_PassesThrough()
    {
        var inner = new CapturingLogger();

        Sut(inner).Log(LogLevel.Information, default, State("acct {Acct}", ("Acct", "555")), null, (_, _) => "acct 555");

        Assert.Equal("acct 555", inner.LastMessage);
    }

    [Fact]
    public void Log_NonKvpState_PassesThrough()
    {
        var inner = new CapturingLogger();

        Sut(inner, "Acct").Log(LogLevel.Information, default, "plain message", null, (s, _) => s);

        Assert.Equal("plain message", inner.LastMessage);
    }

    [Fact]
    public void BeginScope_SensitiveKey_ScrubsScopeState()
    {
        var inner = new CapturingLogger();

        Sut(inner, "Acct").BeginScope(State("scope {Acct}", ("Acct", "555")));

        var scope = (IReadOnlyList<KeyValuePair<string, object?>>)inner.Scopes.Single()!;
        Assert.Equal(Marker, scope.Single(k => k.Key == "Acct").Value);
    }

    [Fact]
    public void BeginScope_NoSensitiveKey_PassesThrough()
    {
        var inner = new CapturingLogger();
        var state = State("scope {File}", ("File", "x.dat"));

        Sut(inner, "Acct").BeginScope(state);

        Assert.Same(state, inner.Scopes.Single());
    }

    [Fact]
    public void IsEnabled_Delegates() => Assert.True(Sut(new CapturingLogger(), "Acct").IsEnabled(LogLevel.Warning));

    [Fact]
    public void Constructor_NullArgs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new RedactingLogger(null!, new HashSet<string>()));
        Assert.Throws<ArgumentNullException>(() => new RedactingLogger(new CapturingLogger(), null!));
    }

    [Fact]
    public void Factory_CreateLogger_ReturnsRedactingLogger() =>
        Assert.IsType<RedactingLogger>(
            new RedactingLoggerFactory(new CapturingLoggerFactory(), new HashSet<string> { "Acct" }).CreateLogger("cat"));

    [Fact]
    public void Factory_AddProviderAndDispose_Delegate()
    {
        var inner = new CapturingLoggerFactory();
        var factory = new RedactingLoggerFactory(inner, new HashSet<string>());

        factory.AddProvider(null!);
        factory.Dispose();

        Assert.True(inner.ProviderAdded);
        Assert.True(inner.Disposed);
    }

    [Fact]
    public void Factory_NullArgs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new RedactingLoggerFactory(null!, new HashSet<string>()));
        Assert.Throws<ArgumentNullException>(() => new RedactingLoggerFactory(new CapturingLoggerFactory(), null!));
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<KeyValuePair<string, object?>>? LastState { get; private set; }

        public string? LastMessage { get; private set; }

        public List<object?> Scopes { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            Scopes.Add(state);
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            LastState = (state as IReadOnlyList<KeyValuePair<string, object?>>)?.ToList();
            LastMessage = formatter(state, exception);
        }
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public bool ProviderAdded { get; private set; }

        public bool Disposed { get; private set; }

        public void AddProvider(ILoggerProvider provider) => ProviderAdded = true;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger();

        public void Dispose() => Disposed = true;
    }
}
