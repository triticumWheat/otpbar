using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OtpBar.Core;

namespace OtpBar.App;

public partial class SettingsWindow : Window
{
    private readonly AppModel _model;
    private bool _suppressFieldEvents;

    public SettingsWindow(AppModel model)
    {
        InitializeComponent();
        _model = model;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
            new Uri("pack://application:,,,/Assets/otpbar.ico"));

        _model.PropertyChanged += OnModelChanged;
        ReloadAccounts();
        ShowStatus(_model.LoadError ?? _model.Message);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppModel.Entries):
                ReloadAccounts();
                break;
            case nameof(AppModel.Message):
                ShowStatus(_model.Message);
                break;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_model.IsEditing)
        {
            e.Cancel = true;
            ShowStatus("请先保存或撤销当前修改。");
            return;
        }
        _model.PropertyChanged -= OnModelChanged;
        base.OnClosing(e);
    }

    private void ReloadAccounts()
    {
        _suppressFieldEvents = true;
        AccountList.ItemsSource = _model.Entries;
        AccountList.SelectedValue = _model.SelectedId;
        if (AccountList.SelectedItem is null)
        {
            AccountList.SelectedIndex = _model.Entries.Count > 0 ? 0 : -1;
        }
        _suppressFieldEvents = false;
        LoadDetail();
    }

    private void LoadDetail()
    {
        _suppressFieldEvents = true;
        var entry = _model.SelectedEntry;
        Detail.IsEnabled = entry is not null;
        if (entry is null)
        {
            DetailTitle.Text = _model.LoadError is not null ? "无法读取本地数据" : "还没有账号";
            NameBox.Text = AccountBox.Text = SecretText.Text = ParametersText.Text = "";
        }
        else
        {
            DetailTitle.Text = entry.Name;
            NameBox.Text = entry.Name;
            AccountBox.Text = entry.Account;
            ParametersText.Text = $"{entry.Algorithm.ToName()} · {entry.Digits} 位 · 每 {entry.Period} 秒更新";
            ShowSecret(entry);
        }
        _suppressFieldEvents = false;
        SetEditing(false);
    }

    private void ShowSecret(OtpEntry entry) =>
        SecretText.Text = RevealSecret.IsChecked == true ? entry.Secret : new string('•', Math.Min(32, entry.Secret.Length));

    private void SetEditing(bool editing)
    {
        _model.IsEditing = editing;
        SaveButton.IsEnabled = editing;
        RevertButton.IsEnabled = editing;
    }

    private void ShowStatus(string? message) => MessageText.Text = message ?? "";

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFieldEvents)
        {
            return;
        }
        if (_model.IsEditing)
        {
            _suppressFieldEvents = true;
            AccountList.SelectedValue = _model.SelectedId;
            _suppressFieldEvents = false;
            ShowStatus("请先保存或撤销当前修改。");
            return;
        }
        _model.SelectedId = (AccountList.SelectedItem as OtpEntry)?.Id;
        LoadDetail();
    }

    private void OnFieldChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFieldEvents || _model.SelectedEntry is not { } entry)
        {
            return;
        }
        SetEditing(NameBox.Text != entry.Name || AccountBox.Text != entry.Account);
    }

    private void OnRevealChanged(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedEntry is { } entry)
        {
            ShowSecret(entry);
        }
    }

    private void OnRevertClicked(object sender, RoutedEventArgs e) => LoadDetail();

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedEntry is not { } entry)
        {
            return;
        }
        try
        {
            _model.Update(new OtpEntry(NameBox.Text, entry.Secret, id: entry.Id, account: AccountBox.Text,
                algorithm: entry.Algorithm, digits: entry.Digits, period: entry.Period));
        }
        catch (OtpException error)
        {
            ShowStatus(error.Message);
            return;
        }
        LoadDetail();
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (_model.SelectedEntry is not { } entry)
        {
            return;
        }
        var confirmed = MessageBox.Show(
            this,
            $"从这台电脑删除“{entry.Name}”？手机上的账号不受影响，需要时可以重新导入备份。",
            "删除账号", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (confirmed != MessageBoxResult.OK)
        {
            return;
        }
        SetEditing(false);
        try
        {
            _model.Remove(entry);
        }
        catch (OtpException error)
        {
            ShowStatus(error.Message);
        }
    }

    private void OnImportClicked(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "选择 2FAS 备份",
            Filter = "2FAS 备份 (*.2fas)|*.2fas|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*"
        };
        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        var data = File.ReadAllBytes(picker.FileName);
        string? password = null;
        while (true)
        {
            try
            {
                _model.ImportBackup(data, password);
                return;
            }
            catch (OtpException error)
                when (error.Kind is OtpErrorKind.PasswordRequired or OtpErrorKind.DecryptionFailed)
            {
                // The backup is password protected, or the password just entered was wrong.
                password = PasswordWindow.Ask(this, error.Message);
                if (password is null)
                {
                    ShowStatus("已取消导入。");
                    return;
                }
            }
            catch (OtpException error)
            {
                ShowStatus(error.Message);
                return;
            }
        }
    }
}
