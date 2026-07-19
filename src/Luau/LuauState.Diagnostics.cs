using static Luau.Internal.Interop.NativeMethods;

namespace Luau;

public unsafe partial class LuauState
{
    /// <summary>
    /// Converts a stack value using Luau's display-string semantics, including
    /// a protected <c>__tostring</c> metamethod call when applicable.
    /// </summary>
    internal string ToDisplayString(int index)
    {
        return ToDisplayStringCore(index, maxUtf8Bytes: null, out _);
    }

    /// <summary>
    /// Converts a stack value using Luau's display-string semantics while
    /// limiting the UTF-8 size of the returned managed string.
    /// </summary>
    /// <param name="index">The stack index to format.</param>
    /// <param name="maxUtf8Bytes">
    /// The maximum number of bytes the returned string occupies when encoded
    /// as UTF-8. A value of zero returns an empty string after formatting the
    /// value.
    /// </param>
    /// <param name="truncated">
    /// Set to <see langword="true"/> when any formatted content was omitted to
    /// satisfy <paramref name="maxUtf8Bytes"/>.
    /// </param>
    internal string ToDisplayString(int index, int maxUtf8Bytes, out bool truncated)
    {
        if (maxUtf8Bytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxUtf8Bytes),
                maxUtf8Bytes,
                "The UTF-8 display-string limit cannot be negative.");
        }

        return ToDisplayStringCore(index, maxUtf8Bytes, out truncated);
    }

    string ToDisplayStringCore(int index, int? maxUtf8Bytes, out bool truncated)
    {
        ThrowIfDisposed();
        using var access = EnterNativeAccess();
        using var hostOperation = new LuauDirectHostOperationScope(this);
        truncated = false;

        LuauNativeProtection.Prepare(context);

        byte* result = null;
        ulong length = 0;
        var status = luau_host_to_display_string(l, index, &result, &length);
        LuauNativeProtection.ThrowIfFailed(this, l, status, "convert a value to a display string");

        string formatted;
        if (result == null || length == 0)
        {
            formatted = string.Empty;
        }
        else if (maxUtf8Bytes is not { } byteLimit)
        {
            ReserveDecodedString(length);
            formatted = System.Text.Encoding.UTF8.GetString(
                new ReadOnlySpan<byte>(result, checked((int)length)));
        }
        else
        {
            var effectiveLimit = Options.MaxDecodedStringBytes is { } rootLimit
                ? Math.Min(byteLimit, rootLimit)
                : byteLimit;
            var decodedLength = BoundedUtf8Decoder.GetValidPrefixLength(
                result,
                length,
                effectiveLimit);
            context.GetActiveOperation()?.ReserveDecodedBytes((ulong)decodedLength);
            formatted = BoundedUtf8Decoder.Decode(
                result,
                length,
                effectiveLimit,
                out truncated);
        }

        hostOperation.CompleteAndRestore(
            "A direct host display-string conversion cannot yield or suspend the Luau thread.");
        return formatted;
    }

}
