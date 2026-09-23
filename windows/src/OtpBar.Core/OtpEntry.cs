namespace OtpBar.Core;

public sealed record OtpEntry
{
    private static readonly int[] SupportedPeriods = [10, 30, 60, 90];

    public Guid Id { get; }
    public string Name { get; }
    public string Account { get; }
    public string Secret { get; }
    public OtpAlgorithm Algorithm { get; }
    public int Digits { get; }
    public int Period { get; }

    public OtpEntry(
        string name,
        string secret,
        Guid? id = null,
        string account = "",
        OtpAlgorithm algorithm = OtpAlgorithm.Sha1,
        int digits = 6,
        int period = 30)
    {
        var trimmedName = name.Trim();
        if (trimmedName.Length == 0)
        {
            throw OtpException.InvalidName();
        }
        if (digits < 5 || digits > 8 || !SupportedPeriods.Contains(period))
        {
            throw OtpException.InvalidParameters();
        }
        Secret = Base32.Canonical(secret);
        Id = id ?? Guid.NewGuid();
        Name = trimmedName;
        Account = account.Trim();
        Algorithm = algorithm;
        Digits = digits;
        Period = period;
    }

    /// <summary>Two entries collide when they would always produce the same code, regardless of label.</summary>
    public bool HasSameGenerator(OtpEntry other) =>
        Secret == other.Secret && Algorithm == other.Algorithm && Digits == other.Digits && Period == other.Period;

    /// <summary>
    /// Replaces the description a record would generate, which spells out the secret. That text reaches
    /// accessibility tools, log files and debuggers, none of which should ever be handed the key.
    /// </summary>
    public override string ToString() => Account.Length > 0 ? $"{Name} ({Account})" : Name;
}
