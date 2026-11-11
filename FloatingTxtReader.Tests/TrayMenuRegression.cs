using System.Diagnostics;
using System.IO;
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

internal static class TrayMenuRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run(MainWindow reader, AppState state, Action<bool, string> check)
    {
        var menu = Field<TrayMenu>(reader, "_trayMenu");
        reader.GetType().GetField("_chapters", Private)!.SetValue(reader,
            new List<(int Line, string Title)> { (0, "第一话"), (6, "第二话") });
        Call(reader, "BuildTrayMenu", menu);
        var originalItems = menu.Items.Cast<object>().ToArray();
        check(menu.Items.OfType<Separator>().Count() == 3, "WPF tray menu preserves three functional separators");
        check(menu.Items[^1] == menu.ExitItem && (string)menu.ExitItem.Header == "退出应用", "Exit remains last in WPF menu");
        check(menu.SettingsItem.Items.Cast<MenuItem>().Select(item => (string)item.Header).SequenceEqual(new[] { "阅读设置...", "快捷键设置..." }),
            "WPF settings submenu preserves both settings entries");
        check(menu.ChapterButtonsItem.StaysOpenOnClick && !menu.StaysOpen, "Chapter controls stay open while outside clicks may dismiss the menu");

        foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
        {
            var bitmap = Render(menu, scale);
            check(HasTransparentAntialiasedCorners(bitmap, scale), $"Menu corners have real transparent and partial-alpha pixels at {scale * 100:0}%");
            check(HasTightBorder(bitmap, scale), $"Menu ends at border without an opaque outer fringe at {scale * 100:0}%");
        }
        var normal = Render(menu);
        check(normal.PixelWidth <= 240 && normal.PixelHeight <= 260, "Two-column tray menu stays compact in both dimensions");
        check(menu.FindName("NextFiveChaptersButton") is null, "Removed next-five-chapters action is absent from the menu");
        var previous = menu.PreviousChapterButton;
        var next = menu.NextChapterButton;
        check(Math.Abs(next.ActualWidth - previous.ActualWidth) < .1 && next.ActualHeight >= previous.ActualHeight * 1.18 &&
            next.ActualHeight <= previous.ActualHeight * 1.3,
            "Next chapter is visibly taller while both chapter controls remain aligned");
        var nextSurface = (Border)next.Template.FindName("ButtonSurface", next);
        var nextBackground = ((SolidColorBrush)nextSurface.Background).Color;
        check(Contrast(nextBackground, Color.FromRgb(232, 239, 234)) is >= 1.1 and <= 1.5 &&
            Contrast(nextBackground, ((SolidColorBrush)previous.Background).Color) >= 1.1 &&
            Contrast(((SolidColorBrush)next.Foreground).Color, nextBackground) >= 4.5,
            "Next chapter uses a visible but gentle same-family fill with readable text");
        check(new[] { "ChapterHover", "ChapterPressed" }.All(key =>
            Contrast(((SolidColorBrush)next.Foreground).Color, ((SolidColorBrush)next.FindResource(key)).Color) >= 4.5),
            "Pastel chapter hover and pressed colors keep dark text readable");
        var isPressed = typeof(System.Windows.Controls.Primitives.ButtonBase).GetProperty("IsPressed")!;
        try
        {
            isPressed.SetValue(next, true);
            Render(menu);
            var pressedColor = ((SolidColorBrush)nextSurface.Background).Color;
            check(pressedColor == ((SolidColorBrush)next.FindResource("ChapterPressed")).Color &&
                Contrast(((SolidColorBrush)next.Foreground).Color, pressedColor) >= 4.5,
                "Actual pressed next-chapter surface resolves its own pastel background");
        }
        finally
        {
            isPressed.SetValue(next, false);
            Render(menu);
        }
        next.IsEnabled = false;
        Render(menu);
        var disabledSurface = ((SolidColorBrush)nextSurface.Background).Color;
        var label = (ContentPresenter)next.Template.FindName("ButtonLabel", next);
        check(Contrast(System.Windows.Documents.TextElement.GetForeground(label) is SolidColorBrush labelBrush ? labelBrush.Color : Colors.Transparent, disabledSurface) >= 4.5 &&
            Contrast(disabledSurface, nextBackground) >= 1.1,
            "Disabled next chapter uses a distinct muted background with readable text");
        next.IsEnabled = true;
        Render(menu);
        var parent = (System.Windows.Controls.Grid)previous.Parent;
        var firstBounds = previous.TransformToAncestor(parent).TransformBounds(new Rect(previous.RenderSize));
        var secondBounds = next.TransformToAncestor(parent).TransformBounds(new Rect(next.RenderSize));
        check(Math.Abs(secondBounds.Left - firstBounds.Left) < .1 && secondBounds.Top - firstBounds.Bottom >= 5.9,
            "Chapter controls are vertically stacked with a gap");
        var chapterBounds = menu.ChapterButtonsItem.TransformToAncestor(menu).TransformBounds(new Rect(menu.ChapterButtonsItem.RenderSize));
        var listBounds = menu.ChapterListItem.TransformToAncestor(menu).TransformBounds(new Rect(menu.ChapterListItem.RenderSize));
        check(chapterBounds.Left >= menu.ActualWidth / 2 && listBounds.Right <= chapterBounds.Left,
            "Chapter controls occupy the right half without overlapping left-hand menu items");
        check(menu.FontSize >= 12 && Contrast(((SolidColorBrush)menu.Foreground).Color, Color.FromRgb(232, 239, 234)) >= 4.5,
            "Menu text remains readable on the stronger background");

        var originalPath = state.FilePath;
        state.FilePath = new string('长', 80) + ".txt";
        Call(reader, "BuildTrayMenu", menu);
        var longName = Render(menu);
        check(longName.PixelWidth == normal.PixelWidth && ((string)menu.FileItem.Header).Length < 40 &&
            ((string)menu.FileItem.ToolTip).Contains(state.FilePath), "Long file names do not widen menu and retain full path tooltip");
        state.FilePath = originalPath;
        state.IsLocked = true;
        Call(reader, "BuildTrayMenu", menu);
        check((string)menu.LockItem.Header == "解锁窗口", "Locked state shows the correct menu action");
        state.IsLocked = false;

        reader.Show();
        Call(reader, "BuildTrayMenu", menu);
        var tray = Field<object>(reader, "_trayIcon");
        var formsAssembly = tray.GetType().Assembly;
        var buttonsType = formsAssembly.GetType("System.Windows.Forms.MouseButtons")!;
        var mouseArgsType = formsAssembly.GetType("System.Windows.Forms.MouseEventArgs")!;
        void TrayMouseUp(string button)
        {
            var args = Activator.CreateInstance(mouseArgsType, Enum.Parse(buttonsType, button), 1, 0, 0, 0)!;
            tray.GetType().GetMethod("OnMouseUp", Private)!.Invoke(tray, new[] { args });
        }
        TrayMouseUp("Left");
        check(!menu.IsOpen, "Left tray click does not accidentally open the context menu");
        TrayMouseUp("Right");
        check(menu.IsOpen, "Actual tray right-click event opens the WPF menu");
        menu.Close();
        menu.IsOpen = true;
        menu.UpdateLayout();
        var source = PresentationSource.FromVisual(menu) as HwndSource;
        check(source is not null && (GetWindowLong(source.Handle, -20) & 0x80000) != 0,
            "Actual WPF menu popup uses a layered window for per-pixel transparency");
        menu.SettingsItem.IsSubmenuOpen = true;
        menu.SettingsItem.UpdateLayout();
        var popup = (Popup)menu.SettingsItem.Template.FindName("PART_Popup", menu.SettingsItem);
        check(popup.IsOpen && popup.AllowsTransparency, "Settings submenu also uses a transparent popup");
        check(ReferenceEquals(popup.PlacementTarget, menu), "Settings submenu opens beside the whole panel instead of covering the right column");
        var submenu = (Border)menu.SettingsItem.Template.FindName("SubmenuSurface", menu.SettingsItem);
        var submenuBitmap = Render(submenu);
        check(HasTransparentAntialiasedCorners(submenuBitmap, 1) && HasTightBorder(submenuBitmap, 1),
            "Settings submenu has smooth alpha corners without an opaque fringe");
        menu.Close();
        check(!menu.SettingsItem.IsSubmenuOpen && !popup.IsOpen, "Closing main menu also closes its settings popup");
        reader.Hide();
        menu.IsOpen = true;
        check(menu.IsOpen && PresentationSource.FromVisual(menu) is HwndSource,
            "Tray menu can open even when the reader is hidden");
        menu.Close();
        reader.Show();
        Click(menu.ReadingSettingsItem);
        var readingSettings = Field<Window>(reader, "_settingsWindow");
        check(readingSettings.IsVisible && !menu.IsOpen, "Reading settings command closes menu and opens existing settings window");
        readingSettings.Close();
        menu.IsOpen = true;
        Click(menu.HotkeySettingsItem);
        var hotkeySettings = Field<Window>(reader, "_hotkeySettingsWindow");
        check(hotkeySettings.IsVisible && !menu.IsOpen, "Hotkey settings command closes menu and opens existing editor");
        hotkeySettings.Close();
        menu.IsOpen = true;
        Click(menu.LockItem);
        check(state.IsLocked && !menu.IsOpen, "Lock command retains existing behavior and dismisses popup");
        Click(menu.LockItem);
        menu.IsOpen = true;
        Click(menu.ResetPositionItem);
        check(!menu.IsOpen && Math.Abs(reader.Left - (SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - reader.Width) / 2)) < 1,
            "Reset position command remains functional through WPF menu");
        Field<DispatcherTimer>(reader, "_saveTimer").Stop();

        menu.IsOpen = true;
        menu.Focus();
        var inputSource = PresentationSource.FromVisual(menu)!;
        menu.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
        check(!menu.IsOpen, "Escape dismisses the actual WPF context menu");
        menu.IsOpen = true;
        Mouse.Capture(null);
        check(!menu.IsOpen, "Losing mouse capture to an outside interaction dismisses the menu");

        for (var i = 0; i < 5; i++) { menu.IsOpen = true; menu.IsOpen = false; }
        var userBefore = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
        var gdiBefore = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
        for (var i = 0; i < 100; i++)
        {
            Call(reader, "BuildTrayMenu", menu);
            menu.IsOpen = true;
            menu.IsOpen = false;
        }
        check(menu.Items.Cast<object>().SequenceEqual(originalItems), "Opening the menu reuses existing WPF controls");
        var userAfter = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
        var gdiAfter = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
        check(userAfter <= userBefore + 8, $"WPF menu USER handles stay bounded after 100 opens ({userBefore} -> {userAfter})");
        check(gdiAfter <= gdiBefore + 8, $"WPF menu GDI handles stay bounded after 100 opens ({gdiBefore} -> {gdiAfter})");
    }

    internal static RenderTargetBitmap Render(FrameworkElement element, double scale = 1)
    {
        element.ApplyTemplate();
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(new Point(), element.DesiredSize));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * scale), (int)Math.Ceiling(element.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }

    internal static void RenderPreview(string prefix)
    {
        var state = new AppState { CloseHotkey = "" };
        var reader = new MainWindow(state);
        var menu = Field<TrayMenu>(reader, "_trayMenu");
        try
        {
            foreach (var kind in new[] { "normal", "long", "empty", "locked" })
            {
                state.FilePath = kind == "empty" ? null : kind == "long" ? new string('长', 80) + ".txt" : "demo-book.txt";
                state.IsLocked = kind == "locked";
                reader.GetType().GetField("_chapters", Private)!.SetValue(reader, kind == "empty"
                    ? new List<(int Line, string Title)>() : new List<(int Line, string Title)> { (0, "第一章"), (30, "第二章"), (60, "第三章") });
                state.CurrentLine = kind == "locked" ? 60 : 30;
                Call(reader, "BuildTrayMenu", menu);
                var bitmap = Render(menu);
                Save(bitmap, prefix + "-" + kind + ".png");
                Console.WriteLine($"TRAY {kind} {bitmap.PixelWidth}x{bitmap.PixelHeight}");
                if (kind != "normal") continue;
                var width = bitmap.PixelWidth;
                var height = bitmap.PixelHeight;
                var comparison = new DrawingVisual();
                using (var context = comparison.RenderOpen())
                {
                    context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width * 2 + 96, height + 48));
                    context.DrawRectangle(new SolidColorBrush(Color.FromRgb(32, 32, 32)), null, new Rect(width + 48, 0, width + 48, height + 48));
                    context.DrawImage(bitmap, new Rect(24, 24, width, height));
                    context.DrawImage(bitmap, new Rect(width + 72, 24, width, height));
                }
                var backgrounds = new RenderTargetBitmap(width * 2 + 96, height + 48, 96, 96, PixelFormats.Pbgra32);
                backgrounds.Render(comparison);
                Save(backgrounds, prefix + "-backgrounds.png");
                reader.Show();
                menu.IsOpen = true;
                menu.ResetPositionItem.Focus();
                Save(Render(menu), prefix + "-hover.png");
                menu.SettingsItem.IsSubmenuOpen = true;
                menu.SettingsItem.UpdateLayout();
                var submenu = (Border)menu.SettingsItem.Template.FindName("SubmenuSurface", menu.SettingsItem);
                Save(Render(submenu), prefix + "-settings.png");
                menu.Close();
            }
        }
        finally
        {
            menu.Close();
            Field<DispatcherTimer>(reader, "_saveTimer").Stop();
            Call(reader, "UnregisterHotkeys");
            Field<IDisposable>(reader, "_trayIcon").Dispose();
            reader.GetType().GetField("_isExiting", Private)!.SetValue(reader, true);
            reader.Close();
        }
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private static bool HasTransparentAntialiasedCorners(BitmapSource bitmap, double scale)
    {
        var size = (int)Math.Ceiling(16 * scale);
        foreach (var origin in new[] { new Point(0, 0), new Point(bitmap.PixelWidth - size, 0),
            new Point(0, bitmap.PixelHeight - size), new Point(bitmap.PixelWidth - size, bitmap.PixelHeight - size) })
        {
            var transparent = 0;
            var partial = 0;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var alpha = Pixel(bitmap, (int)origin.X + x, (int)origin.Y + y).A;
                if (alpha == 0) transparent++;
                if (alpha is > 0 and < 255) partial++;
            }
            if (transparent == 0 || partial < 4) return false;
        }
        return true;
    }

    private static bool HasTightBorder(BitmapSource bitmap, double scale)
    {
        var middle = bitmap.PixelWidth / 2;
        var surface = Pixel(bitmap, middle, (int)Math.Ceiling(6 * scale));
        for (var y = 0; y < Math.Ceiling(3 * scale); y++)
        {
            var color = Pixel(bitmap, middle, y);
            if (color.A == 0) continue;
            return Contrast(color, surface) >= 2.5 && Contrast(color, Colors.White) >= 2.5;
        }
        return false;
    }

    private static Color Pixel(BitmapSource bitmap, int x, int y)
    {
        var bytes = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), bytes, 4, 0);
        if (bytes[3] == 0) return Colors.Transparent;
        return Color.FromArgb(bytes[3], (byte)Math.Min(255, bytes[2] * 255 / bytes[3]),
            (byte)Math.Min(255, bytes[1] * 255 / bytes[3]), (byte)Math.Min(255, bytes[0] * 255 / bytes[3]));
    }

    private static double Contrast(Color first, Color second)
    {
        static double Linear(byte value) => value / 255d <= .04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
        static double Luminance(Color color) => .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    internal static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, uint flag);
}
