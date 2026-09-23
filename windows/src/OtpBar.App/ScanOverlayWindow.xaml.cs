using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using OtpBar.Core;
using Drawing = System.Drawing;

namespace OtpBar.App;

/// <summary>
/// A dim sheet over the live desktop. The user drags a box, and only that box is read off the screen,
/// so a scan costs a few hundred kilobytes rather than a copy of the whole desktop.
/// </summary>
public partial class ScanOverlayWindow : Window
{
    private readonly Drawing.Rectangle _bounds = QrScanner.VirtualScreen;
    private Point _origin;
    private bool _dragging;

    private ScanOverlayWindow() => InitializeComponent();

    /// <summary>The decoded text, or null when nothing was selected or the user cancelled.</summary>
    public string? Result { get; private set; }

    public string? Error { get; private set; }

    internal static ScanOverlayWindow Prompt()
    {
        var window = new ScanOverlayWindow();
        window.ShowDialog();
        return window;
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int cx, int cy, uint flags);

    /// <summary>
    /// Placed in physical pixels. Sizing through WPF would go through device independent units, which
    /// do not line up with the screen across monitors of differing scale.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        SetWindowPos(new WindowInteropHelper(this).Handle, 0,
            _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, 0x0010 /* SWP_NOACTIVATE */);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _origin = e.GetPosition(Root);
        _dragging = true;
        Selection.Visibility = Visibility.Visible;
        CaptureMouse();
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            Place(e.GetPosition(Root));
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            base.OnMouseLeftButtonUp(e);
            return;
        }
        _dragging = false;
        ReleaseMouseCapture();
        var region = Place(e.GetPosition(Root));
        base.OnMouseLeftButtonUp(e);
        // A click, or a twitch while clicking, is not a selection.
        if (region.Width < 16 || region.Height < 16)
        {
            Error = "请拖出一个框把二维码圈起来。";
            Close();
            return;
        }
        Read(region);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        Close();
        base.OnMouseRightButtonUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
        base.OnKeyDown(e);
    }

    private Rect Place(Point corner)
    {
        var region = new Rect(_origin, corner);
        Selection.Margin = new Thickness(region.X, region.Y, 0, 0);
        Selection.Width = region.Width;
        Selection.Height = region.Height;
        return region;
    }

    private void Read(Rect region)
    {
        // The sheet and the selection border must not end up inside the picture.
        Visibility = Visibility.Hidden;
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        Thread.Sleep(80);

        try
        {
            Result = QrScanner.Decode(QrScanner.Capture(ToScreen(region)));
        }
        catch (OtpException error)
        {
            Error = error.Message;
        }
        finally
        {
            Close();
        }
    }

    /// <summary>Window units to physical screen pixels, via the ratio the window was actually given.</summary>
    private Drawing.Rectangle ToScreen(Rect region)
    {
        var horizontal = _bounds.Width / Root.ActualWidth;
        var vertical = _bounds.Height / Root.ActualHeight;
        var x = _bounds.X + (int)Math.Round(region.X * horizontal);
        var y = _bounds.Y + (int)Math.Round(region.Y * vertical);
        var width = Math.Max(1, (int)Math.Round(region.Width * horizontal));
        var height = Math.Max(1, (int)Math.Round(region.Height * vertical));

        // A selection that lands off the desktop intersects to nothing, which is not a capturable area.
        var area = Drawing.Rectangle.Intersect(_bounds, new Drawing.Rectangle(x, y, width, height));
        return area.Width > 0 && area.Height > 0 ? area : throw OtpException.NoCodeFound();
    }
}
