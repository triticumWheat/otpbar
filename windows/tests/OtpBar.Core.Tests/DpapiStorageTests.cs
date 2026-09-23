using System.Security.Cryptography;
using OtpBar.Core;
using static OtpBarTests.TestSupport;

namespace OtpBarTests;

public sealed class DpapiStorageTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "otpbar-tests-" + Guid.NewGuid().ToString("N"));

    private string Path_ => Path.Combine(_directory, "vault.v1.dat");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void MissingFileReadsAsAnEmptyVaultRatherThanAnError()
    {
        Assert.Null(new DpapiStorage(Path_).Read());
        Assert.Empty(new TokenVault(new DpapiStorage(Path_)).Entries);
        Assert.False(File.Exists(Path_));
    }

    [Fact]
    public void ImportSurvivesAReopenAndIsNotStoredInTheClear()
    {
        var vault = new TokenVault(new DpapiStorage(Path_));
        vault.ImportBackup(Fixture("plaintext-v4"));

        var stored = File.ReadAllBytes(Path_);
        Assert.DoesNotContain(PublicTestSecret, System.Text.Encoding.ASCII.GetString(stored));
        Assert.Equal(vault.Entries, new TokenVault(new DpapiStorage(Path_)).Entries);
    }

    [Fact]
    public void TamperedFileFailsClosedInsteadOfLoadingOrResetting()
    {
        var vault = new TokenVault(new DpapiStorage(Path_));
        vault.ImportBackup(Fixture("plaintext-v4"));

        var stored = File.ReadAllBytes(Path_);
        stored[^1] ^= 1;
        File.WriteAllBytes(Path_, stored);

        // DPAPI authenticates the blob, so a damaged vault never reaches the decoder.
        Assert.Throws<CryptographicException>(() => new TokenVault(new DpapiStorage(Path_)));
        Assert.Equal(stored, File.ReadAllBytes(Path_));
    }

    [Fact]
    public void WritesReplaceTheFileWithoutLeavingStagingBehind()
    {
        var storage = new DpapiStorage(Path_);
        storage.Write([1, 2, 3]);
        storage.Write([4, 5, 6, 7]);

        Assert.Equal([4, 5, 6, 7], storage.Read());
        Assert.Equal(["vault.v1.dat"], Directory.GetFiles(_directory).Select(System.IO.Path.GetFileName));
    }
}
