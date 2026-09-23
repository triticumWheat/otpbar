using System.Text;

namespace OtpBar.Core;

internal static class Base32
{
    private static readonly Dictionary<int, int> PaddingForRemainder =
        new() { [0] = 0, [2] = 6, [4] = 4, [5] = 3, [7] = 1 };

    /// <summary>Normalises a secret to upper case without whitespace or padding, rejecting anything undecodable.</summary>
    public static string Canonical(string input)
    {
        var compact = new string(input.ToUpperInvariant().Where(character => !char.IsWhiteSpace(character)).ToArray());
        var parts = compact.Split('=', 2);
        var content = parts[0];
        if (content.Length == 0)
        {
            throw OtpException.InvalidSecret();
        }
        if (!PaddingForRemainder.TryGetValue(Encoding.UTF8.GetByteCount(content) % 8, out var expectedPadding))
        {
            throw OtpException.InvalidSecret();
        }
        if (parts.Length == 2)
        {
            var padding = parts[1];
            if (expectedPadding == 0 || !padding.All(character => character == '=') || padding.Length + 1 != expectedPadding)
            {
                throw OtpException.InvalidSecret();
            }
        }
        Decode(content);
        return content;
    }

    public static byte[] Decode(string canonical)
    {
        var result = new List<byte>();
        uint buffer = 0;
        var bits = 0;
        foreach (var character in Encoding.UTF8.GetBytes(canonical))
        {
            uint value;
            switch (character)
            {
                case >= 65 and <= 90: value = (uint)(character - 65); break;
                case >= 50 and <= 55: value = (uint)(character - 50 + 26); break;
                default: throw OtpException.InvalidSecret();
            }
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                result.Add((byte)((buffer >> bits) & 255));
                buffer &= (uint)((1 << bits) - 1);
            }
        }
        // Leftover bits must be zero, otherwise two different strings would decode to the same key.
        if (result.Count == 0 || buffer != 0)
        {
            throw OtpException.InvalidSecret();
        }
        return result.ToArray();
    }
}
