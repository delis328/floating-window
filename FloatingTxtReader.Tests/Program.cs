using System.Text.Json;
using System.Windows.Input;
using FloatingTxtReader;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--reserve-hotkey"))
        {
            var registered = RegisterHotKey(IntPtr.Zero, 99, 7, 0x78);
            Console.WriteLine(registered ? "READY" : "UNAVAILABLE");
            Console.ReadLine();
            if (registered) UnregisterHotKey(IntPtr.Zero, 99);
            return;
        }
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (args.Length == 2 && args[0] == "--render-preview")
        {
            var preview = new HotkeySettingsWindow(new AppState(), _ => null);
            var content = (FrameworkElement)preview.Content;
            content.Measure(new Size(474, double.PositiveInfinity));
            content.Arrange(new Rect(0, 0, 474, content.DesiredSize.Height));
            content.UpdateLayout();
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                var bounds = new Rect(new Size(474, content.DesiredSize.Height));
                context.DrawRectangle(Brushes.White, null, bounds);
                context.DrawRectangle(new VisualBrush(content), null, new Rect(18, 18, content.ActualWidth, content.ActualHeight));
            }
            var bitmap = new RenderTargetBitmap(474, (int)Math.Ceiling(content.DesiredSize.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(args[1]);
            encoder.Save(output);
            application.Shutdown();
            return;
        }
        if (args.Contains("--preview"))
        {
            var preview = new HotkeySettingsWindow(new AppState(), _ => null);
            preview.Closed += (_, _) => application.Shutdown();
            application.Run(preview);
            return;
        }

        var passed = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            Console.WriteLine($"PASS {++passed}: {name}");
        }

        foreach (var value in new[] { "Ctrl+Alt+Q", "~", "波浪键", "Ctrl+0", "Shift+Down", "Win+Alt+N" })
            Check(Hotkeys.TryParse(value, out _, out _), $"Accept {value}");
        foreach (var value in new[] { "", "Ctrl", "Ctrl+Alt", "None", "LeftCtrl", "Ctrl+LeftShift", "A+B", "Ctrl++A", "Ctrl+Ctrl+A", "Invalid", "F12", "999" })
            Check(!Hotkeys.TryParse(value, out _, out _), $"Reject {value}");

        Check(Hotkeys.TryParse("Ctrl+Alt+Q", out var modifiers, out var key) && modifiers == 3 && key == 0x51, "Parse native modifiers and key");
        Check(Hotkeys.Format(ModifierKeys.Control | ModifierKeys.Windows, Key.Down) == "Ctrl+Win+Down", "Format Windows modifier");
        Check(Hotkeys.Format(ModifierKeys.None, Key.Oem3) == "波浪键", "Preserve legacy tilde name");
        Check(Hotkeys.Format(ModifierKeys.Alt, Key.D0) == "Alt+0", "Format digit key");

        var legacy = JsonSerializer.Deserialize<AppState>("{\"CloseHotkey\":\"Alt+Q\",\"CurrentLine\":42}")!;
        Check(legacy.CloseHotkey == "Alt+Q" && legacy.CurrentLine == 42, "Preserve existing settings");
        Check(Hotkeys.GetBindings(legacy).Skip(1).All(binding => binding.Shortcut == ""), "New shortcuts default to unassigned");
        Check(Hotkeys.Validate(legacy) is null, "Legacy config is valid");
        Check(legacy.FastForwardLinesPerSecond == 10, "Old configurations default to ten lines per second");
        legacy.HideHotkey = "q+alt";
        Check(Hotkeys.Validate(legacy) is not null, "Reject reordered duplicate shortcuts");
        legacy.HideHotkey = "Ctrl+Alt+H";
        legacy.FastForwardHotkey = "Ctrl+Down";
        Check(Hotkeys.Validate(legacy) is null, "Accept distinct shortcuts");
        legacy.FastForwardLinesPerSecond = 7;
        var restored = JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(legacy))!;
        Check(restored.HideHotkey == legacy.HideHotkey && restored.FastForwardHotkey == legacy.FastForwardHotkey, "Persist new shortcuts");
        Check(Hotkeys.GetBindings(restored).Select(binding => binding.Id).Distinct().Count() == 7, "Seven unique action ids");
        Check(restored.FastForwardLinesPerSecond == 7, "Persist custom fast-forward speed");

        foreach (var speed in new[] { -1, 0, 31, int.MaxValue })
            Check(Hotkeys.Validate(new AppState { FastForwardLinesPerSecond = speed }) is not null, $"Reject speed {speed}");
        foreach (var speed in new[] { 1, 5, 10, 30 })
        {
            Check(Hotkeys.Validate(new AppState { FastForwardLinesPerSecond = speed }) is null, $"Accept speed {speed}");
            Check(Hotkeys.FastForwardInterval(speed) == TimeSpan.FromSeconds(1d / speed), $"Fixed interval for {speed} lines per second");
        }
        // Exercise the real WPF window without pumping the save timer or touching settings.json.
        var state = new AppState { CloseHotkey = "", VisibleLines = 3, FastForwardLinesPerSecond = int.MaxValue };
        var reader = new MainWindow(state);
        try
        {
            reader.Show();
            Check(state.FastForwardLinesPerSecond == 30, "Clamp invalid persisted speed before use");
            SetField(reader, "_lines", Enumerable.Range(0, 100).Select(index => $"line {index}").ToList());
            SetField(reader, "_chapters", new List<(int Line, string Title)> { (0, "第一章"), (30, "第二章"), (60, "第三章") });
            Call(reader, "MoveByLine", -1);
            Check(state.CurrentLine == 0, "Previous line clamps at beginning");
            Call(reader, "MoveByLine", 1);
            Check(state.CurrentLine == 1, "Next line moves one line even with three visible lines");
            Call(reader, "MoveByLine", 200);
            Check(state.CurrentLine == 97, "Next line clamps at last visible page");
            Call(reader, "GoToLine", 10);
            Call(reader, "GoToChapterOffset", 1);
            Check(state.CurrentLine == 30, "Next chapter jumps to heading");
            Call(reader, "GoToChapterOffset", -1);
            Check(state.CurrentLine == 0, "Previous chapter jumps to heading");

            var configured = new AppState
            {
                CloseHotkey = "Ctrl+Alt+Shift+F1", HideHotkey = "Ctrl+Alt+Shift+F2",
                PreviousLineHotkey = "Ctrl+Alt+Shift+F3", NextLineHotkey = "Ctrl+Alt+Shift+F4",
                PreviousChapterHotkey = "Ctrl+Alt+Shift+F5", NextChapterHotkey = "Ctrl+Alt+Shift+F6",
                FastForwardHotkey = "Ctrl+Alt+Shift+F7", FastForwardLinesPerSecond = 5,
            };
            Check(Call(reader, "ApplyHotkeys", configured) is null, "Register all seven global shortcuts");
            Check(state.FastForwardLinesPerSecond == 5, "Apply custom fixed speed");
            Check(Field<HashSet<int>>(reader, "_registeredHotkeys").Count == 7, "All seven registrations are tracked");
            var swap = configured.NextLineHotkey;
            configured.NextLineHotkey = configured.PreviousLineHotkey;
            configured.PreviousLineHotkey = swap;
            Check(Call(reader, "ApplyHotkeys", configured) is null, "Swap two existing shortcut bindings");

            void Dispatch(int id) => Call(reader, "WindowMessageHook", IntPtr.Zero, 0x0312, new IntPtr(id), IntPtr.Zero, false);
            Call(reader, "GoToLine", 10);
            Dispatch(Hotkeys.NextLine);
            Check(state.CurrentLine == 11, "WM_HOTKEY dispatches next line while unlocked");
            Dispatch(Hotkeys.PreviousLine);
            Check(state.CurrentLine == 10, "WM_HOTKEY dispatches previous line");
            Dispatch(Hotkeys.NextChapter);
            Check(state.CurrentLine == 30, "WM_HOTKEY dispatches next chapter");
            Dispatch(Hotkeys.PreviousChapter);
            Check(state.CurrentLine == 0, "WM_HOTKEY dispatches previous chapter");
            Dispatch(Hotkeys.Hide);
            Check(!reader.IsVisible, "Hide shortcut leaves reader hidden");
            Dispatch(Hotkeys.NextLine);
            Check(state.CurrentLine == 0, "Navigation pauses while hidden");
            Dispatch(Hotkeys.Hide);
            Check(reader.IsVisible, "Same shortcut restores hidden reader");

            var watch = Field<Stopwatch>(reader, "_fastForwardWatch");
            var timer = Field<DispatcherTimer>(reader, "_fastForwardTimer");
            SetField(reader, "_lastFastForwardStep", TimeSpan.Zero);
            watch.Restart();
            timer.Start();
            Thread.Sleep(350);
            Call(reader, "FastForwardTick", true);
            Check(state.CurrentLine == 1, "Held fast-forward advances exactly one line per tick");
            Call(reader, "FastForwardTick", true);
            Check(state.CurrentLine == 1, "Fast-forward does not advance before next interval");
            Thread.Sleep(110);
            Call(reader, "FastForwardTick", true);
            Check(state.CurrentLine == 2, "Advance when next fixed interval is reached");
            Call(reader, "FastForwardTick", true);
            Check(state.CurrentLine == 2, "Do not burst through missed intervals");
            state.FastForwardLinesPerSecond = 1;
            SetField(reader, "_lastFastForwardStep", TimeSpan.Zero);
            watch.Restart();
            Thread.Sleep(350);
            Call(reader, "FastForwardTick", true);
            Check(state.CurrentLine == 2, "One line per second does not accelerate after holding 350 ms");
            Thread.Sleep(700);
            Call(reader, "FastForwardTick", true);
            Check(state.CurrentLine == 3, "One line per second advances after a full second");
            Thread.Sleep(50);
            Call(reader, "FastForwardTick", true);
            Check(state.CurrentLine == 3, "Longer holds keep the same fixed interval");
            Call(reader, "FastForwardTick", false);
            Check(state.CurrentLine == 3 && !timer.IsEnabled && !watch.IsRunning, "Release stops immediately without advancing another line");
            state.FastForwardLinesPerSecond = 5;
            SetField(reader, "_lastFastForwardStep", TimeSpan.Zero);
            watch.Restart(); timer.Start();
            Call(reader, "GoToLine", 97);
            Thread.Sleep(350);
            Call(reader, "FastForwardTick", true);
            Check(state.CurrentLine == 97 && !timer.IsEnabled, "Fast-forward stops at end of file");
            watch.Restart(); timer.Start();
            Dispatch(Hotkeys.Hide);
            Check(!timer.IsEnabled, "Hiding stops fast-forward");
            Dispatch(Hotkeys.Hide);

            Call(reader, "ShowHotkeySettings");
            Check(Field<HashSet<int>>(reader, "_registeredHotkeys").Count == 0, "Opening shortcut editor suspends global shortcuts");
            var editor = Field<HotkeySettingsWindow>(reader, "_hotkeySettingsWindow");
            var boxes = Field<Dictionary<int, TextBox>>(editor, "_boxes");
            Check(boxes.Count == 7, "Shortcut editor shows seven fields");
            boxes[Hotkeys.Hide].Text = boxes[Hotkeys.Close].Text;
            Call(editor, "Apply_Click", editor, new RoutedEventArgs());
            Check(editor.IsVisible && ((TextBlock)editor.FindName("ErrorText")).Text.Contains("重复"), "Editor displays duplicate shortcut error without closing");
            boxes[Hotkeys.Hide].Text = "F12";
            Call(editor, "Apply_Click", editor, new RoutedEventArgs());
            Check(editor.IsVisible && ((TextBlock)editor.FindName("ErrorText")).Text.Contains("保留"), "Editor rejects reserved key without closing");
            boxes[Hotkeys.Hide].Text = configured.HideHotkey;
            var speedBox = (TextBox)editor.FindName("FastForwardSpeedBox");
            Check(speedBox.Text == "5", "Editor loads saved speed");
            foreach (var invalidSpeed in new[] { "", "0", "-1", "31", "1.5", "abc", "2147483648" })
            {
                speedBox.Text = invalidSpeed;
                Call(editor, "Apply_Click", editor, new RoutedEventArgs());
                Check(editor.IsVisible && ((TextBlock)editor.FindName("ErrorText")).Text.Contains("快进速度"), $"Editor rejects speed '{invalidSpeed}'");
            }
            speedBox.Text = "12";
            editor.Close();
            Check(state.FastForwardLinesPerSecond == 5, "Cancel discards edited speed");
            Check(state.HideHotkey == configured.HideHotkey, "Cancel discards edited shortcut values");
            Check(Field<HashSet<int>>(reader, "_registeredHotkeys").Count == 7, "Cancel restores all old registrations");
            Call(reader, "ShowHotkeySettings");
            editor = Field<HotkeySettingsWindow>(reader, "_hotkeySettingsWindow");
            ((TextBox)editor.FindName("FastForwardSpeedBox")).Text = "12";
            Call(editor, "Apply_Click", editor, new RoutedEventArgs());
            Check(!editor.IsVisible && state.FastForwardLinesPerSecond == 12, "Editor applies custom speed without changing shortcuts");


            using var reservation = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--reserve-hotkey")
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            try
            {
                Check(reservation.StandardOutput.ReadLine() == "READY", "Reserve conflict shortcut in another process");
                Call(reader, "ShowHotkeySettings");
                editor = Field<HotkeySettingsWindow>(reader, "_hotkeySettingsWindow");
                var oldShortcut = state.FastForwardHotkey;
                var oldSpeed = state.FastForwardLinesPerSecond;
                configured.FastForwardLinesPerSecond = 9;
                configured.FastForwardHotkey = "Ctrl+Alt+Shift+F9";
                Check(Call(reader, "ApplyHotkeys", configured) is string, "Reject shortcut occupied by another process");
                Check(state.FastForwardHotkey == oldShortcut, "Failed registration preserves saved bindings");
                Check(state.FastForwardLinesPerSecond == oldSpeed, "Failed registration preserves saved speed");
                Check(Field<HashSet<int>>(reader, "_registeredHotkeys").Count == 0, "Failed registration releases partial new bindings");
                editor.Close();
                Check(Field<HashSet<int>>(reader, "_registeredHotkeys").Count == 7, "Closing failed editor restores old working shortcuts");
            }
            finally
            {
                reservation.StandardInput.WriteLine();
                if (!reservation.WaitForExit(5000)) reservation.Kill();
            }
        }
        finally
        {
            Field<DispatcherTimer>(reader, "_saveTimer").Stop();
            Call(reader, "StopFastForward");
            Call(reader, "UnregisterHotkeys");
            Field<IDisposable>(reader, "_trayIcon").Dispose();
            SetField(reader, "_isExiting", true);
            reader.Close();
            application.Shutdown();
        }
        Console.WriteLine($"All {passed} checks passed.");
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
