using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OtpBar.Core;

namespace OtpBar.App;

public partial class CodePopupWindow : Window
{
    private readonly AppModel _model;
    private readonly Action _showSettings;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly ObservableCollection<CodeRow> _rows = [];

    public CodePopupWindow(AppModel model, Action showSettings)
    {
        InitializeComponent();
        _model = model;
        _showSettings = showSettings;
        Rows.ItemsSource = _rows;
        _timer.Tick += (_, _) => Refresh();
        Deactivated += (_, _) => Hide();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        };
    }

    public void ShowNearTray()
    {
        Rebuild();
        Refresh();
        Show();
        UpdateLayout();

        // The tray sits at the near corner of the work area on every taskbar edge Windows supports.
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left + 12, area.Right - ActualWidth - 12);
        Top = Math.Max(area.Top + 12, area.Bottom - ActualHeight - 12);
        Activate();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
        }
        base.OnPreviewKeyDown(e);
    }

    private void Rebuild()
    {
        _rows.Clear();
        foreach (var entry in _model.Entries)
        {
            _rows.Add(new CodeRow(entry));
        }

        var notice = _model.LoadError is not null
            ? "无法读取本地数据，请打开设置"
            : _model.Entries.Count == 0 ? "还没有验证码，请在设置中导入备份。" : null;
        EmptyNotice.Text = notice ?? "";
        EmptyNotice.Visibility = notice is null ? Visibility.Collapsed : Visibility.Visible;
        Scroller.Visibility = notice is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var row in _rows)
        {
            row.Refresh(now);
        }
    }

    private void OnRowClicked(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is CodeRow row)
        {
            _model.Copy(row.Entry);
            Hide();
        }
    }

    private void OnSettingsClicked(object sender, MouseButtonEventArgs e)
    {
        Hide();
        _showSettings();
    }
}

public sealed class CodeRow(OtpEntry entry) : INotifyPropertyChanged
{
    private string _code = "";
    private string _remaining = "";

    public OtpEntry Entry { get; } = entry;

    public string Code
    {
        get => _code;
        private set => Set(ref _code, value);
    }

    public string Remaining
    {
        get => _remaining;
        private set => Set(ref _remaining, value);
    }

    public void Refresh(DateTimeOffset now)
    {
        try
        {
            var code = Totp.Generate(Entry, now);
            Code = AppModel.GroupedCode(code.Value);
            Remaining = $"{Math.Max(0, Math.Ceiling((code.ValidUntil - now).TotalSeconds)):0} 秒";
        }
        catch (OtpException)
        {
            // Only a system clock outside the representable range reaches here.
            Code = "时间无效";
            Remaining = "";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value)
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
