using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FloatingTxtReader;

internal static class SettingsWriterRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Run(Action<bool, string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), $"FloatingTxtReader-writer-{Guid.NewGuid():N}.json");
        try
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var snapshots = new List<AppState>();
            var callerThread = Environment.CurrentManagedThreadId;
            var writerThread = callerThread;
            var active = 0;
            var maximumActive = 0;
            var writer = new SettingsWriter(json =>
            {
                writerThread = Environment.CurrentManagedThreadId;
                maximumActive = Math.Max(maximumActive, Interlocked.Increment(ref active));
                try
                {
                    if (snapshots.Count == 0)
                    {
                        entered.Set();
                        if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Synthetic write gate timed out");
                    }
                    snapshots.Add(JsonSerializer.Deserialize<AppState>(json)!);
                    SettingsStore.WriteSnapshot(path, json);
                }
                finally { Interlocked.Decrement(ref active); }
            });
            var state = new AppState { CurrentLine = 10, CurrentSourceOffset = 420, CurrentFileRemoveBlankLines = true, IsLocked = true };
            var first = writer.SaveAsync(state);
            try
            {
                check(entered.Wait(TimeSpan.FromSeconds(5)) && !first.IsCompleted,
                    "Settings save returns without waiting for a blocked disk writer");
                state.CurrentLine = 20;
                var middle = writer.SaveAsync(state);
                state.CurrentLine = 30;
                var latest = writer.SaveAsync(state);
                state.CurrentLine = 40;
                check(ReferenceEquals(first, middle) && ReferenceEquals(first, latest) && !latest.IsCompleted,
                    "Pending snapshots share one worker completion instead of queueing independent writes");
            }
            finally { release.Set(); }
            Wait(first);
            check(snapshots.Select(snapshot => snapshot.CurrentLine).SequenceEqual(new[] { 10, 30 }),
                "Slow saves coalesce obsolete pending progress and capture immutable snapshots");
            check(writerThread != callerThread && maximumActive == 1,
                "Only one settings writer runs at once, off the caller's UI thread");
            var saved = JsonSerializer.Deserialize<AppState>(File.ReadAllText(path))!;
            check(saved.CurrentLine == 30 && saved.CurrentSourceOffset == 420 && saved.CurrentFileRemoveBlankLines == true && saved.IsLocked,
                "Atomic background save preserves reading position, cleanup decision and lock state");
            check(!File.Exists(path + ".tmp"), "Successful atomic save leaves no temporary settings file behind");

            for (var index = 0; index < 40; index++) Wait(writer.SaveAsync(new AppState { CurrentLine = index }));
            check(JsonSerializer.Deserialize<AppState>(File.ReadAllText(path))!.CurrentLine == 39,
                "A save arriving after worker completion always starts another write");

            using var burstEntered = new ManualResetEventSlim();
            using var burstRelease = new ManualResetEventSlim();
            var burstValues = new List<int>();
            var burstWriter = new SettingsWriter(json =>
            {
                if (burstValues.Count == 0)
                {
                    burstEntered.Set();
                    if (!burstRelease.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                }
                burstValues.Add(JsonSerializer.Deserialize<AppState>(json)!.CurrentLine);
            });
            var burst = burstWriter.SaveAsync(new AppState { CurrentLine = 0 });
            try
            {
                check(burstEntered.Wait(TimeSpan.FromSeconds(5)), "Burst-save test reaches a blocked first write");
                Parallel.For(1, 201, index => { _ = burstWriter.SaveAsync(new AppState { CurrentLine = index }); });
                _ = burstWriter.SaveAsync(new AppState { CurrentLine = 999 });
            }
            finally { burstRelease.Set(); }
            Wait(burst);
            check(burstValues.SequenceEqual(new[] { 0, 999 }),
                "Concurrent save requests keep only the latest pending snapshot");

            var attempts = 0;
            var recovering = new SettingsWriter(json =>
            {
                if (Interlocked.Increment(ref attempts) == 1) throw new IOException("Synthetic disk failure");
                SettingsStore.WriteSnapshot(path, json);
            });
            var failure = recovering.SaveAsync(new AppState { CurrentLine = 50 });
            var failed = false;
            try { Wait(failure); }
            catch (IOException) { failed = true; }
            check(failed, "A failed final write is observable instead of being silently reported as saved");
            Wait(recovering.SaveAsync(new AppState { CurrentLine = 51 }));
            check(JsonSerializer.Deserialize<AppState>(File.ReadAllText(path))!.CurrentLine == 51,
                "An IO failure does not strand or poison later saves");
            var denied = new SettingsWriter(_ => throw new UnauthorizedAccessException("Synthetic permission failure"));
            var accessFailed = false;
            try { Wait(denied.SaveAsync(new AppState())); }
            catch (UnauthorizedAccessException) { accessFailed = true; }
            check(accessFailed, "Permission failures also complete the save task with an error");

            CheckDispatcherResponsiveness(check);
            using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--save-exit-probe \"{path}\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            })!;
            if (!child.WaitForExit(10000)) { child.Kill(); throw new Exception("Exit-save subprocess timed out"); }
            check(child.ExitCode == 0, "Explicit exit completes its final save in an isolated subprocess");
            var final = JsonSerializer.Deserialize<AppState>(File.ReadAllText(path))!;
            check(final.CurrentLine == 42, "Exit drains pending wheel input and waits until the newest progress replaces an older in-flight save");
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
    }

    private static void CheckDispatcherResponsiveness(Action<bool, string> check)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var lastLine = -1;
        var writer = new SettingsWriter(json =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            lastLine = JsonSerializer.Deserialize<AppState>(json)!.CurrentLine;
        });
        var state = new AppState { CloseHotkey = "", IsLocked = true };
        var reader = new MainWindow(state, writer) { ShowActivated = false, Topmost = false };
        try
        {
            reader.Show();
            reader.GetType().GetField("_lines", Private)!.SetValue(reader, Enumerable.Range(0, 100).Select(index => $"line {index}").ToList());
            var timer = (DispatcherTimer)reader.GetType().GetField("_saveTimer", Private)!.GetValue(reader)!;
            check(timer.Interval == TimeSpan.FromMilliseconds(700), "Normal progress saving retains its existing 700 ms debounce");
            timer.Interval = TimeSpan.FromMilliseconds(10);
            reader.GetType().GetMethod("GoToLine", Private)!.Invoke(reader, new object[] { 1 });
            WheelRegression.Pump(100);
            check(entered.IsSet, "The real progress timer starts the background writer");
            var heartbeat = false;
            reader.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => heartbeat = true));
            var mouseFlag = typeof(UIElement).GetMethod("WriteFlag", Private)!;
            mouseFlag.Invoke(reader, new[] { Enum.ToObject(mouseFlag.GetParameters()[0].ParameterType, 0x1000), (object)true });
            reader.GetType().GetMethod("Window_PreviewMouseWheel", Private)!.Invoke(reader, new object[]
            {
                reader, new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent },
            });
            WheelRegression.Pump(80);
            check(heartbeat && state.CurrentLine == 2 && reader.ReaderText.Text == "line 2" && !release.IsSet,
                "Wheel rendering and UI input remain responsive while a disk save is deliberately blocked");
        }
        finally
        {
            ((DispatcherTimer)reader.GetType().GetField("_saveTimer", Private)!.GetValue(reader)!).Stop();
            release.Set();
            Wait(writer.SaveAsync(state));
            WheelRegression.Pump(20);
            reader.GetType().GetMethod("UnregisterHotkeys", Private)!.Invoke(reader, null);
            ((IDisposable)reader.GetType().GetField("_trayIcon", Private)!.GetValue(reader)!).Dispose();
            reader.GetType().GetField("_isExiting", Private)!.SetValue(reader, true);
            reader.Close();
        }
        check(lastLine == 2, "The responsive UI's latest position is the one eventually saved");
    }

    internal static void ExitProbe(string path)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var writer = new SettingsWriter(json =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            SettingsStore.WriteSnapshot(path, json);
        });
        var state = new AppState { CloseHotkey = "", IsLocked = true, CurrentLine = 40 };
        var reader = new MainWindow(state, writer) { ShowActivated = false, Topmost = false };
        reader.Show();
        reader.GetType().GetField("_lines", Private)!.SetValue(reader, Enumerable.Range(0, 100).Select(index => $"line {index}").ToList());
        _ = writer.SaveAsync(state);
        if (!entered.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
        var mouseFlag = typeof(UIElement).GetMethod("WriteFlag", Private)!;
        mouseFlag.Invoke(reader, new[] { Enum.ToObject(mouseFlag.GetParameters()[0].ParameterType, 0x1000), (object)true });
        for (var index = 0; index < 2; index++)
            reader.GetType().GetMethod("Window_PreviewMouseWheel", Private)!.Invoke(reader, new object[]
            {
                reader, new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent },
            });
        using var delayedRelease = new System.Threading.Timer(_ => release.Set(), null, 150, Timeout.Infinite);
        reader.GetType().GetMethod("ExitApplication", Private)!.Invoke(reader, null);
        if (JsonSerializer.Deserialize<AppState>(File.ReadAllText(path))!.CurrentLine != 42) throw new Exception("Exit saved stale progress");
    }

    private static void Wait(Task task) => task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
}
