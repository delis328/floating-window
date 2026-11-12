using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace FloatingTxtReader;

public partial class TrayMenu : ContextMenu
{
    private HwndSource? _popupSource;
    private Rect? _lastBounds;

    public TrayMenu()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            UpdateLayout();
            _lastBounds = new Rect(PointToScreen(new Point()), PointToScreen(new Point(ActualWidth, ActualHeight)));
        };
        Closed += (_, _) =>
        {
            SettingsItem.IsSubmenuOpen = false;
            ReleasePopupHook();
        };
        AddHandler(Keyboard.PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler((_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        }), true);
        SettingsItem.SubmenuOpened += (_, _) =>
        {
            var popup = (Popup)SettingsItem.Template.FindName("PART_Popup", SettingsItem);
            popup.PlacementTarget = this;
            popup.VerticalOffset = SettingsItem.TranslatePoint(new System.Windows.Point(), this).Y - ((Border)popup.Child).Padding.Top;
        };
    }

    internal void OpenFromTray()
    {
        Close();
        IsOpen = true;
        UpdateLayout();
        _popupSource = PresentationSource.FromVisual(this) as HwndSource;
        if (_popupSource is null || (GetForegroundWindow() != _popupSource.Handle && !SetForegroundWindow(_popupSource.Handle)))
        {
            Close();
            return;
        }

        _popupSource.AddHook(PopupMessageHook);
        SetFocus(_popupSource.Handle);
        Focus();
        Keyboard.Focus(this);
    }

    internal void ShowSettingsWindow(Window window, bool modal = false)
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        var anchor = _lastBounds ?? new Rect(cursor.X, cursor.Y, 0, 0);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)anchor.Left, (int)anchor.Top)).WorkingArea;
        var workArea = new Rect(screen.X, screen.Y, screen.Width, screen.Height);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        var handle = new WindowInteropHelper(window).EnsureHandle();
        const uint positionOnly = 0x0001 | 0x0004 | 0x0010; // No resizing, Z-order change or activation.
        // Move to the menu's monitor before WPF measures the dialog at that monitor's DPI.
        SetWindowPos(handle, IntPtr.Zero, (int)anchor.Left, (int)anchor.Top, 0, 0, positionOnly);
        window.MaxHeight = screen.Height / VisualTreeHelper.GetDpi(window).DpiScaleY;
        void PositionAfterLayout(object sender, RoutedEventArgs e)
        {
            window.Loaded -= PositionAfterLayout;
            var dpi = VisualTreeHelper.GetDpi(window);
            var size = new Size(window.ActualWidth * dpi.DpiScaleX, window.ActualHeight * dpi.DpiScaleY);
            var position = SettingsWindowPosition(anchor, size, workArea, 6 * dpi.DpiScaleX);
            SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(position.X), (int)Math.Round(position.Y), 0, 0, positionOnly);
        }
        window.Loaded += PositionAfterLayout;
        if (modal) window.ShowDialog();
        else window.Show();
    }

    internal static Point SettingsWindowPosition(Rect anchor, Size size, Rect workArea, double gap)
    {
        var left = anchor.Right + gap;
        if (left + size.Width > workArea.Right) left = anchor.Left - size.Width - gap;
        return new Point(Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - size.Width)),
            Math.Clamp(anchor.Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - size.Height)));
    }

    private IntPtr PopupMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmActivateApp = 0x001C;
        if (message == wmActivateApp && wParam == IntPtr.Zero) Close();
        return IntPtr.Zero;
    }

    internal void Close()
    {
        ReleasePopupHook();
        SettingsItem.IsSubmenuOpen = false;
        IsOpen = false;
    }

    private void ReleasePopupHook()
    {
        _popupSource?.RemoveHook(PopupMessageHook);
        _popupSource = null;
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
