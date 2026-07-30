using System.Buffers.Binary;
using System.Text;

namespace Common.Security.DataProtection;

/// <summary>
/// Builds the additional authenticated data (AAD) that binds a ciphertext to its
/// <see cref="FieldProtectionContext"/>. The single authoritative encoder for every protection path
/// (field and payload): the bytes must be identical across paths or a value encrypted by one cannot
/// be decrypted by the other, so this is deliberately not duplicated.
/// </summary>
internal static class ProtectionAad
{
    /// <summary>Encodes the context as length-prefixed AAD bytes.</summary>
    public static byte[] Build(FieldProtectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Length-prefixed encoding so distinct (fileId, recordSeq, field) triples can never collide to
        // the same bytes. A plain delimiter would collide when a value contains it, weakening the binding.
        var fileId = Encoding.UTF8.GetBytes(context.FileId);
        var field = Encoding.UTF8.GetBytes(context.Field);
        var buffer = new byte[sizeof(int) + fileId.Length + sizeof(long) + sizeof(int) + field.Length];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteInt32LittleEndian(span, fileId.Length);
        fileId.CopyTo(span[sizeof(int)..]);
        var offset = sizeof(int) + fileId.Length;

        BinaryPrimitives.WriteInt64LittleEndian(span[offset..], context.RecordSeq);
        offset += sizeof(long);

        BinaryPrimitives.WriteInt32LittleEndian(span[offset..], field.Length);
        field.CopyTo(span[(offset + sizeof(int))..]);

        return buffer;
    }
}
