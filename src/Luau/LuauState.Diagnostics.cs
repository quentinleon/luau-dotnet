using System.Buffers;
using System.Text;
using static Luau.Native.NativeMethods;

namespace Luau;

public unsafe partial class LuauState
{
    /// <summary>
    /// Converts a stack value using Luau's display-string semantics, including
    /// a protected <c>__tostring</c> metamethod call when applicable.
    /// </summary>
    public string ToDisplayString(int index)
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
    public string ToDisplayString(int index, int maxUtf8Bytes, out bool truncated)
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
        using var hostOperation = BeginHostOperationIfNeeded();
        var originalTop = lua_gettop(l);
        var restoreStack = true;
        var resetAttempted = false;
        truncated = false;

        try
        {
            LuauNativeProtection.Prepare(context);

            byte* result = null;
            nuint length = 0;
            var status = luau_ffi_protected_luaL_tolstring(l, index, &result, &length);
            LuauNativeProtection.ThrowIfFailed(this, l, status, "convert a value to a display string");

            if (hostOperation.IsOwnedOperationSuspended)
            {
                restoreStack = false;
                resetAttempted = true;
                hostOperation.AbortSuspendedOperation();
                throw new LuauException("A direct host display-string conversion cannot yield or suspend the Luau thread.");
            }

            if (result == null || length == 0)
            {
                return string.Empty;
            }

            if (maxUtf8Bytes is not { } byteLimit)
            {
                if (length > int.MaxValue)
                {
                    throw new LuauException("The Luau display string is too large for managed memory.");
                }

                return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(result, (int)length));
            }

            return DecodeBoundedUtf8(result, length, byteLimit, out truncated);
        }
        catch
        {
            if (!resetAttempted && hostOperation.IsOwnedOperationSuspended)
            {
                restoreStack = false;
                resetAttempted = true;
                hostOperation.AbortSuspendedOperation();
            }

            throw;
        }
        finally
        {
            if (restoreStack)
            {
                lua_settop(l, originalTop);
            }
        }
    }

    static string DecodeBoundedUtf8(
        byte* value,
        nuint length,
        int maxUtf8Bytes,
        out bool truncated)
    {
        if (maxUtf8Bytes == 0)
        {
            truncated = length != 0;
            return string.Empty;
        }

        var bytesToDecode = length > (nuint)maxUtf8Bytes
            ? maxUtf8Bytes
            : checked((int)length);
        if (length > (nuint)bytesToDecode)
        {
            bytesToDecode = TrimIncompleteUtf8Sequence(value, bytesToDecode);
        }

        if (bytesToDecode == 0)
        {
            truncated = length != 0;
            return string.Empty;
        }

        var characters = ArrayPool<char>.Shared.Rent(bytesToDecode);
        try
        {
            var characterCount = Encoding.UTF8.GetChars(
                new ReadOnlySpan<byte>(value, bytesToDecode),
                characters.AsSpan());
            var boundedCharacterCount = GetUtf8BoundedCharacterCount(
                characters.AsSpan(0, characterCount),
                maxUtf8Bytes);

            truncated = length > (nuint)bytesToDecode || boundedCharacterCount < characterCount;
            return new string(characters, 0, boundedCharacterCount);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(characters);
        }
    }

    static int TrimIncompleteUtf8Sequence(byte* value, int length)
    {
        if (length == 0)
        {
            return 0;
        }

        var sequenceStart = length - 1;
        while (sequenceStart > 0 &&
               (value[sequenceStart] & 0xc0) == 0x80 &&
               length - sequenceStart < 4)
        {
            sequenceStart--;
        }

        var leadingByte = value[sequenceStart];
        int expectedLength;
        if ((leadingByte & 0x80) == 0)
        {
            expectedLength = 1;
        }
        else if ((leadingByte & 0xe0) == 0xc0)
        {
            expectedLength = 2;
        }
        else if ((leadingByte & 0xf0) == 0xe0)
        {
            expectedLength = 3;
        }
        else if ((leadingByte & 0xf8) == 0xf0)
        {
            expectedLength = 4;
        }
        else
        {
            return length;
        }

        return sequenceStart + expectedLength > length ? sequenceStart : length;
    }

    static int GetUtf8BoundedCharacterCount(ReadOnlySpan<char> value, int maxUtf8Bytes)
    {
        var encodedBytes = 0;
        var index = 0;

        while (index < value.Length)
        {
            var character = value[index];
            int characterCount;
            int byteCount;

            if (char.IsHighSurrogate(character) &&
                index + 1 < value.Length &&
                char.IsLowSurrogate(value[index + 1]))
            {
                characterCount = 2;
                byteCount = 4;
            }
            else
            {
                characterCount = 1;
                byteCount = character <= '\u007f'
                    ? 1
                    : character <= '\u07ff'
                        ? 2
                        : 3;
            }

            if (encodedBytes > maxUtf8Bytes - byteCount)
            {
                break;
            }

            encodedBytes += byteCount;
            index += characterCount;
        }

        return index;
    }
}
