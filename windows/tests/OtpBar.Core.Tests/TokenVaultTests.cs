using System.Text.Json.Nodes;
using OtpBar.Core;
using static OtpBarTests.TestSupport;

namespace OtpBarTests;

public class TokenVaultTests
{
    private const string AsciiPassword = "otpbar-test-only";

    [Fact]
    public void ImportAndReloadPreserveIdentityAndData()
    {
        var storage = new MemoryStorage();
        var vault = new TokenVault(storage);
        Assert.Empty(vault.Entries);

        var result = vault.ImportBackup(Fixture("plaintext-v4"));
        Assert.Equal(2, result.Added);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, storage.Writes);
        Assert.Equal(vault.Entries, new TokenVault(storage).Entries);
    }

    [Fact]
    public void DuplicateImportsPreserveLocalEdits()
    {
        var storage = new MemoryStorage();
        var vault = new TokenVault(storage);
        vault.ImportBackup(Fixture("plaintext-v4"));

        var original = vault.Entries[0];
        var edited = new OtpEntry("Local name", original.Secret, id: original.Id, account: "local@example.invalid");
        vault.Update(edited);

        var writesBefore = storage.Writes;
        var result = vault.ImportBackup(Fixture("encrypted-ascii-v4"), AsciiPassword);
        Assert.Equal(0, result.Added);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(writesBefore, storage.Writes);
        Assert.Equal(edited, vault.Entries[0]);
    }

    [Fact]
    public void DuplicatesWithinBackupAreSkippedButDifferentParametersSurvive()
    {
        var data = ModifiedBackup(json =>
        {
            var services = json["services"]!.AsArray();
            var duplicate = services[1]!.DeepClone().AsObject();
            duplicate["secret"] = PublicTestSecret.ToLowerInvariant() + "\n";
            var variant = duplicate.DeepClone().AsObject();
            variant["otp"]!["period"] = 60;
            services.Add(duplicate);
            services.Add(variant);
        });

        var result = new TokenVault(new MemoryStorage()).ImportBackup(data);
        Assert.Equal(3, result.Added);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void OneBadEntryRejectsWholeImport()
    {
        var storage = new MemoryStorage();
        var vault = new TokenVault(storage);
        // Second in display order: first must not be written either.
        var corrupt = ModifiedBackup(json => json["services"]![0]!["secret"] = "invalid!");

        Throws(OtpErrorKind.InvalidSecret, () => vault.ImportBackup(corrupt));
        Assert.Equal(0, storage.Writes);
        Assert.Empty(vault.Entries);
        Assert.Null(storage.Data);
    }

    [Fact]
    public void FailedWritesLeaveMemoryAndPersistedDataUnchanged()
    {
        var storage = new MemoryStorage();
        var vault = new TokenVault(storage);

        storage.FailWrite = true;
        Assert.Throws<TestFailureException>(() => vault.ImportBackup(Fixture("plaintext-v4")));
        Assert.Empty(vault.Entries);
        Assert.Null(storage.Data);

        storage.FailWrite = false;
        vault.ImportBackup(Fixture("plaintext-v4"));
        var before = vault.Entries.ToList();
        var bytesBefore = storage.Data;

        storage.FailWrite = true;
        var changed = new OtpEntry("Changed", "MZXW6YTBOI", id: before[0].Id);
        Assert.Throws<TestFailureException>(() => vault.Update(changed));
        Assert.Equal(before, vault.Entries);
        Assert.Equal(bytesBefore, storage.Data);

        storage.FailWrite = false;
        Assert.Equal(before, new TokenVault(storage).Entries);
    }

    [Fact]
    public void UnreadableVaultNeverBecomesWritableEmptyVault()
    {
        var storage = new MemoryStorage { FailRead = true };
        Assert.Throws<TestFailureException>(() => new TokenVault(storage));

        storage.FailRead = false;
        foreach (var invalid in new[] { "broken", "{}", "{\"schemaVersion\":2,\"entries\":[]}" })
        {
            storage.Data = System.Text.Encoding.UTF8.GetBytes(invalid);
            Throws(OtpErrorKind.InvalidVault, () => new TokenVault(storage));
        }
        Assert.Equal(0, storage.Writes);
    }

    [Fact]
    public void PersistedEntriesAreValidatedOnRead()
    {
        var storage = new MemoryStorage();
        var vault = new TokenVault(storage);
        vault.ImportBackup(Fixture("plaintext-v4"));
        var original = JsonNode.Parse(storage.Data!)!.AsObject();

        foreach (var field in new[] { "digits", "period", "secret" })
        {
            var json = original.DeepClone().AsObject();
            json["entries"]![0]![field] = field == "secret" ? "invalid!" : JsonValue.Create(0);
            storage.Data = json.ToBytes();
            Throws(OtpErrorKind.InvalidVault, () => new TokenVault(storage));
        }

        var duplicated = original.DeepClone().AsObject();
        var entries = duplicated["entries"]!.AsArray();
        foreach (var entry in entries.ToList())
        {
            entries.Add(entry!.DeepClone());
        }
        storage.Data = duplicated.ToBytes();
        Throws(OtpErrorKind.InvalidVault, () => new TokenVault(storage));
    }

    [Fact]
    public void EditsValidateIdentityAndAvoidGeneratorCollision()
    {
        var storage = new MemoryStorage();
        var vault = new TokenVault(storage);
        vault.ImportBackup(Fixture("plaintext-v4"));

        var other = vault.Entries[1];
        var collision = new OtpEntry("Collision", other.Secret, id: vault.Entries[0].Id,
            algorithm: other.Algorithm, digits: other.Digits, period: other.Period);
        Throws(OtpErrorKind.DuplicateEntry, () => vault.Update(collision));
        Throws(OtpErrorKind.EntryNotFound, () => vault.Update(new OtpEntry("Unknown", PublicTestSecret)));
        Assert.Equal(1, storage.Writes);
    }

    [Fact]
    public void LookupRecomputesCodeAtRollover()
    {
        var vault = new TokenVault(new MemoryStorage());
        vault.ImportBackup(Fixture("plaintext-v4"));
        var id = vault.Entries[0].Id;

        // RFC 4226 counters 0 and 1: a click after rollover must return the new code.
        var displayed = vault.Code(id, 29.999);
        var clicked = vault.Code(id, 30d);
        Assert.Equal("755224", displayed.Value);
        Assert.Equal("287082", clicked.Value);
        Assert.Equal(30, displayed.ValidUntilUnixSeconds);
    }

    [Fact]
    public void DeletionPersistsAndPreservesOtherAccounts()
    {
        var storage = new MemoryStorage();
        var vault = new TokenVault(storage);
        vault.ImportBackup(Fixture("plaintext-v4"));

        var removed = vault.Entries[0].Id;
        var retained = vault.Entries[1];
        vault.Remove(removed);
        Assert.Equal([retained], vault.Entries);
        Assert.Equal([retained], new TokenVault(storage).Entries);
        Throws(OtpErrorKind.EntryNotFound, () => vault.Code(removed));

        vault.Remove(retained.Id);
        Assert.Empty(new TokenVault(storage).Entries);

        var result = vault.ImportBackup(Fixture("plaintext-v4"));
        Assert.Equal(2, result.Added);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void FailedOrStaleDeletionDoesNotChangeTheVault()
    {
        var storage = new MemoryStorage();
        var vault = new TokenVault(storage);
        vault.ImportBackup(Fixture("plaintext-v4"));

        var before = vault.Entries.ToList();
        var bytesBefore = storage.Data;
        var writesBefore = storage.Writes;

        storage.FailWrite = true;
        Assert.Throws<TestFailureException>(() => vault.Remove(before[0].Id));
        Assert.Equal(before, vault.Entries);
        Assert.Equal(bytesBefore, storage.Data);

        storage.FailWrite = false;
        Throws(OtpErrorKind.EntryNotFound, () => vault.Remove(Guid.NewGuid()));
        Assert.Equal(writesBefore, storage.Writes);
        Assert.Equal(before, new TokenVault(storage).Entries);
    }

    [Fact]
    public void MatchingNamesWithDifferentSecretsAreNotDuplicates()
    {
        var data = ModifiedBackup(json =>
        {
            var services = json["services"]!.AsArray();
            services[1]!["name"] = services[0]!["name"]!.DeepClone();
            services[1]!["otp"] = services[0]!["otp"]!.DeepClone();
        });

        var vault = new TokenVault(new MemoryStorage());
        var firstImport = vault.ImportBackup(data);
        Assert.Equal(2, firstImport.Added);
        Assert.Equal(0, firstImport.Skipped);
        Assert.Equal(vault.Entries[0].Name, vault.Entries[1].Name);

        var secondImport = vault.ImportBackup(data);
        Assert.Equal(0, secondImport.Added);
        Assert.Equal(2, secondImport.Skipped);
    }
}
