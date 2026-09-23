using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OtpBar.Core;

/// <summary>Reads a 2FAS phone backup, plaintext or password protected, without ever mutating the source bytes.</summary>
public static class TwoFasImporter
{
    public const int MaximumFileSize = 100 * 1024 * 1024;

    private const int PbkdfIterations = 10_000;
    private const int KeyLength = 32;
    private const int SaltLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;

    // Public format marker specified by 2FAS; not a secret.
    private const string ReferenceValue =
        "tRViSsLKzd86Hprh4ceC2OP7xazn4rrt4xhfEUbOjxLX8Rc3mkISXE0lWbmnWfggogbBJhtYgpK6fMl1D6mtsy92R3HkdGfwuXbz" +
        "LebqVFJsR7IZ2w58t938iymwG4824igYy1wi6n2WDpO1Q1P69zwJGs2F5a1qP4MyIiDSD7NCV2OvidXQCBnDlGfmz0f1BQySRkkt" +
        "4ryiJeCjD2o4QsveJ9uDBUn8ELyOrESv5R5DMDkD4iAF8TXU7KyoJujd";

    public static IReadOnlyList<OtpEntry> Read(byte[] data, string? password = null)
    {
        if (data.Length > MaximumFileSize)
        {
            throw OtpException.InvalidBackup();
        }
        try
        {
            return ReadCore(data, password);
        }
        catch (OtpException)
        {
            throw;
        }
        catch (Exception)
        {
            // Never include parser or crypto diagnostics containing backup data in interface errors or logs.
            throw OtpException.InvalidBackup();
        }
    }

    private static IReadOnlyList<OtpEntry> ReadCore(byte[] data, string? password)
    {
        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw OtpException.InvalidBackup();
        }
        if (!root.TryGetProperty("schemaVersion", out var schemaElement))
        {
            throw OtpException.InvalidBackup();
        }
        var schemaVersion = schemaElement.GetInt64();
        if (schemaVersion is not (3 or 4))
        {
            throw OtpException.UnsupportedSchema(schemaVersion);
        }

        var plaintext = Property(root, "services");
        var encrypted = Property(root, "servicesEncrypted");
        var reference = Property(root, "reference");

        List<ServiceDto> services;
        if (encrypted is not null || reference is not null)
        {
            if (encrypted is null || reference is null || plaintext is { } listed && listed.GetArrayLength() > 0)
            {
                throw OtpException.InvalidBackup();
            }
            if (password is null)
            {
                throw OtpException.PasswordRequired();
            }
            var marker = Decrypt(reference.Value.GetString()!, password);
            if (!marker.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(ReferenceValue)))
            {
                throw OtpException.DecryptionFailed();
            }
            using var decrypted = JsonDocument.Parse(Decrypt(encrypted.Value.GetString()!, password));
            services = ParseServices(decrypted.RootElement);
        }
        else
        {
            if (plaintext is null)
            {
                throw OtpException.InvalidBackup();
            }
            services = ParseServices(plaintext.Value);
        }

        // Stable ordering when two groups contain the same position.
        return services
            .Select((service, offset) => (service, offset))
            .OrderBy(item => item.service.Position ?? item.offset)
            .ThenBy(item => item.offset)
            .Select(item => ToEntry(item.service))
            .ToList();
    }

    private static JsonElement? Property(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind != JsonValueKind.Null ? element : null;

    private static OtpEntry ToEntry(ServiceDto service)
    {
        if ((service.TokenType ?? "TOTP").ToUpperInvariant() != "TOTP" ||
            !OtpAlgorithmNames.TryParse((service.Algorithm ?? "SHA1").ToUpperInvariant(), out var algorithm))
        {
            throw OtpException.UnsupportedToken();
        }
        return new OtpEntry(
            service.Name,
            service.Secret,
            account: service.Account ?? "",
            algorithm: algorithm,
            digits: service.Digits ?? 6,
            period: service.Period ?? 30);
    }

    private static List<ServiceDto> ParseServices(JsonElement array)
    {
        var services = new List<ServiceDto>();
        foreach (var element in array.EnumerateArray())
        {
            var otp = element.GetProperty("otp");
            services.Add(new ServiceDto(
                Name: element.GetProperty("name").GetString()!,
                Secret: element.GetProperty("secret").GetString()!,
                Account: OptionalString(otp, "account"),
                TokenType: OptionalString(otp, "tokenType"),
                Algorithm: OptionalString(otp, "algorithm"),
                Digits: OptionalInt(otp, "digits"),
                Period: OptionalInt(otp, "period"),
                Position: element.TryGetProperty("order", out var order) && order.ValueKind != JsonValueKind.Null
                    ? order.GetProperty("position").GetInt32()
                    : null));
        }
        return services;
    }

    private static string? OptionalString(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var element) && element.ValueKind != JsonValueKind.Null ? element.GetString() : null;

    private static int? OptionalInt(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var element) && element.ValueKind != JsonValueKind.Null ? element.GetInt32() : null;

    private static byte[] Decrypt(string encoded, string password)
    {
        var parts = encoded.Split(':');
        if (parts.Length != 3 ||
            !TryDecodeBase64(parts[0], out var payload) ||
            !TryDecodeBase64(parts[1], out var salt) ||
            !TryDecodeBase64(parts[2], out var nonce) ||
            payload.Length < TagLength || salt.Length != SaltLength || nonce.Length != NonceLength)
        {
            throw OtpException.InvalidBackup();
        }

        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, PbkdfIterations, HashAlgorithmName.SHA256, KeyLength);
        try
        {
            using var cipher = new AesGcm(key, TagLength);
            var plaintext = new byte[payload.Length - TagLength];
            cipher.Decrypt(nonce, payload.AsSpan(0, plaintext.Length), payload.AsSpan(plaintext.Length), plaintext);
            return plaintext;
        }
        catch (CryptographicException)
        {
            // Wrong password and tampered ciphertext are indistinguishable by design.
            throw OtpException.DecryptionFailed();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        var buffer = new byte[value.Length / 4 * 3 + 3];
        if (Convert.TryFromBase64String(value, buffer, out var written))
        {
            bytes = buffer[..written];
            return true;
        }
        bytes = [];
        return false;
    }

    private readonly record struct ServiceDto(
        string Name,
        string Secret,
        string? Account,
        string? TokenType,
        string? Algorithm,
        int? Digits,
        int? Period,
        int? Position);
}
