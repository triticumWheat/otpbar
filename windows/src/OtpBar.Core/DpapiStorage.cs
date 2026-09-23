using System.Security.Cryptography;
using System.Text;

namespace OtpBar.Core;

/// <summary>
/// Stores the vault under the current Windows account using DPAPI, the platform equivalent of a per-user keychain.
/// Another Windows user, or the same file copied to another machine, cannot decrypt it.
/// </summary>
public sealed class DpapiStorage : IVaultStorage
{
    // Entropy is a domain separator, not a secret: it stops an unrelated blob from being decrypted as a vault.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("otpbar-vault-v1");

    private readonly string _path;

    public DpapiStorage(string? path = null)
    {
        _path = path ?? DefaultPath;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OTPBar", "vault.v1.dat");

    public byte[]? Read()
    {
        if (!File.Exists(_path))
        {
            return null;
        }
        return ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
    }

    public void Write(byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var sealedBytes = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
        // Write beside the vault and swap, so an interrupted write cannot truncate a good file.
        var staging = _path + ".tmp";
        File.WriteAllBytes(staging, sealedBytes);
        File.Move(staging, _path, overwrite: true);
    }
}
