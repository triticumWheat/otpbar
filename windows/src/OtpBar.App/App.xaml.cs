using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using OtpBar.Core;
using Forms = System.Windows.Forms;

namespace OtpBar.App;

public partial class App : Application
{
    private static Mutex? _onlyInstance;

    private AppModel _model = null!;
    private Forms.NotifyIcon _trayIcon = null!;
    private CodePopupWindow? _popup;
    private SettingsWindow? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Theme.Apply(Resources);

        // Without this the process disappears on an unexpected failure and the user is left guessing
        // whether what they just entered was saved. The vault only updates itself after a successful
        // write, so carrying on after a report leaves memory and disk agreeing.
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            var written = Record(args.Exception);
            MessageBox.Show(
                $"刚才的操作没有完成，数据没有改动。\n\n" +
                $"{args.Exception.GetType().Name}: {args.Exception.Message}\n\n" +
                (written is null ? "" : $"详细信息已写入\n{written}"),
                "OTPBar", MessageBoxButton.OK, MessageBoxImage.Warning);
        };

        // --vault <path> runs against a throwaway vault, --popup/--settings open a window without the tray.
        var vaultPath = ArgumentValue(e.Args, "--vault");

        // One copy per vault. A second would add a duplicate tray icon and race the first over the file.
        var key = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes((vaultPath ?? DpapiStorage.DefaultPath).ToLowerInvariant())))[..16];
        _onlyInstance = new Mutex(initiallyOwned: true, $@"Local\OTPBar.{key}", out var isOnlyInstance);
        if (!isOnlyInstance)
        {
            Shutdown();
            return;
        }

        _model = new AppModel(vaultPath is null ? null : new DpapiStorage(vaultPath));

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "OTPBar — 左键查看验证码，右键打开设置",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _trayIcon.MouseUp += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                TogglePopup();
            }
        };
        _trayIcon.DoubleClick += (_, _) => ShowSettings();

        if (e.Args.Contains("--popup"))
        {
            TogglePopup();
        }
        else if (e.Args.Contains("--settings") || _model.Entries.Count == 0 || _model.LoadError is not null)
        {
            ShowSettings();
        }
    }

    /// <summary>
    /// Keeps the full stack next to the vault so a failure can be diagnosed after the fact.
    /// Returns where it went, or null when even that did not work. Entries and codes are never written.
    /// </summary>
    private static string? Record(Exception error)
    {
        var path = Path.Combine(Path.GetDirectoryName(DpapiStorage.DefaultPath)!, "error.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\n{error}\n\n");
            return path;
        }
        catch (Exception)
        {
            // Reporting must never replace the failure it is reporting.
            return null;
        }
    }

    private static string? ArgumentValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Also reached by the duplicate-instance path, before anything has been built.
        _model?.ClearCopiedCode();
        if (_trayIcon is not null)
        {
            // Skipping this is what leaves a dead icon sitting in the notification area.
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.OnExit(e);
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var name = Theme.IsTaskbarDark ? "tray-light.ico" : "tray-dark.ico";
        var stream = GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"))!.Stream;
        using (stream)
        {
            return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        }
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("设置…", null, (_, _) => ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出 OTPBar", null, (_, _) => Quit());
        return menu;
    }

    private void TogglePopup()
    {
        if (_popup is { IsVisible: true })
        {
            _popup.Hide();
            return;
        }
        _popup ??= new CodePopupWindow(_model, ShowSettings);
        _popup.ShowNearTray();
    }

    internal void ShowSettings()
    {
        _popup?.Hide();
        if (_settings is null)
        {
            _settings = new SettingsWindow(_model);
            _settings.Closed += (_, _) => _settings = null;
            _settings.Show();
        }
        else
        {
            _settings.WindowState = WindowState.Normal;
        }
        _settings.Activate();
    }

    private void Quit()
    {
        if (_model.IsEditing)
        {
            ShowSettings();
            _model.Message = "请先保存或取消当前修改，再退出。";
            return;
        }
        Shutdown();
    }
}
