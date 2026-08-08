namespace Common.Security.Encryption;

/// <summary>What to do with a field's value on the wire.</summary>
public enum ProtectionAction
{
    /// <summary>Carry the value in clear (non-sensitive field).</summary>
    Clear,

    /// <summary>Encrypt the value at field level.</summary>
    Encrypt,
}
