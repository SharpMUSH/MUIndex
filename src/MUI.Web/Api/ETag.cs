using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;

namespace MUI.Web.Api;

/// <summary>
/// Strong ETags, hashed over the exact bytes the response carries (spec §10).
/// </summary>
/// <remarks>
/// Strong, over the body rather than a version stamp: a 304 promises the bytes a client holds are
/// still what we'd send, and a stamp derived from "the newest row we know about" can be wrong exactly
/// when it matters. The streamed dump is hashed the same way via <see cref="HashSink"/>, written
/// twice (once into the sink, once into the response) so memory stays constant. See <c>DumpEndpoints</c>.
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
    /// A comma-separated list, may be <c>*</c>, and members may be weak — <c>W/</c> is stripped
    /// rather than treated as a mismatch, per RFC 9110's weak comparison. Anything unparseable simply
    /// does not match.
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
    /// An <see cref="IBufferWriter{T}"/>, matching what the real response path writes into
    /// (<c>Response.BodyWriter</c>), so the hash pass exercises the same code path as the body it
    /// hashes. Hands out the same fixed buffer every time, so a catalogue of any size costs one buffer.
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
