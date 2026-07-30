using System.Globalization;
using System.Text;
using System.Text.Json;
using Common.Messaging.Contracts;

namespace Common.Security.DataProtection;

/// <summary>
/// Default <see cref="IFieldProtector"/>. Encrypts clear values with the active key via the
/// configured <see cref="ICryptoProvider"/>, binding each ciphertext to its
/// <see cref="FieldProtectionContext"/>. Clear values are serialized through the messaging
/// contract's JSON so typed values (string, number, boolean, null) round-trip losslessly.
/// </summary>
public sealed class DefaultFieldProtector : IFieldProtector
{
    private readonly ICryptoProvider _crypto;
    private readonly IKeyProvider _keys;
    private readonly DataProtectionPolicy _policy;
    private readonly Dictionary<string, IMasker> _maskers;

    /// <summary>Creates the protector.</summary>
    /// <param name="crypto">Crypto provider.</param>
    /// <param name="keys">Key provider.</param>
    /// <param name="policy">Data-protection policy.</param>
    /// <param name="maskers">Available maskers, keyed by their strategy name.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public DefaultFieldProtector(
        ICryptoProvider crypto,
        IKeyProvider keys,
        DataProtectionPolicy policy,
        IEnumerable<IMasker> maskers)
    {
        ArgumentNullException.ThrowIfNull(crypto);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(maskers);

        _crypto = crypto;
        _keys = keys;
        _policy = policy;
        _maskers = maskers.ToDictionary(m => m.Name, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public FieldValue Protect(FieldProtectionContext context, FieldValue value)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(value);

        var protection = _policy.GetProtection(context.Field);
        if (protection.Action != ProtectionAction.Encrypt || value is EncryptedFieldValue)
        {
            return value;
        }

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value, MessagingJson.Options);
        var envelope = _crypto.Encrypt(plaintext, _keys.GetActiveKey(), Aad(context));
        return new EncryptedFieldValue(envelope);
    }

    /// <inheritdoc />
    public FieldValue Unprotect(FieldProtectionContext context, FieldValue value)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(value);

        if (value is not EncryptedFieldValue encrypted)
        {
            return value;
        }

        var key = _keys.ResolveKey(encrypted.Value.KeyId, encrypted.Value.KeyVersion);
        var plaintext = _crypto.Decrypt(encrypted.Value, key, Aad(context));
        return JsonSerializer.Deserialize<FieldValue>(plaintext, MessagingJson.Options)
            ?? throw new InvalidOperationException("Decrypted field value was null.");
    }

    /// <inheritdoc />
    public string Mask(FieldProtectionContext context, FieldValue value)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(value);

        var protection = _policy.GetProtection(context.Field);
        var clear = ClearString(value);

        if (protection.MaskStrategy is null)
        {
            return clear;
        }

        if (!_maskers.TryGetValue(protection.MaskStrategy, out var masker))
        {
            throw new InvalidOperationException($"Unknown mask strategy '{protection.MaskStrategy}'.");
        }

        return masker.Mask(clear);
    }

    private static byte[] Aad(FieldProtectionContext context) =>
        Encoding.UTF8.GetBytes($"{context.FileId}:{context.RecordSeq}:{context.Field}");

    private static string ClearString(FieldValue value) => value switch
    {
        EncryptedFieldValue => throw new InvalidOperationException("Cannot mask an already-encrypted value."),
        ClearFieldValue { Value: null } => string.Empty,
        ClearFieldValue { Value: string s } => s,
        ClearFieldValue clear => Convert.ToString(clear.Value, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => throw new InvalidOperationException($"Unsupported field value type '{value.GetType()}'."),
    };
}
