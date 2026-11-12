using System.IO;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FloatingTxtReader;

internal static class SettingsAppearanceRegression
{
    internal static void Run(Action<bool, string> check)
    {
        var placementCases = new[]
        {
            ("right side", new Rect(100, 100, 240, 200), new Size(340, 468), new Rect(0, 0, 1920, 1040), 6d, new Point(346, 100)),
            ("right edge falls back left", new Rect(1680, 800, 240, 200), new Size(340, 468), new Rect(0, 0, 1920, 1040), 6d, new Point(1334, 572)),
            ("bottom edge clamps upwards", new Rect(100, 800, 240, 200), new Size(340, 468), new Rect(0, 0, 1920, 1040), 6d, new Point(346, 572)),
            ("negative-coordinate monitor", new Rect(-1800, 100, 240, 200), new Size(340, 468), new Rect(-1920, 0, 1920, 1040), 6d, new Point(-1554, 100)),
            ("monitor above primary", new Rect(100, -980, 240, 200), new Size(340, 468), new Rect(0, -1040, 1920, 1040), 6d, new Point(346, -980)),
            ("150 percent geometry", new Rect(2520, 1200, 360, 300), new Size(510, 702), new Rect(0, 0, 2880, 1560), 9d, new Point(2001, 858)),
            ("200 percent geometry", new Rect(3360, 1600, 480, 400), new Size(680, 936), new Rect(0, 0, 3840, 2080), 12d, new Point(2668, 1144)),
            ("small work area", new Rect(250, 250, 240, 100), new Size(340, 468), new Rect(0, 0, 640, 400), 6d, new Point(0, 0)),
            ("stale monitor position", new Rect(3000, 3000, 240, 200), new Size(340, 468), new Rect(0, 0, 1920, 1040), 6d, new Point(1580, 572)),
            ("tray double-click cursor fallback", new Rect(100, 100, 0, 0), new Size(340, 468), new Rect(0, 0, 1920, 1040), 6d, new Point(106, 100)),
        };
        foreach (var (name, anchor, size, workArea, gap, expected) in placementCases)
            check(TrayMenu.SettingsWindowPosition(anchor, size, workArea, gap) == expected, $"Settings placement: {name}");
        foreach (var kind in new[] { "reading", "hotkeys" })
        {
            var applied = 0;
            var window = Create(kind, _ => { applied++; return null; });
            try
            {
                window.Show();
                window.UpdateLayout();
                check(window.WindowStyle == WindowStyle.None && window.AllowsTransparency,
                    $"{kind} settings use a transparent rounded shell instead of old system chrome");
                var surface = (Border)window.Template.FindName("DialogSurface", window);
                check(surface.CornerRadius.TopLeft >= 14 && ((SolidColorBrush)surface.Background).Color == Color.FromRgb(232, 239, 234),
                    $"{kind} settings match tray-menu corner radius and background");
                check(window.Width <= (kind == "reading" ? 340 : 380) && surface.ActualHeight <= 500 && window.FontSize == 12,
                    $"{kind} settings use a compact layout with tray-menu-sized text");
                var fields = Descendants<TextBox>(window).ToArray();
                check(fields.Length >= 6 && fields.All(box => box.ActualHeight >= 28 && box.Template.FindName("FieldSurface", box) is Border),
                    $"{kind} settings fields share rounded templates and readable hit heights");
                check(fields.Zip(fields.Skip(1), (previous, next) =>
                    next.TranslatePoint(new Point(), surface).Y - previous.TranslatePoint(new Point(0, previous.ActualHeight), surface).Y >= 5).All(gap => gap),
                    $"{kind} compact rows preserve a visible gap between input hit regions");
                check(Descendants<TextBlock>(window).Where(block => block.TextWrapping == TextWrapping.NoWrap && !string.IsNullOrEmpty(block.Text))
                    .All(block => new FormattedText(block.Text, CultureInfo.CurrentUICulture, block.FlowDirection,
                        new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch), block.FontSize,
                        block.Foreground, VisualTreeHelper.GetDpi(block).PixelsPerDip).Width <= block.ActualWidth + 1),
                    $"{kind} compact labels and button text remain fully visible");
                check(Math.Abs(surface.ActualWidth - window.Width) < 1,
                    $"{kind} preview uses the actual dialog width without changing layout");
                var apply = (Button)window.FindName("ApplyButton");
                var cancel = (Button)window.FindName("CancelButton");
                check(apply.ActualHeight >= 28 && cancel.ActualHeight >= 28 && cancel.IsCancel &&
                    apply.TranslatePoint(new Point(), surface).Y + apply.ActualHeight <= surface.ActualHeight,
                    $"{kind} settings footer stays visible and Escape remains a cancel action");
                foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
                {
                    var bitmap = Render(surface, scale);
                    var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                    bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                    var partial = false;
                    for (var y = 0; y < Math.Min(32, bitmap.PixelHeight); y++)
                    for (var x = 0; x < Math.Min(32, bitmap.PixelWidth); x++)
                    {
                        var alpha = pixels[(y * bitmap.PixelWidth + x) * 4 + 3];
                        partial |= alpha is > 0 and < 255;
                    }
                    check(pixels[3] == 0 && partial, $"{kind} settings preserve transparent antialiased corners at {scale * 100:0}%");
                }
                if (kind == "reading")
                {
                    var color = (ComboBox)window.FindName("ColorBox");
                    check(Math.Abs(color.ActualHeight - fields[0].ActualHeight) < 1,
                        "Reading text fields and dropdowns use the same height");
                    color.Focus();
                    color.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Down)
                        { RoutedEvent = Keyboard.KeyDownEvent });
                    check(color.SelectedIndex == 1, "Restyled color selector still supports keyboard selection of black");
                    var toggle = (ToggleButton)color.Template.FindName("SelectionToggle", color);
                    toggle.IsChecked = true;
                    window.UpdateLayout();
                    var popup = (Popup)color.Template.FindName("PART_Popup", color);
                    var dropdown = (Border)color.Template.FindName("DropdownSurface", color);
                    check(color.IsDropDownOpen && popup.IsOpen && popup.AllowsTransparency && dropdown.CornerRadius.TopLeft >= 8,
                        "Color selector opens a matching transparent rounded dropdown");
                    color.IsDropDownOpen = false;
                }
                else
                {
                    var clear = Descendants<Button>(window).Where(button => Equals(button.Content, "清空")).ToArray();
                    check(clear.Length == 7 && clear.All(button => button.Template.FindName("ButtonSurface", button) is Border && button.ActualHeight >= 28),
                        "All seven shortcut clear actions inherit the same rounded style");
                    var box = fields.First(field => field.IsReadOnly);
                    box.Focus();
                    box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.F8)
                        { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    check(box.Text == "F8", "Restyled shortcut field still records a pressed key");
                    clear[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    check(box.Text == "", "Restyled clear action still removes its shortcut");
                }
                var invalid = (TextBox)window.FindName(kind == "reading" ? "FontSizeBox" : "FastForwardSpeedBox");
                invalid.Text = "0";
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(window.IsVisible && applied == 0 && !string.IsNullOrWhiteSpace(((TextBlock)window.FindName("ErrorText")).Text),
                    $"{kind} invalid values show an inline error without applying or closing");
                window.MaxHeight = 420;
                window.UpdateLayout();
                var scroll = Descendants<ScrollViewer>(window).First();
                check(surface.ActualHeight <= 420 && scroll.ScrollableHeight > 0 &&
                    apply.TranslatePoint(new Point(), surface).Y + apply.ActualHeight <= surface.ActualHeight - 10,
                    $"{kind} short work areas scroll the form while keeping footer actions visible");
                invalid.Text = kind == "reading" ? "18" : "12";
                var close = (Button)window.Template.FindName("DialogCloseButton", window);
                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(!window.IsVisible && applied == 0, $"{kind} rounded title-bar close cancels without applying settings");
            }
            finally { window.Close(); }

