namespace OtpBar.Core;

public enum OtpErrorKind
{
    InvalidName,
    InvalidSecret,
    InvalidParameters,
    InvalidTime,
    InvalidBackup,
    UnsupportedSchema,
    UnsupportedToken,
    PasswordRequired,
    DecryptionFailed,
    InvalidVault,
    EntryNotFound,
    DuplicateEntry
}

/// <summary>Every failure the vault surfaces to the interface, with a message safe to display.</summary>
public sealed class OtpException : Exception
{
    public OtpErrorKind Kind { get; }

    /// <summary>Set only for <see cref="OtpErrorKind.UnsupportedSchema"/>.</summary>
    public long? SchemaVersion { get; }

    private OtpException(OtpErrorKind kind, string message, long? schemaVersion = null) : base(message)
    {
        Kind = kind;
        SchemaVersion = schemaVersion;
    }

    public static OtpException InvalidName() =>
        new(OtpErrorKind.InvalidName, "请输入账号名称。");

    public static OtpException InvalidSecret() =>
        new(OtpErrorKind.InvalidSecret, "密钥不是有效的 Base32 字符串。");

    public static OtpException InvalidParameters() =>
        new(OtpErrorKind.InvalidParameters, "支持 5–8 位验证码，周期为 10、30、60 或 90 秒。");

    public static OtpException InvalidTime() =>
        new(OtpErrorKind.InvalidTime, "系统时间无效，请检查电脑的日期与时间设置。");

    public static OtpException InvalidBackup() =>
        new(OtpErrorKind.InvalidBackup, "备份格式无效或已损坏，未导入任何条目。");

    public static OtpException UnsupportedSchema(long version) =>
        new(OtpErrorKind.UnsupportedSchema, $"暂不支持备份版本 {version}，请用最新版 2FAS 重新导出。", version);

    public static OtpException UnsupportedToken() =>
        new(OtpErrorKind.UnsupportedToken, "备份包含暂不支持的验证码类型或算法，未导入任何条目。");

    public static OtpException PasswordRequired() =>
        new(OtpErrorKind.PasswordRequired, "请输入导出备份时设置的密码。");

    public static OtpException DecryptionFailed() =>
        new(OtpErrorKind.DecryptionFailed, "密码不正确或备份已损坏。");

    public static OtpException InvalidVault() =>
        new(OtpErrorKind.InvalidVault, "本地数据无法读取；为保护原数据，已停止写入。");

    public static OtpException EntryNotFound() =>
        new(OtpErrorKind.EntryNotFound, "未找到该条目，请重新打开设置。");

    public static OtpException DuplicateEntry() =>
        new(OtpErrorKind.DuplicateEntry, "已存在使用相同密钥和生成参数的条目。");
}
