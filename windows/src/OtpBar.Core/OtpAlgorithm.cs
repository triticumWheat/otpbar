namespace OtpBar.Core;

public enum OtpAlgorithm
{
    Sha1,
    Sha256,
    Sha512
}

public static class OtpAlgorithmNames
{
    public static string ToName(this OtpAlgorithm algorithm) => algorithm switch
    {
        OtpAlgorithm.Sha1 => "SHA1",
        OtpAlgorithm.Sha256 => "SHA256",
        OtpAlgorithm.Sha512 => "SHA512",
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
    };

    public static bool TryParse(string name, out OtpAlgorithm algorithm)
    {
        switch (name)
        {
            case "SHA1": algorithm = OtpAlgorithm.Sha1; return true;
            case "SHA256": algorithm = OtpAlgorithm.Sha256; return true;
            case "SHA512": algorithm = OtpAlgorithm.Sha512; return true;
            default: algorithm = OtpAlgorithm.Sha1; return false;
        }
    }
}
