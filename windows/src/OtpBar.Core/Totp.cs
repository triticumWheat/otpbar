using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace OtpBar.Core;

public readonly record struct OtpCode(string Value, double ValidUntilUnixSeconds)
{
    public DateTimeOffset ValidUntil => DateTimeOffset.UnixEpoch.AddSeconds(ValidUntilUnixSeconds);
}

public static class Totp
{
    public static OtpCode Generate(OtpEntry entry, DateTimeOffset at) =>
        Generate(entry, (at - DateTimeOffset.UnixEpoch).TotalSeconds);

    public static OtpCode Generate(OtpEntry entry, double unixSeconds)
    {
        var counterValue = Math.Floor(unixSeconds / entry.Period);
        if (!double.IsFinite(unixSeconds) || unixSeconds < 0 || !(counterValue < ulong.MaxValue))
        {
            throw OtpException.InvalidTime();
        }

        Span<byte> message = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(message, (ulong)counterValue);
        var key = Base32.Decode(entry.Secret);
        byte[] digest;
        try
        {
            digest = entry.Algorithm switch
            {
                OtpAlgorithm.Sha1 => HMACSHA1.HashData(key, message),
                OtpAlgorithm.Sha256 => HMACSHA256.HashData(key, message),
                OtpAlgorithm.Sha512 => HMACSHA512.HashData(key, message),
                _ => throw new ArgumentOutOfRangeException(nameof(entry))
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        var offset = digest[^1] & 15;
        var truncated = BinaryPrimitives.ReadUInt32BigEndian(digest.AsSpan(offset, 4)) & 0x7fff_ffffu;
        uint modulus = 1;
        for (var index = 0; index < entry.Digits; index++)
        {
            modulus *= 10;
        }
        var value = (truncated % modulus).ToString(CultureInfo.InvariantCulture).PadLeft(entry.Digits, '0');
        return new OtpCode(value, (counterValue + 1) * entry.Period);
    }
}