            var saved = Create(kind, updated =>
            {
                check(kind == "reading" ? updated.FontSize == 18 && updated.ForegroundColor == "#000000" : updated.FastForwardLinesPerSecond == 12,
                    $"{kind} apply preserves edited values through the existing callback");
                return null;
            });
            try
            {
                saved.Show();
                ((TextBox)saved.FindName(kind == "reading" ? "FontSizeBox" : "FastForwardSpeedBox")).Text = kind == "reading" ? "18" : "12";
                if (kind == "reading") ((ComboBox)saved.FindName("ColorBox")).SelectedIndex = 1;
                ((Button)saved.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(!saved.IsVisible, $"{kind} successful apply closes the restyled dialog");
            }
            finally { saved.Close(); }

            var escaped = Create(kind, _ => { applied++; return null; });
            try
            {
                escaped.Show();
                escaped.Activate();
                ((TextBox)escaped.FindName(kind == "reading" ? "FontSizeBox" : "FastForwardSpeedBox")).Focus();
                var source = (HwndSource)PresentationSource.FromVisual(escaped);
                PostMessage(source.Handle, 0x0100, new IntPtr(0x1B), new IntPtr(1));
                var frame = new DispatcherFrame();
                var timeout = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                timeout.Tick += (_, _) => { timeout.Stop(); frame.Continue = false; };
                timeout.Start();
                Dispatcher.PushFrame(frame);
                check(!escaped.IsVisible && applied == 0, $"{kind} native Escape cancels the dialog without saving");
            }
            finally { escaped.Close(); }
        }
    }

    internal static void RenderPreview(string prefix)
    {
        foreach (var kind in new[] { "reading", "hotkeys" })
        {
            var window = Create(kind, _ => null);
            try
            {
                window.Show();
                window.UpdateLayout();
                var surface = window.Template.FindName("DialogSurface", window) as FrameworkElement ?? (FrameworkElement)window.Content;
                Save(Render(surface), prefix + "-" + kind + ".png");
                Save(Render(surface, 1.5), prefix + "-" + kind + "-150.png");
                if (kind == "reading")
                {
                    var color = (ComboBox)window.FindName("ColorBox");
                    color.IsDropDownOpen = true;
                    window.UpdateLayout();
                    var dropdown = (Border)color.Template.FindName("DropdownSurface", color);
                    dropdown.UpdateLayout();
                    Save(Render(dropdown), prefix + "-color-dropdown.png");
                    color.IsDropDownOpen = false;
                }
                var field = (TextBox)window.FindName(kind == "reading" ? "FontSizeBox" : "FastForwardSpeedBox");
                field.Text = "0";
                window.GetType().GetMethod("Apply_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, new object[] { window, new RoutedEventArgs() });
                window.UpdateLayout();
                Save(Render(surface), prefix + "-" + kind + "-error.png");
                Console.WriteLine($"SETTINGS {kind} {surface.ActualWidth:0}x{surface.ActualHeight:0}");
            }
            finally { window.Close(); }
        }
    }

    private static Window Create(string kind, Func<AppState, string?> apply)
    {
        var state = new AppState { HideHotkey = "波浪键", FastForwardHotkey = "Add", FontSize = 16, CharactersPerLine = 20 };
        return kind == "reading" ? new SettingsWindow(state, apply) : new HotkeySettingsWindow(state, apply);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    internal static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    internal static RenderTargetBitmap Render(FrameworkElement surface, double scale = 1)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth * scale), (int)Math.Ceiling(surface.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawRectangle(new VisualBrush(surface), null, new Rect(0, 0, surface.ActualWidth, surface.ActualHeight));
        bitmap.Render(visual);
        return bitmap;
    }

    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
