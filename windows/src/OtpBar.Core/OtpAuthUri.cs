namespace OtpBar.Core;

/// <summary>
/// Reads the otpauth:// URI that services encode into their enrolment QR codes:
/// otpauth://totp/Issuer:account?secret=BASE32&amp;issuer=Issuer&amp;algorithm=SHA1&amp;digits=6&amp;period=30
/// </summary>
public static class OtpAuthUri
{
    public static bool IsOtpAuthUri(string text) =>
        text.TrimStart().StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase);

    public static OtpEntry Parse(string text)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals("otpauth", StringComparison.OrdinalIgnoreCase))
        {
            throw OtpException.InvalidOtpAuthUri();
        }
        // Counter-based codes need a synchronised counter this app does not keep.
        if (!uri.Host.Equals("totp", StringComparison.OrdinalIgnoreCase))
        {
            throw OtpException.UnsupportedToken();
        }

        var parameters = ParseQuery(uri.Query);
        // A blank secret is a broken link, not a key the reader should complain about.
        if (!parameters.TryGetValue("secret", out var secret) || secret.Trim().Length == 0)
        {
            throw OtpException.InvalidOtpAuthUri();
        }

        // The label is "account" or "issuer:account", with optional spaces after the colon.
        var label = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        var separator = label.IndexOf(':');
        var labelIssuer = separator >= 0 ? label[..separator].Trim() : "";
        var account = (separator >= 0 ? label[(separator + 1)..] : label).Trim();

        // The issuer parameter is authoritative; the label prefix is the older convention.
        var name = parameters.TryGetValue("issuer", out var issuer) && issuer.Trim().Length > 0
            ? issuer.Trim()
            : labelIssuer.Length > 0 ? labelIssuer : account;
        if (name.Length == 0)
        {
            throw OtpException.InvalidName();
        }

        var algorithm = OtpAlgorithm.Sha1;
        if (parameters.TryGetValue("algorithm", out var algorithmName) &&
            !OtpAlgorithmNames.TryParse(algorithmName.Trim().ToUpperInvariant(), out algorithm))
        {
            throw OtpException.UnsupportedToken();
        }

        return new OtpEntry(
            name,
            secret,
            account: account,
            algorithm: algorithm,
            digits: Number(parameters, "digits", 6),
            period: Number(parameters, "period", 30));
    }

    private static int Number(Dictionary<string, string> parameters, string key, int fallback)
    {
        if (!parameters.TryGetValue(key, out var text) || text.Trim().Length == 0)
        {
            return fallback;
        }
        // A value the entry would reject anyway is reported as a parameter problem, not as a bad URI.
        return int.TryParse(text.Trim(), out var value) ? value : throw OtpException.InvalidParameters();
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.IndexOf('=');
            if (split > 0)
            {
                parameters[Uri.UnescapeDataString(pair[..split])] = Uri.UnescapeDataString(pair[(split + 1)..]);
            }
        }
        return parameters;
    }
}
