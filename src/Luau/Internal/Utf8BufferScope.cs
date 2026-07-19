using System.Buffers;
using System.Text;

namespace Luau;

/// <summary>
/// Owns one pooled UTF-8 encoding or byte snapshot and optionally appends the
/// NUL terminator required by native name APIs. The terminator is excluded
/// from <see cref="Bytes"/> and included in <see cref="NullTerminatedBytes"/>.
/// </summary>
internal ref struct Utf8BufferScope
{
    byte[]? buffer;
    readonly int length;
    readonly bool nullTerminated;

    internal Utf8BufferScope(ReadOnlySpan<char> value, bool appendNull = false)
        : this(value, Encoding.UTF8.GetByteCount(value), appendNull)
    {
    }

    internal Utf8BufferScope(
        ReadOnlySpan<char> value,
        int byteCount,
        bool appendNull = false)
    {
        if (byteCount < 0 || Encoding.UTF8.GetByteCount(value) != byteCount)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        }

        buffer = ArrayPool<byte>.Shared.Rent(
            Math.Max(1, checked(byteCount + (appendNull ? 1 : 0))));
        length = Encoding.UTF8.GetBytes(value, buffer);
        nullTerminated = appendNull;
        if (appendNull)
        {
            buffer[length] = 0;
        }
    }

    internal Utf8BufferScope(ReadOnlySpan<byte> value, bool appendNull = false)
    {
        buffer = ArrayPool<byte>.Shared.Rent(
            Math.Max(1, checked(value.Length + (appendNull ? 1 : 0))));
        value.CopyTo(buffer);
        length = value.Length;
        nullTerminated = appendNull;
        if (appendNull)
        {
            buffer[length] = 0;
        }
    }

    internal readonly int Length => length;

    internal readonly ReadOnlySpan<byte> Bytes
    {
        get
        {
            var rented = buffer ?? throw new ObjectDisposedException(nameof(Utf8BufferScope));
            return rented.AsSpan(0, length);
        }
    }

    internal readonly ReadOnlySpan<byte> NullTerminatedBytes
    {
        get
        {
            if (!nullTerminated)
            {
                throw new InvalidOperationException("This UTF-8 scope has no NUL terminator.");
            }
            var rented = buffer ?? throw new ObjectDisposedException(nameof(Utf8BufferScope));
            return rented.AsSpan(0, length + 1);
        }
    }

    public void Dispose()
    {
        var rented = buffer;
        buffer = null;
        if (rented != null)
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
