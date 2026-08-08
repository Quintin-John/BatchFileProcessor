namespace Ingestion.Worker.Profiles;

/// <summary>
/// The formats this deployment can ingest, keyed by the token a profile selects one by. The single
/// place a format is registered: adding one is a new <see cref="IRecordFormat"/> plus an entry here, and no
/// existing type grows another branch. Fail-closed — a token with no registered format is rejected when the
/// profiles are loaded, not when the first file arrives.
/// </summary>
internal static class RecordFormats
{
    private static readonly IRecordFormat[] Registered = [new FixedLengthFormat(), new DelimitedFormat()];

    private static readonly Dictionary<string, IRecordFormat> ByToken =
        Registered.ToDictionary(format => format.Token, StringComparer.OrdinalIgnoreCase);

    /// <summary>The registered tokens, in registration order, for diagnostics.</summary>
    public static IReadOnlyList<string> Tokens { get; } = Registered.Select(format => format.Token).ToArray();

    /// <summary>Resolves a profile's declared format token, or null if none is registered for it.</summary>
    /// <param name="token">The token as written in the profile; required.</param>
    /// <exception cref="ArgumentNullException"><paramref name="token"/> is null.</exception>
    public static IRecordFormat? Resolve(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return ByToken.GetValueOrDefault(token);
    }
}
