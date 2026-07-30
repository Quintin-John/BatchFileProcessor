namespace Common.Security.DataProtection;

/// <summary>
/// How a single field is protected: its on-the-wire <see cref="Action"/>, the diagnostics
/// masking strategy (if any), and whether it must be redacted from logs/lineage.
/// </summary>
/// <param name="Action">On-the-wire action (clear or encrypt).</param>
/// <param name="MaskStrategy">Masker strategy name for diagnostics, or null for no masking.</param>
/// <param name="RedactInLogs">True if the value must never appear in clear in logs/lineage.</param>
public sealed record FieldProtection(ProtectionAction Action, string? MaskStrategy, bool RedactInLogs);
