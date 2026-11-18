using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FloatingTxtReader;

internal static class WheelRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run(Action<bool, string> check)
    {
        var state = new AppState { CloseHotkey = "", IsLocked = true, CharactersPerLine = 36 };
        var reader = new MainWindow(state) { ShowActivated = false, Topmost = false };
        var scroll = Field<DispatcherTimer>(reader, "_scrollTimer");
        Field<DispatcherTimer>(reader, "_saveTimer").Interval = TimeSpan.FromDays(1);
        var lines = Enumerable.Range(0, 1000).Select(index => $"合成测试行 {index}").ToList();
        var mouseFlag = typeof(UIElement).GetMethod("WriteFlag", Private)!;
        var overFlag = Enum.ToObject(mouseFlag.GetParameters()[0].ParameterType, 0x1000);
        void Over(bool value) => mouseFlag.Invoke(reader, new[] { overFlag, (object)value });
        MouseWheelEventArgs Send(int delta)
        {
            var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            Call(reader, "Window_PreviewMouseWheel", reader, args);
            return args;
        }
        void Reset(int line = 100)
        {
            scroll.Stop();
            SetField(reader, "_pendingScrollLines", 0);
            SetField(reader, "_wheelRemainder", 0);
            state.IsLocked = true;
            state.VisibleLines = 1;
            state.CurrentLine = line;
            Over(true);
            Call(reader, "RenderLines");
        }
        try
        {
            reader.Show();
            check(scroll.Interval == TimeSpan.FromMilliseconds(15), "Continuous wheel input uses a short 15 ms batching window");
            SetField(reader, "_lines", lines);
            Reset();
            var size = new Size(reader.ActualWidth, reader.ActualHeight);
            var first = Send(-120);
            check(first.Handled && state.CurrentLine == 101 && reader.ReaderText.Text == lines[101],
                "The first wheel detent renders immediately without waiting for a timer");
            Send(-120); Send(-120);
            check(state.CurrentLine == 101 && Field<int>(reader, "_pendingScrollLines") == 2,
                "Subsequent wheel detents coalesce inside the active frame interval");
            Over(false);
            Call(reader, "Window_MouseLeave", reader, new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount));
            check(state.CurrentLine == 103 && reader.ReaderText.Opacity == 0 && Field<int>(reader, "_pendingScrollLines") == 0,
                "Leaving the region commits accepted wheel input before hiding text");
            Call(reader, "FlushPendingScroll");
            check(state.CurrentLine == 103 && reader.ReaderText.Opacity == 0 && !scroll.IsEnabled,
                "A later empty flush neither repeats navigation nor reveals hidden text");
            check(new Size(reader.ActualWidth, reader.ActualHeight) == size,
                "Wheel navigation preserves the fixed reader window size");
            Reset();
            Send(-60); Send(120);
            check(state.CurrentLine == 99, "Reversing a fractional wheel delta does not swallow a full reverse detent");
            Reset();
            Send(-40); Send(-40);
            check(state.CurrentLine == 100 && !scroll.IsEnabled, "Partial wheel deltas accumulate without waking the timer");
            Send(-40);
            check(state.CurrentLine == 101, "Three same-direction high-resolution wheel deltas produce exactly one line");
            Reset();
            Send(-120); Send(-120); Send(120);
            Call(reader, "FlushPendingScroll");
            check(state.CurrentLine == 101, "Opposite full detents retain their net navigation while coalescing");
            Reset(0); Send(120);
            check(state.CurrentLine == 0 && !scroll.IsEnabled, "Beginning-of-file scrolling clamps without running an idle timer");
            Send(-120);
            check(state.CurrentLine == 1, "Reversing at the beginning of the file responds immediately");
            Reset(999); Send(-120);
            check(state.CurrentLine == 999, "End-of-file scrolling remains bounded");
            Reset(995); state.VisibleLines = 3; Send(-120); Send(-120); Send(-120);
            Call(reader, "FlushPendingScroll");
            check(state.CurrentLine == 997 && reader.ReaderText.Text == string.Join(Environment.NewLine, lines.Skip(997)),
                "Multiple visible lines preserve the final-page boundary and text");
            Reset(); state.IsLocked = false;
            check(!Send(-120).Handled && state.CurrentLine == 100, "Unlocked reader still ignores wheel navigation");
            Reset(); Over(false);
            check(!Send(-120).Handled && state.CurrentLine == 100, "Wheel input originating outside the reader remains ignored");
            Reset(); Send(-120); Send(-120);
            Call(reader, "ToggleLock");
            check(!state.IsLocked && state.CurrentLine == 102 && Field<int>(reader, "_pendingScrollLines") == 0,
                "Unlocking commits previously accepted wheel navigation before changing mode");
            Reset(); Send(-120);
            Pump(80);
            check(!scroll.IsEnabled, "Wheel frame timer stops completely when input becomes idle");
            Reset(); Send(-120); Send(-120);
            var elapsed = Stopwatch.StartNew();
            var appliedAt = double.PositiveInfinity;
            EventHandler observe = (_, _) => { if (state.CurrentLine == 102) appliedAt = Math.Min(appliedAt, elapsed.Elapsed.TotalMilliseconds); };
            scroll.Tick += observe;
            Action? busyInput = null;
            busyInput = () =>
            {
                var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 1000;
                while (Stopwatch.GetTimestamp() < until) { }
                if (elapsed.ElapsedMilliseconds < 250) reader.Dispatcher.BeginInvoke(DispatcherPriority.Input, busyInput!);
            };
            try
            {
                reader.Dispatcher.BeginInvoke(DispatcherPriority.Input, busyInput);
                Pump(350);
                check(appliedAt < 250, $"Pending wheel input runs during queued input work instead of starving ({appliedAt:0.0} ms)");
            }
            finally { scroll.Tick -= observe; }
            var path = Path.Combine(Path.GetTempPath(), $"FloatingTxtReader-wheel-switch-{Guid.NewGuid():N}.txt");
            try
            {
                Reset(); Send(-120); Send(-120);
                File.WriteAllText(path, "新文件第一行\n新文件第二行\n新文件第三行");
                Call(reader, "LoadFile", path);
                Pump(60);
                check(state.CurrentLine == 0 && reader.ReaderText.Text == "新文件第一行" && Field<int>(reader, "_pendingScrollLines") == 0,
                    "Switching TXT files cannot replay the old file's pending wheel delta into the new document");
            }
            finally { File.Delete(path); }
        }
        finally
        {
            scroll.Stop();
            Field<DispatcherTimer>(reader, "_saveTimer").Stop();
            Call(reader, "StopFastForward");
            Call(reader, "UnregisterHotkeys");
            Field<IDisposable>(reader, "_trayIcon").Dispose();
            SetField(reader, "_isExiting", true);
            reader.Close();
        }
    }

    internal static void Pump(int milliseconds)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        using var timer = new System.Threading.Timer(_ => dispatcher.BeginInvoke(DispatcherPriority.Send,
            new Action(() => frame.Continue = false)), null, milliseconds, Timeout.Infinite);
        Dispatcher.PushFrame(frame);
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
}
