namespace Common.Security.DataProtection;

/// <summary>
/// Produces a masked, safe-to-display form of a sensitive value for diagnostics (reject queue,
/// dashboards). Selected per field by strategy <see cref="Name"/> in the data-protection policy.
/// </summary>
public interface IMasker
{
    /// <summary>Strategy name referenced by the policy (e.g. <c>first6last4</c>).</summary>
    string Name { get; }

    /// <summary>Returns a masked form of <paramref name="value"/>. Null or empty yields an empty string.</summary>
    string Mask(string? value);
}
