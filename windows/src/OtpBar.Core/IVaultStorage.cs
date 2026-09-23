namespace OtpBar.Core;

public interface IVaultStorage
{
    /// <summary>Returns null when nothing has been stored yet. Any other failure must throw, never return null.</summary>
    byte[]? Read();

    void Write(byte[] data);
}

public readonly record struct ImportResult(int Added, int Skipped);
