using System.Text.Json;

namespace OtpBar.Core;

public sealed class TokenVault
{
    private const int CurrentSchemaVersion = 1;

    private readonly IVaultStorage _storage;
    private List<OtpEntry> _entries;

    public IReadOnlyList<OtpEntry> Entries => _entries;

    public TokenVault(IVaultStorage storage)
    {
        _storage = storage;
        // A failed read must never create an empty vault that can overwrite existing data.
        var data = storage.Read();
        _entries = data is null ? [] : Decode(data);
    }

    public ImportResult ImportBackup(byte[] data, string? password = null)
    {
        var incoming = TwoFasImporter.Read(data, password);
        var next = new List<OtpEntry>(_entries);
        var skipped = 0;
        foreach (var entry in incoming)
        {
            if (next.Any(existing => existing.HasSameGenerator(entry)))
            {
                skipped++;
            }
            else
            {
                next.Add(entry);
            }
        }
        var added = next.Count - _entries.Count;
        if (added > 0)
        {
            Save(next);
        }
        return new ImportResult(added, skipped);
    }

    public void Update(OtpEntry entry)
    {
        var index = _entries.FindIndex(existing => existing.Id == entry.Id);
        if (index < 0)
        {
            throw OtpException.EntryNotFound();
        }
        if (_entries.Any(existing => existing.Id != entry.Id && existing.HasSameGenerator(entry)))
        {
            throw OtpException.DuplicateEntry();
        }
        var next = new List<OtpEntry>(_entries);
        next[index] = entry;
        Save(next);
    }

    public void Remove(Guid id)
    {
        if (_entries.All(entry => entry.Id != id))
        {
            throw OtpException.EntryNotFound();
        }
        Save(_entries.Where(entry => entry.Id != id).ToList());
    }

    public OtpCode Code(Guid id, DateTimeOffset at) =>
        Totp.Generate(Find(id), at);

    public OtpCode Code(Guid id, double unixSeconds) =>
        Totp.Generate(Find(id), unixSeconds);

    public OtpCode Code(Guid id) => Code(id, DateTimeOffset.UtcNow);

    private OtpEntry Find(Guid id) =>
        _entries.FirstOrDefault(entry => entry.Id == id) ?? throw OtpException.EntryNotFound();

    private void Save(List<OtpEntry> next)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(
            new VaultJson.SavedVault(CurrentSchemaVersion, next), VaultJson.Options);
        _storage.Write(data);
        _entries = next;
    }

    private static List<OtpEntry> Decode(byte[] data)
    {
        VaultJson.SavedVault? saved;
        try
        {
            saved = JsonSerializer.Deserialize<VaultJson.SavedVault>(data, VaultJson.Options);
        }
        catch (Exception)
        {
            // Anything unreadable stays unreadable: the interface refuses to write rather than silently reset.
            throw OtpException.InvalidVault();
        }
        if (saved?.Entries is not { } entries || saved.SchemaVersion != CurrentSchemaVersion)
        {
            throw OtpException.InvalidVault();
        }
        if (entries.Select(entry => entry.Id).Distinct().Count() != entries.Count)
        {
            throw OtpException.InvalidVault();
        }
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries.Take(index).Any(earlier => earlier.HasSameGenerator(entries[index])))
            {
                throw OtpException.InvalidVault();
            }
        }
        return entries.ToList();
    }
}
