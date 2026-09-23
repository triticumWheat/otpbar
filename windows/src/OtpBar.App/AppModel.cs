using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using OtpBar.Core;

namespace OtpBar.App;

public sealed class AppModel : INotifyPropertyChanged
{
    private readonly SecureClipboard _clipboard = new();
    private readonly DispatcherTimer _clipboardTimer = new();
    private TokenVault? _vault;

    private IReadOnlyList<OtpEntry> _entries = [];
    private string? _loadError;
    private string? _message;
    private Guid? _selectedId;

    public AppModel(IVaultStorage? storage = null)
    {
        _clipboardTimer.Tick += (_, _) => ClearCopiedCode();
        try
        {
            _vault = new TokenVault(storage ?? new DpapiStorage());
            _entries = _vault.Entries;
            _selectedId = _entries.FirstOrDefault()?.Id;
        }
        catch (Exception error)
        {
            // The vault refuses to write over data it could not read; the interface must say so rather than look empty.
            LoadError = error is OtpException ? error.Message : "本地数据无法读取；为保护原数据，已停止写入。";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<OtpEntry> Entries
    {
        get => _entries;
        private set => Set(ref _entries, value);
    }

    public string? LoadError
    {
        get => _loadError;
        private set => Set(ref _loadError, value);
    }

    public string? Message
    {
        get => _message;
        set => Set(ref _message, value);
    }

    public Guid? SelectedId
    {
        get => _selectedId;
        set
        {
            if (Set(ref _selectedId, value))
            {
                OnPropertyChanged(nameof(SelectedEntry));
            }
        }
    }

    public OtpEntry? SelectedEntry => _entries.FirstOrDefault(entry => entry.Id == _selectedId);

    public bool IsEditing { get; set; }

    public ImportResult ImportBackup(byte[] data, string? password)
    {
        var vault = _vault ?? throw OtpException.InvalidVault();
        var result = vault.ImportBackup(data, password);
        Entries = vault.Entries;
        SelectedId ??= Entries.FirstOrDefault()?.Id;
        Message = $"导入完成：新增 {result.Added} 个，跳过 {result.Skipped} 个重复账号。";
        return result;
    }

    public void Update(OtpEntry entry)
    {
        var vault = _vault ?? throw OtpException.InvalidVault();
        vault.Update(entry);
        Entries = vault.Entries;
        IsEditing = false;
        OnPropertyChanged(nameof(SelectedEntry));
        Message = "修改已保存。";
    }

    public void Remove(OtpEntry entry)
    {
        var vault = _vault ?? throw OtpException.InvalidVault();
        vault.Remove(entry.Id);
        Entries = vault.Entries;
        SelectedId = Entries.FirstOrDefault()?.Id;
        Message = $"已从这台电脑删除“{entry.Name}”。";
    }

    public void Copy(OtpEntry entry)
    {
        var vault = _vault;
        if (vault is null)
        {
            Message = OtpException.InvalidVault().Message;
            return;
        }
        OtpCode code;
        try
        {
            code = vault.Code(entry.Id);
        }
        catch (OtpException error)
        {
            Message = error.Message;
            return;
        }

        _clipboardTimer.Stop();
        if (!_clipboard.TryCopy(code.Value))
        {
            Message = "无法写入剪贴板，请重试。";
            return;
        }
        // Take the code back the moment it stops being valid.
        _clipboardTimer.Interval = TimeSpan.FromSeconds(
            Math.Max(0.01, (code.ValidUntil - DateTimeOffset.UtcNow).TotalSeconds));
        _clipboardTimer.Start();
        Message = $"已复制 {entry.Name} 的验证码。";
    }

    public void ClearCopiedCode()
    {
        _clipboardTimer.Stop();
        _clipboard.ClearIfUnchanged();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Splits a code down the middle, the way authenticator apps present it.</summary>
    public static string GroupedCode(string code) =>
        code[..(code.Length / 2)] + " " + code[(code.Length / 2)..];
}
