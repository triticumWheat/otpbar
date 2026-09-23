using System.Text.Json.Nodes;
using OtpBar.Core;
using static OtpBarTests.TestSupport;

namespace OtpBarTests;

public class TwoFasImporterTests
{
    private const string AsciiPassword = "otpbar-test-only";

    [Theory]
    [InlineData("plaintext-v3")]
    [InlineData("plaintext-v4")]
    public void ImportsPlaintextVersions(string name)
    {
        var data = Fixture(name);
        var original = data.ToArray();
        var entries = TwoFasImporter.Read(data);

        Assert.Equal(["Example first", "Example second"], entries.Select(entry => entry.Name));
        Assert.Equal([OtpAlgorithm.Sha1, OtpAlgorithm.Sha256], entries.Select(entry => entry.Algorithm));
        Assert.Equal([6, 8], entries.Select(entry => entry.Digits));
        Assert.Equal([30, 60], entries.Select(entry => entry.Period));
        Assert.Equal("first@example.invalid", entries[0].Account);
        Assert.Equal("287082", Totp.Generate(entries[0], 59).Value);
        Assert.Equal(original, data);
    }

    [Theory]
    [InlineData("ascii", AsciiPassword)]
    [InlineData("utf8", "测试密码🔑")]
    [InlineData("empty", "")]
    public void DecryptsIndependentNodeFixtures(string name, string password)
    {
        var plain = TwoFasImporter.Read(Fixture("plaintext-v4"));
        var decrypted = TwoFasImporter.Read(Fixture($"encrypted-{name}-v4"), password);

        Assert.Equal(plain.Count, decrypted.Count);
        foreach (var (actual, expected) in decrypted.Zip(plain))
        {
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.Account, actual.Account);
            Assert.True(actual.HasSameGenerator(expected));
        }
    }

    [Fact]
    public void PasswordAndReferenceFailures()
    {
        var encrypted = Fixture("encrypted-ascii-v4");
        Throws(OtpErrorKind.PasswordRequired, () => TwoFasImporter.Read(encrypted));

        foreach (var password in new[] { "wrong", "", "otpbar-test-only " })
        {
            Throws(OtpErrorKind.DecryptionFailed, () => TwoFasImporter.Read(encrypted, password));
        }
        Throws(OtpErrorKind.DecryptionFailed,
            () => TwoFasImporter.Read(Fixture("encrypted-bad-reference-v4"), AsciiPassword));
    }

    [Fact]
    public void EveryEncryptedComponentIsAuthenticated()
    {
        // Both ciphertext+tag fields, both salts and both nonces must be authenticated.
        foreach (var field in new[] { "servicesEncrypted", "reference" })
        {
            foreach (var component in new[] { 0, 1, 2 })
            {
                foreach (var atEnd in new[] { false, true })
                {
                    var json = FixtureObject("encrypted-ascii-v4");
                    var parts = json[field]!.GetValue<string>().Split(':');
                    var bytes = Convert.FromBase64String(parts[component]);
                    bytes[atEnd ? bytes.Length - 1 : 0] ^= 1;
                    parts[component] = Convert.ToBase64String(bytes);
                    json[field] = string.Join(":", parts);

                    Throws(OtpErrorKind.DecryptionFailed,
                        () => TwoFasImporter.Read(json.ToBytes(), AsciiPassword));
                }
            }
        }
    }

    [Fact]
    public void MalformedEncryptedEnvelopesFailClosed()
    {
        foreach (var value in new[] { "", ":", "::", "invalid:base64:!", "AA==:AA==:AA==" })
        {
            var json = FixtureObject("encrypted-ascii-v4");
            json["servicesEncrypted"] = value;
            Throws(OtpErrorKind.InvalidBackup, () => TwoFasImporter.Read(json.ToBytes(), AsciiPassword));
        }

        foreach (var field in new[] { "servicesEncrypted", "reference" })
        {
            var json = FixtureObject("encrypted-ascii-v4");
            json.Remove(field);
            Throws(OtpErrorKind.InvalidBackup, () => TwoFasImporter.Read(json.ToBytes(), AsciiPassword));
        }

        var mixed = FixtureObject("encrypted-ascii-v4");
        mixed["services"] = FixtureObject("plaintext-v4")["services"]!.DeepClone();
        Throws(OtpErrorKind.InvalidBackup, () => TwoFasImporter.Read(mixed.ToBytes(), AsciiPassword));
    }

    [Fact]
    public void UnsupportedVersionsAndTokensFailExplicitly()
    {
        foreach (var version in new[] { 0L, 1L, 2L, 5L, long.MaxValue })
        {
            var data = ModifiedBackup(json => json["schemaVersion"] = version);
            var error = Throws(OtpErrorKind.UnsupportedSchema, () => TwoFasImporter.Read(data));
            Assert.Equal(version, error.SchemaVersion);
        }

        (string Field, string Value)[] unsupported =
            [("tokenType", "HOTP"), ("tokenType", "STEAM"), ("tokenType", "future"), ("algorithm", "MD5")];
        foreach (var (field, value) in unsupported)
        {
            var data = ModifiedBackup(json => json["services"]![0]!["otp"]![field] = value);
            Throws(OtpErrorKind.UnsupportedToken, () => TwoFasImporter.Read(data));
        }
    }

    [Fact]
    public void MissingOptionalOtpFieldsUse2FasDefaults()
    {
        var data = ModifiedBackup(json => json["services"]![0]!["otp"] =
            new JsonObject { ["account"] = "label", ["algorithm"] = null, ["digits"] = null });

        var entries = TwoFasImporter.Read(data);
        Assert.Equal(OtpAlgorithm.Sha1, entries[1].Algorithm);
        Assert.Equal(6, entries[1].Digits);
        Assert.Equal(30, entries[1].Period);
    }

    [Fact]
    public void CorruptJsonMissingFieldsAndInvalidEntriesFail()
    {
        foreach (var input in new[] { "not json", "[]", "{}", "{\"schemaVersion\":4}", "{\"schemaVersion\":4,\"services\":null}" })
        {
            Throws(OtpErrorKind.InvalidBackup, () => TwoFasImporter.Read(System.Text.Encoding.UTF8.GetBytes(input)));
        }

        var invalidSecret = ModifiedBackup(json => json["services"]![0]!["secret"] = "not-a-key!");
        Throws(OtpErrorKind.InvalidSecret, () => TwoFasImporter.Read(invalidSecret));

        var invalidDigits = ModifiedBackup(json => json["services"]![0]!["otp"]!["digits"] = 0);
        Throws(OtpErrorKind.InvalidParameters, () => TwoFasImporter.Read(invalidDigits));
    }
}
