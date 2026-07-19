using System.Security.Cryptography;

namespace Luau.Internal;

internal static class LuauBytecodeHash
{
    internal const int Sha256HexLength = 64;

    internal static string Sha256(ReadOnlySpan<byte> value)
    {
        using var algorithm = SHA256.Create();
        Span<byte> hash = stackalloc byte[32];
        if (!algorithm.TryComputeHash(value, hash, out var written) || written != hash.Length)
        {
            throw new CryptographicException("SHA-256 did not produce a complete digest.");
        }
        return ToLowerHex(hash);
    }

    internal static bool IsSha256(string value)
    {
        if (value == null || value.Length != Sha256HexLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!((character >= '0' && character <= '9') ||
                (character >= 'a' && character <= 'f') ||
                (character >= 'A' && character <= 'F')))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool EqualsSha256(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    internal static string ToLowerHex(ReadOnlySpan<byte> value)
    {
        const string alphabet = "0123456789abcdef";
        var result = new char[value.Length * 2];
        for (var index = 0; index < value.Length; index++)
        {
            result[index * 2] = alphabet[value[index] >> 4];
            result[(index * 2) + 1] = alphabet[value[index] & 0x0f];
        }

        return new string(result);
    }

    internal static void WriteHex(string value, Span<byte> destination)
    {
        if (!IsSha256(value) || destination.Length < 32)
        {
            throw new ArgumentException("A complete SHA-256 destination is required.", nameof(value));
        }

        for (var index = 0; index < 32; index++)
        {
            destination[index] = (byte)((Hex(value[index * 2]) << 4) | Hex(value[(index * 2) + 1]));
        }
    }

    static int Hex(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'a' and <= 'f' => value - 'a' + 10,
        >= 'A' and <= 'F' => value - 'A' + 10,
        _ => throw new ArgumentException("Invalid hexadecimal character."),
    };
}
