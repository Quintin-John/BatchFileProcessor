using Microsoft.Extensions.Logging;

namespace Common.Observability.Tests;

public sealed class RedactingLoggerTests
{
    private const string Marker = RedactingLogger.RedactionMarker;

    // The redaction set is built from the layout's encrypt flags, so the key is whatever a layout happened
    // to name; this logger treats it as opaque. The value is arbitrary — it must not survive regardless.
    private const string SensitiveKey = "Secret";
    private const string SensitiveValue = "some sensitive value";
    private const string OtherKey = "File";

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
        Sut(inner, SensitiveKey).Log(
            LogLevel.Information, default, State("value {Secret}", (SensitiveKey, SensitiveValue)), null, (_, _) => "orig");

        Assert.Equal(Marker, inner.LastState!.Single(k => k.Key == SensitiveKey).Value);
        Assert.Equal("value " + Marker, inner.LastMessage);
        Assert.DoesNotContain(SensitiveValue, inner.LastMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_NoSensitiveKey_PassesThroughUnchanged()
    {
        var inner = new CapturingLogger();

        Sut(inner, SensitiveKey).Log(
            LogLevel.Information, default, State("Ingested {File}", (OtherKey, "x.dat")), null, (_, _) => "Ingested x.dat");

        Assert.Equal("Ingested x.dat", inner.LastMessage);
        Assert.Equal("x.dat", inner.LastState!.Single(k => k.Key == OtherKey).Value);
    }

    [Fact]
    public void Log_MixedKeys_RedactsOnlySensitive()
    {
        var inner = new CapturingLogger();

        Sut(inner, SensitiveKey).Log(
            LogLevel.Information, default, State("{File} value {Secret}", (OtherKey, "x.dat"), (SensitiveKey, SensitiveValue)), null, (_, _) => "orig");

        Assert.Equal("x.dat value " + Marker, inner.LastMessage);
        Assert.DoesNotContain(SensitiveValue, inner.LastMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_SensitiveKey_NoTemplate_JoinsScrubbedPairs()
    {
        var inner = new CapturingLogger();
        IReadOnlyList<KeyValuePair<string, object?>> state =
            [new(SensitiveKey, SensitiveValue), new(OtherKey, "x.dat")];

        Sut(inner, SensitiveKey).Log(LogLevel.Information, default, state, null, (_, _) => "orig");

        Assert.Contains(SensitiveKey + "=" + Marker, inner.LastMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain(SensitiveValue, inner.LastMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_EmptySensitiveSet_PassesThrough()
    {
        var inner = new CapturingLogger();

        const string rendered = "value " + SensitiveValue;

        Sut(inner).Log(
            LogLevel.Information, default, State("value {Secret}", (SensitiveKey, SensitiveValue)), null,
            (_, _) => rendered);

        Assert.Equal(rendered, inner.LastMessage);
    }

    [Fact]
    public void Log_NonKvpState_PassesThrough()
    {
        var inner = new CapturingLogger();

        Sut(inner, SensitiveKey).Log(LogLevel.Information, default, "plain message", null, (s, _) => s);

        Assert.Equal("plain message", inner.LastMessage);
    }

    [Fact]
    public void BeginScope_SensitiveKey_ScrubsScopeState()
    {
        var inner = new CapturingLogger();

        Sut(inner, SensitiveKey).BeginScope(State("scope {Secret}", (SensitiveKey, SensitiveValue)));

        var scope = (IReadOnlyList<KeyValuePair<string, object?>>)inner.Scopes.Single()!;
        Assert.Equal(Marker, scope.Single(k => k.Key == SensitiveKey).Value);
    }

    [Fact]
    public void BeginScope_NoSensitiveKey_PassesThrough()
    {
        var inner = new CapturingLogger();
        var state = State("scope {File}", (OtherKey, "x.dat"));

        Sut(inner, SensitiveKey).BeginScope(state);

        Assert.Same(state, inner.Scopes.Single());
    }

    [Fact]
    public void IsEnabled_Delegates() => Assert.True(Sut(new CapturingLogger(), SensitiveKey).IsEnabled(LogLevel.Warning));

    [Fact]
    public void Constructor_NullArgs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new RedactingLogger(null!, new HashSet<string>()));
        Assert.Throws<ArgumentNullException>(() => new RedactingLogger(new CapturingLogger(), null!));
    }

    [Fact]
    public void Factory_CreateLogger_ReturnsRedactingLogger() =>
        Assert.IsType<RedactingLogger>(
            new RedactingLoggerFactory(new CapturingLoggerFactory(), new HashSet<string> { SensitiveKey }).CreateLogger("cat"));

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
