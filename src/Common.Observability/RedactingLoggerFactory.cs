using Microsoft.Extensions.Logging;

namespace Common.Observability;

/// <summary>
/// An <see cref="ILoggerFactory"/> decorator that returns <see cref="RedactingLogger"/> instances, so every
/// logger handed out redacts sensitive structured values before they reach a sink. Provider registration and
/// disposal delegate to the wrapped factory.
/// </summary>
internal sealed class RedactingLoggerFactory : ILoggerFactory
{
    private readonly ILoggerFactory _inner;
    private readonly IReadOnlySet<string> _sensitiveKeys;

    /// <summary>Creates the decorator over an inner factory.</summary>
    /// <param name="inner">The factory to delegate to; required.</param>
    /// <param name="sensitiveKeys">Keys whose values must be redacted; required (may be empty).</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public RedactingLoggerFactory(ILoggerFactory inner, IReadOnlySet<string> sensitiveKeys)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(sensitiveKeys);
        _inner = inner;
        _sensitiveKeys = sensitiveKeys;
    }

    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider) => _inner.AddProvider(provider);

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) =>
        new RedactingLogger(_inner.CreateLogger(categoryName), _sensitiveKeys);

    /// <inheritdoc />
    public void Dispose() => _inner.Dispose();
}
