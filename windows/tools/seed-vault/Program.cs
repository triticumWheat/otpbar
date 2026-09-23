// One-time: imports a .2fas backup into a vault file so the interface can be exercised
// without touching the real vault. Usage: seed-vault <backup> <vault-path> [password]
using OtpBar.Core;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: seed-vault <backup> <vault-path> [password]");
    return 1;
}

var vault = new TokenVault(new DpapiStorage(args[1]));
var result = vault.ImportBackup(File.ReadAllBytes(args[0]), args.Length > 2 ? args[2] : null);
Console.WriteLine($"added={result.Added} skipped={result.Skipped} total={vault.Entries.Count}");
foreach (var entry in vault.Entries)
{
    Console.WriteLine($"  {entry.Algorithm.ToName()} {entry.Digits}/{entry.Period}s  {entry.Account}");
}
return 0;
