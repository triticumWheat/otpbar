using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using OtpBar.Core;

namespace OtpBar.App;

public partial class AddAccountWindow : Window
{
    private readonly DispatcherTimer _preview = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _suppressFieldEvents;

    private AddAccountWindow()
    {
        InitializeComponent();
        // Filling one box raises SelectionChanged while the others are still empty.
        _suppressFieldEvents = true;
        AlgorithmBox.ItemsSource = new[] { "SHA1", "SHA256", "SHA512" };
        AlgorithmBox.SelectedIndex = 0;
        DigitsBox.ItemsSource = new[] { 5, 6, 7, 8 };
        DigitsBox.SelectedItem = 6;
        PeriodBox.ItemsSource = new[] { 10, 30, 60, 90 };
        PeriodBox.SelectedItem = 30;
        _suppressFieldEvents = false;
        _preview.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { _preview.Start(); Refresh(); };
        Closed += (_, _) => _preview.Stop();
    }

    /// <summary>The account to add, or null when the user cancelled.</summary>
    public OtpEntry? Entry { get; private set; }

    public static OtpEntry? Prompt(Window owner)
    {
        var window = new AddAccountWindow { Owner = owner };
        window.ShowDialog();
        return window.Entry;
    }

    private void Fill(OtpEntry entry)
    {
        _suppressFieldEvents = true;
        NameBox.Text = entry.Name;
        AccountBox.Text = entry.Account;
        SecretBox.Text = entry.Secret;
        AlgorithmBox.SelectedItem = entry.Algorithm.ToName();
        DigitsBox.SelectedItem = entry.Digits;
        PeriodBox.SelectedItem = entry.Period;
        _suppressFieldEvents = false;
        Refresh();
    }

    /// <summary>Builds an entry from the fields, or reports why it cannot be built yet.</summary>
    private OtpEntry? Current(out string? problem)
    {
        problem = null;
        try
        {
            OtpAlgorithmNames.TryParse(AlgorithmBox.SelectedItem as string ?? "SHA1", out var algorithm);
            return new OtpEntry(
                NameBox.Text, SecretBox.Text,
                account: AccountBox.Text,
                algorithm: algorithm,
                digits: DigitsBox.SelectedItem as int? ?? 6,
                period: PeriodBox.SelectedItem as int? ?? 30);
        }
        catch (OtpException error)
        {
            // Half-typed input is the normal state of this window, not an error worth shouting about.
            problem = error.Message;
            return null;
        }
    }

    private void Refresh()
    {
        var entry = Current(out var problem);
        SaveButton.IsEnabled = entry is not null;
        if (entry is null)
        {
            PreviewCode.Text = "—";
            PreviewRemaining.Text = "";
            MessageText.Text = SecretBox.Text.Trim().Length == 0 ? "" : problem ?? "";
            return;
        }
        MessageText.Text = "";
        var now = DateTimeOffset.UtcNow;
        var code = Totp.Generate(entry, now);
        PreviewCode.Text = AppModel.GroupedCode(code.Value);
        PreviewRemaining.Text = $"{Math.Max(0, Math.Ceiling((code.ValidUntil - now).TotalSeconds)):0} 秒";
    }

    private void OnFieldChanged(object sender, RoutedEventArgs e)
    {
        if (!_suppressFieldEvents)
        {
            Refresh();
        }
    }

    private void OnSecretChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFieldEvents)
        {
            return;
        }
        // Pasting a whole link into the key box fills in everything it carries.
        if (OtpAuthUri.IsOtpAuthUri(SecretBox.Text))
        {
            Accept(SecretBox.Text);
            return;
        }
        Refresh();
    }

    private void Accept(string text)
    {
        try
        {
            Fill(OtpAuthUri.Parse(text));
        }
        catch (OtpException error)
        {
            MessageText.Text = error.Message;
        }
    }

    private async void OnScanScreen(object sender, RoutedEventArgs e)
    {
        MessageText.Text = "";
        // Moved aside rather than hidden: Hide() on a window opened with ShowDialog ends its modal
        // state, and every later attempt to close it through DialogResult then throws.
        var owner = Owner;
        var restore = (Left, Top, OwnerLeft: owner?.Left ?? 0, OwnerTop: owner?.Top ?? 0);
        Move(this, OffScreen, OffScreen);
        Move(owner, OffScreen, OffScreen);
        // Let the desktop repaint without this app over the top of the code.
        await Task.Delay(180);

        var overlay = ScanOverlayWindow.Prompt();

        Move(this, restore.Left, restore.Top);
        Move(owner, restore.OwnerLeft, restore.OwnerTop);
        Activate();

        if (overlay.Result is { } text)
        {
            Accept(text);
        }
        else if (overlay.Error is { } error)
        {
            MessageText.Text = error;
        }
    }

    private void OnScanFile(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "选择含二维码的图片",
            Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件 (*.*)|*.*"
        };
        if (picker.ShowDialog(this) != true)
        {
            return;
        }
        try
        {
            Accept(QrScanner.DecodeFile(picker.FileName));
        }
        catch (OtpException error)
        {
            MessageText.Text = error.Message;
        }
    }

    private void OnPasteLink(object sender, RoutedEventArgs e)
    {
        MessageText.Text = "";
        if (Clipboard.ContainsText() && OtpAuthUri.IsOtpAuthUri(Clipboard.GetText()))
        {
            Accept(Clipboard.GetText());
            return;
        }
        if (Clipboard.ContainsImage())
        {
            try
            {
                // ContainsImage can still be followed by a null, for instance when the owning app closes.
                var image = Clipboard.GetImage();
                MessageText.Text = image is null ? "剪贴板里的图片读不出来。" : "";
                if (image is not null)
                {
                    Accept(QrScanner.Decode(image));
                }
            }
            catch (OtpException error)
            {
                MessageText.Text = error.Message;
            }
            // A screenshot of a code stays in the Windows clipboard history, which clearing does not reach.
            MessageText.Text += MessageText.Text.Length > 0 ? " " : "";
            MessageText.Text += "剪贴板里的二维码截图会留在 Win+V 历史中，建议改用「扫描屏幕上的二维码」。";
            return;
        }
        MessageText.Text = "剪贴板里既没有 otpauth:// 链接，也没有图片。";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Entry = Current(out var problem);
        if (Entry is null)
        {
            MessageText.Text = problem ?? "";
            return;
        }
        // Closing rather than setting DialogResult: the caller reads Entry, and DialogResult is only
        // legal while the window is still modal, which a scan used to be able to undo.
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Entry = null;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Entry = null;
            Close();
        }
        base.OnKeyDown(e);
    }

    /// <summary>Far outside any desktop, so the window keeps its state without appearing in a capture.</summary>
    private const double OffScreen = -32000;

    private static void Move(Window? window, double left, double top)
    {
        if (window is not null)
        {
            window.Left = left;
            window.Top = top;
        }
    }
}
