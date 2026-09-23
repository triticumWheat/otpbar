using OtpBar.Core;
using static OtpBarTests.TestSupport;

namespace OtpBarTests;

public class OtpAuthUriTests
{
    [Fact]
    public void ReadsEveryParameterOfAFullUri()
    {
        var entry = OtpAuthUri.Parse(
            $"otpauth://totp/Example:first@example.invalid?secret={PublicTestSecret}" +
            "&issuer=Example&algorithm=SHA256&digits=8&period=60");

        Assert.Equal("Example", entry.Name);
        Assert.Equal("first@example.invalid", entry.Account);
        Assert.Equal(PublicTestSecret, entry.Secret);
        Assert.Equal(OtpAlgorithm.Sha256, entry.Algorithm);
        Assert.Equal(8, entry.Digits);
        Assert.Equal(60, entry.Period);
    }

    [Fact]
    public void OmittedParametersFallBackToTheOtpauthDefaults()
    {
        var entry = OtpAuthUri.Parse($"otpauth://totp/GitHub:me@example.invalid?secret={PublicTestSecret}");

        Assert.Equal("GitHub", entry.Name);
        Assert.Equal("me@example.invalid", entry.Account);
        Assert.Equal(OtpAlgorithm.Sha1, entry.Algorithm);
        Assert.Equal(6, entry.Digits);
        Assert.Equal(30, entry.Period);
        // The same public key and parameters the RFC vectors use.
        Assert.Equal("287082", Totp.Generate(entry, 59).Value);
    }

    [Fact]
    public void IssuerParameterWinsOverTheLabelPrefix()
    {
        var entry = OtpAuthUri.Parse(
            $"otpauth://totp/Stale:me@example.invalid?secret={PublicTestSecret}&issuer=Current");

        Assert.Equal("Current", entry.Name);
        Assert.Equal("me@example.invalid", entry.Account);
    }

    [Theory]
    [InlineData("otpauth://totp/GitHub%3Ame%40example.invalid?secret={0}")]
    [InlineData("otpauth://totp/GitHub:%20me@example.invalid?secret={0}")]
    [InlineData("  otpauth://totp/GitHub:me@example.invalid?secret={0}  ")]
    public void LabelsAreUnescapedAndTrimmed(string template)
    {
        var entry = OtpAuthUri.Parse(string.Format(template, PublicTestSecret));

        Assert.Equal("GitHub", entry.Name);
        Assert.Equal("me@example.invalid", entry.Account);
    }

    [Fact]
    public void WithoutAnIssuerTheAccountBecomesTheName()
    {
        var entry = OtpAuthUri.Parse($"otpauth://totp/me@example.invalid?secret={PublicTestSecret}");

        Assert.Equal("me@example.invalid", entry.Name);
        Assert.Equal("me@example.invalid", entry.Account);
    }

    [Theory]
    [InlineData("otpauth://hotp/Example?secret={0}&counter=0")]
    [InlineData("otpauth://totp/Example?secret={0}&algorithm=MD5")]
    public void CounterCodesAndUnknownAlgorithmsAreRejected(string template) =>
        Throws(OtpErrorKind.UnsupportedToken, () => OtpAuthUri.Parse(string.Format(template, PublicTestSecret)));

    [Theory]
    [InlineData("")]
    [InlineData("not a uri")]
    [InlineData("https://example.invalid/totp?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ")]
    [InlineData("otpauth://totp/Example")]
    [InlineData("otpauth://totp/Example?issuer=Example")]
    [InlineData("otpauth://totp/Example?secret=")]
    public void UrisWithoutAUsableSecretAreRejected(string text) =>
        Throws(OtpErrorKind.InvalidOtpAuthUri, () => OtpAuthUri.Parse(text));

    [Fact]
    public void EntryValidationStillApplies()
    {
        Throws(OtpErrorKind.InvalidSecret, () => OtpAuthUri.Parse("otpauth://totp/Example?secret=not-a-key!"));
        foreach (var query in new[] { "digits=9", "digits=abc", "period=45", "period=" + long.MaxValue })
        {
            Throws(OtpErrorKind.InvalidParameters,
                () => OtpAuthUri.Parse($"otpauth://totp/Example?secret={PublicTestSecret}&{query}"));
        }
    }

    [Theory]
    [InlineData("otpauth://totp/x?secret=y", true)]
    [InlineData("  OTPAUTH://TOTP/x?secret=y", true)]
    [InlineData("GEZDGNBVGY3TQOJQ", false)]
    [InlineData("", false)]
    public void RecognisesWhetherTextLooksLikeALink(string text, bool expected) =>
        Assert.Equal(expected, OtpAuthUri.IsOtpAuthUri(text));
}
