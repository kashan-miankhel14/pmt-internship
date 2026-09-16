using System.Buffers.Binary;

namespace PMT.Infrastructure.Ai;

/// <summary>
/// Converts embedding vectors to and from the VARBINARY(MAX) representation stored
/// in dbo.AiDocumentChunk.Embedding.
/// </summary>
/// <remarks>
/// <para>
/// <b>Byte order is big-endian, and that is deliberate.</b> The header comment in
/// migration 0013 describes the payload as little-endian, but the T-SQL decoder that
/// actually reads it (dbo.fn_AiDecodeVector) does:
/// </para>
/// <code>CAST(SUBSTRING(@Embedding, n * 4 + 1, 4) AS int)</code>
/// <para>
/// SQL Server interprets a VARBINARY-to-INT cast most-significant-byte first
/// (CAST(0x00000001 AS int) = 1). The decoder then pulls the IEEE-754 sign bit from
/// 0x80000000 and the exponent from 0x7F800000 of that integer. Those masks only line
/// up when each float's 32-bit word was written MSB-first. Writing little-endian bytes
/// would byte-swap every word, so exponents would decode as denormals and cosine
/// similarity would silently return meaningless scores rather than failing loudly.
/// </para>
/// <para>
/// If the vector format is ever changed here, fn_AiDecodeVector must change with it.
/// </para>
/// </remarks>
internal static class EmbeddingSerializer
{
    private const int BytesPerFloat = sizeof(float);

    /// <summary>
    /// Packs a vector into big-endian float32 bytes, or returns null for a null/empty vector.
    /// </summary>
    public static byte[]? Serialize(IReadOnlyList<float>? vector)
    {
        if (vector is null || vector.Count == 0) return null;

        var buffer = new byte[vector.Count * BytesPerFloat];
        for (var i = 0; i < vector.Count; i++)
            BinaryPrimitives.WriteSingleBigEndian(buffer.AsSpan(i * BytesPerFloat, BytesPerFloat), vector[i]);

        return buffer;
    }

    /// <summary>
    /// Unpacks big-endian float32 bytes back into a vector. Trailing bytes that do not
    /// form a complete float are ignored.
    /// </summary>
    public static float[] Deserialize(byte[]? payload)
    {
        if (payload is null || payload.Length < BytesPerFloat) return [];

        var count = payload.Length / BytesPerFloat;
        var vector = new float[count];
        for (var i = 0; i < count; i++)
            vector[i] = BinaryPrimitives.ReadSingleBigEndian(payload.AsSpan(i * BytesPerFloat, BytesPerFloat));

        return vector;
    }
}
