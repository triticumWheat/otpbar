using System.Text.Json;
using System.Text.Json.Nodes;
using OtpBar.Core;

namespace OtpBarTests;

internal static class TestSupport
{
    public const string PublicTestSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    public static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".json"));

    public static JsonObject FixtureObject(string name) =>
        JsonNode.Parse(Fixture(name))!.AsObject();

    public static byte[] ToBytes(this JsonNode node) =>
        JsonSerializer.SerializeToUtf8Bytes(node);

    public static byte[] ModifiedBackup(Action<JsonObject> modify)
    {
        var json = FixtureObject("plaintext-v4");
        modify(json);
        return json.ToBytes();
    }

    public static OtpException Throws(OtpErrorKind kind, Action action)
    {
        var error = Assert.Throws<OtpException>(action);
        Assert.Equal(kind, error.Kind);
        return error;
    }
}

internal sealed class TestFailureException(bool isRead) : Exception
{
    public bool IsRead { get; } = isRead;
}

internal sealed class MemoryStorage : IVaultStorage
{
    public byte[]? Data { get; set; }
    public bool FailRead { get; set; }
    public bool FailWrite { get; set; }
    public int Writes { get; private set; }

    public byte[]? Read()
    {
        if (FailRead)
        {
            throw new TestFailureException(isRead: true);
        }
        return Data;
    }

    public void Write(byte[] data)
    {
        if (FailWrite)
        {
            throw new TestFailureException(isRead: false);
        }
        Data = data;
        Writes++;
    }
}
