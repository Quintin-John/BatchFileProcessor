namespace Common.Observability;

/// <summary>
/// Ambient access to the current <see cref="RunContext"/>. Flows across async continuations via
/// <see cref="AsyncLocal{T}"/>. Scopes nest: disposing a scope restores the previously current
/// context.
/// </summary>
public static class CorrelationScope
{
    private static readonly AsyncLocal<RunContext?> Ambient = new();

    /// <summary>The current run context, or null if no scope is active.</summary>
    public static RunContext? Current => Ambient.Value;

    /// <summary>Makes <paramref name="context"/> current until the returned scope is disposed.</summary>
    /// <param name="context">The context to make current; required.</param>
    /// <returns>A disposable that restores the previously current context.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    public static IDisposable Begin(RunContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var previous = Ambient.Value;
        Ambient.Value = context;
        return new Restorer(previous);
    }

    private sealed class Restorer : IDisposable
    {
        private readonly RunContext? _previous;
        private bool _disposed;

        public Restorer(RunContext? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Ambient.Value = _previous;
            GC.SuppressFinalize(this);
        }
    }
}
