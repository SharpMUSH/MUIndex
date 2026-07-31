using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;

namespace MUI.Web.Api;

/// <summary>
/// Strong ETags, hashed over the exact bytes the response carries (spec §10).
/// </summary>
/// <remarks>
/// <para>
/// Strong rather than weak, and over the body rather than over a version stamp, because a 304 is a
/// promise that the bytes a client already holds are still the bytes we would send. A stamp derived
/// from "the newest row we know about" is a guess at that promise, and it is wrong in exactly the
/// case it matters — a value re-confirmed with no row changing, or a field changed by a writer whose
/// timestamp the stamp does not read.
/// </para>
/// <para>
/// The streamed dump is hashed the same way and not with a stamp: it is written twice, once into
/// <see cref="HashSink"/> and once into the response, so the hash is still of the exact bytes and
/// memory stays constant. See <c>DumpEndpoints</c>.
/// </para>
/// </remarks>
public static class ETag
{
    /// <summary>The entity tag for a body, quoted and ready to be a header value.</summary>
    public static string Of(ReadOnlySpan<byte> body) => Format(SHA256.HashData(body));

    public static string Format(byte[] hash) => $"\"sha256-{Base64Url.EncodeToString(hash)}\"";

    /// <summary>
    /// Whether an <c>If-None-Match</c> header matches, and the response is therefore a 304.
    /// </summary>
    /// <remarks>
    /// The header is a comma-separated list, may be <c>*</c>, and its members may be weak. A weak
    /// tag whose opaque part equals ours is a match under RFC 9110's weak comparison, which is the
    /// comparison <c>If-None-Match</c> is defined to use — so <c>W/</c> is stripped rather than
    /// treated as a mismatch. Anything unparseable simply does not match, which costs a caller a
    /// body and never a wrong answer.
    /// </remarks>
    public static bool Matches(string? ifNoneMatch, string etag)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
        {
            return false;
        }

        foreach (var candidate in ifNoneMatch.Split(','))
        {
            var trimmed = candidate.Trim();
            if (trimmed == "*")
            {
                return true;
            }

            if (trimmed.StartsWith("W/", StringComparison.Ordinal))
            {
                trimmed = trimmed[2..];
            }

            if (string.Equals(trimmed, etag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A sink that keeps nothing but the hash of what went through it.
    /// </summary>
    /// <remarks>
    /// An <see cref="IBufferWriter{T}"/> and not a <see cref="Stream"/>, because that is what the
    /// streamed dump writes into on the real path too — <c>Response.BodyWriter</c> is one — and a
    /// hash pass that went through a different kind of sink would be hashing a different code path
    /// than the one that produces the body. It hands out the same fixed buffer every time and hashes
    /// what was written into it, so a catalogue of any size costs one buffer.
    /// </remarks>
    public sealed class HashSink : IBufferWriter<byte>, IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        private byte[] _buffer = new byte[16 * 1024];

        public string Tag() => Format(_hash.GetCurrentHash());

        public void Advance(int count) => _hash.AppendData(_buffer.AsSpan(0, count));

        public Memory<byte> GetMemory(int sizeHint = 0) => Ensure(sizeHint).AsMemory();

        public Span<byte> GetSpan(int sizeHint = 0) => Ensure(sizeHint).AsSpan();

        public void Dispose() => _hash.Dispose();

        private byte[] Ensure(int sizeHint)
        {
            if (sizeHint > _buffer.Length)
            {
                _buffer = new byte[Math.Max(sizeHint, _buffer.Length * 2)];
            }

            return _buffer;
        }
    }
}
