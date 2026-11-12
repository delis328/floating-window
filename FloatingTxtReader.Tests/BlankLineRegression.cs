using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Interop;
using FloatingTxtReader;

internal static class BlankLineRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static int Load(MainWindow reader, string path, bool remove, Action<Window>? inspect = null)
        => RespondDuring(() => Call(reader, "LoadFile", path), remove, inspect);

    private static int RespondDuring(Action action, bool remove, Action<Window>? inspect = null)
    {
        var prompts = 0;
        var pending = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var preview = Application.Current.Windows.Cast<Window>().Single(window => window.GetType().Name == "BlankLinePreviewWindow");
            prompts++;
            inspect?.Invoke(preview);
            ((Button)preview.FindName(remove ? "RemoveButton" : "KeepButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        try { action(); }
        finally { pending.Abort(); }
        return prompts;
    }

    internal static void Run(Action<bool, string> check)
    {
        CheckDocuments(check);
        var path = Path.Combine(Path.GetTempPath(), $"FloatingTxtReader-cleanup-{Guid.NewGuid():N}.txt");
        var second = path + "-second.txt";
        var plain = path + "-plain.txt";
        var empty = path + "-blank.txt";
        var legacyEncoding = path + "-gb18030.txt";
        const string content = "第一章 开始\r\n\r\n \t\r\n  正文一\r\n正文二";
        File.WriteAllText(path, content);
        File.WriteAllText(second, "第二章 继续\n\n另一段\n");
        File.WriteAllText(plain, "无空行\n正文\n");
        File.WriteAllText(empty, "\n\t \n　");
        var originalBytes = File.ReadAllBytes(path);
        var state = new AppState { CloseHotkey = "", CharactersPerLine = 20 };
        var reader = CreateReader(state);
        File.WriteAllBytes(legacyEncoding, Encoding.GetEncoding("GB18030").GetBytes("第一章 标题\r\n\r\n中文正文"));
        try
        {
            check(Load(reader, path, false, preview =>
            {
                check(preview.AllowsTransparency && preview.WindowStyle == WindowStyle.None && preview.FontSize == 12 && preview.Width == 500,
                    "Blank-line preview shares the compact tray-menu dialog theme");
                var before = ((TextBlock)preview.FindName("BeforeText")).Text;
                var after = ((TextBlock)preview.FindName("AfterText")).Text;
                check(before.Contains(Environment.NewLine + Environment.NewLine) && !after.Contains(Environment.NewLine + Environment.NewLine) && after.Contains("  正文一"),
                    "Before/after preview removes blank lines without stripping indentation");
                check(preview.ActualHeight < 400 && ((Button)preview.FindName("KeepButton")).IsCancel,
                    "Preview stays compact and cancel defaults to keeping the original view");
            }) == 1, "Opening a TXT with blank lines asks once using a before/after preview");
            check(Lines(reader).SequenceEqual(new[] { "第一章 开始", "", " \t", "  正文一", "正文二" }),
                "Declining cleanup preserves every original line and whitespace");
            check(Load(reader, path, true) == 0 && Lines(reader).Count == 5,
                "Declined choice is reused on a same-file reload without asking again");
            var declinedState = JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(state))!;
            var declinedRestart = CreateReader(declinedState);
            try
            {
                check(RespondDuring(declinedRestart.Show, true) == 0 && Lines(declinedRestart).Count == 5,
                    "Startup also remembers declining cleanup for the current file");
            }
            finally { DisposeReader(declinedRestart); }
            check(RespondDuring(() => Call(reader, "ApplySettings", new AppState { CharactersPerLine = 10 }), true) == 0,
                "Changing wrapping does not repeat the active file's prompt");
            check(Load(reader, second, true) == 1 && Lines(reader).SequenceEqual(new[] { "第二章 继续", "另一段" }),
                "Switching to another TXT asks again and approval removes its blank rows");
            check(Load(reader, second.ToUpperInvariant(), false) == 0 && Lines(reader).Count == 2,
                "Case-insensitive aliases of the same Windows path retain the active choice");
            check(Load(reader, path, true) == 1 && Lines(reader).SequenceEqual(new[] { "第一章 开始", "  正文一", "正文二" }),
                "Returning to a previously selected file asks again instead of caching a per-file history");
            Call(reader, "GoToLine", 1);
            check(state.CurrentSourceOffset == content.IndexOf("  正文一", StringComparison.Ordinal),
                "Filtered progress is anchored to the unchanged original text");
            var resumedState = JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(state))!;
            var resumed = CreateReader(resumedState);
            try
            {
                check(RespondDuring(resumed.Show, false) == 0 && Lines(resumed)[resumedState.CurrentLine] == "  正文一" && Lines(resumed).Count == 3,
                    "Startup reuses the current file's accepted choice and restores the same source text");
            }
            finally { DisposeReader(resumed); }
            check(RespondDuring(() => Call(reader, "ApplySettings", new AppState { CharactersPerLine = 2 }), false) == 0 &&
                state.CurrentSourceOffset == content.IndexOf("  正文一", StringComparison.Ordinal),
                "Rewrapping a cleaned document keeps its source-text position without another prompt");
            check(Lines(reader).Count(line => line == "  ") == 1,
                "Whitespace chunks belonging to an indented nonblank source line are not removed");
            Call(reader, "ApplySettings", new AppState { CharactersPerLine = 20 });
            var chapters = (List<(int Line, string Title)>)Field(reader, "_chapters");
            check(chapters.Count == 1 && chapters[0] == (0, "第一章 开始"),
                "Chapter indexing uses the cleaned display-line positions");
            var keptLines = Lines(reader);
            Load(reader, path + "-missing", true);
            check(ReferenceEquals(keptLines, Lines(reader)) && state.FilePath == path && Load(reader, path, false) == 0,
                "An unreadable selection preserves the previous file, progress and prompt decision");
            check(Load(reader, plain, true) == 0 && Lines(reader).SequenceEqual(new[] { "无空行", "正文", "" }),
                "Files without physical blank lines skip prompting, including a single trailing newline");
            check(Load(reader, path, false, preview =>
            {
                ((Button)preview.Template.FindName("DialogCloseButton", preview)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }) == 1 && Lines(reader).Count == 5 && Load(reader, path, true) == 0,
                "Closing the preview counts as declining once for the active file");
            check(Load(reader, second, false, preview =>
            {
                var source = (HwndSource)PresentationSource.FromVisual(preview);
                PostMessage(source.Handle, 0x0100, new IntPtr(0x1B), new IntPtr(1));
                var frame = new DispatcherFrame();
                var timeout = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                timeout.Tick += (_, _) => { timeout.Stop(); frame.Continue = false; };
                timeout.Start();
                Dispatcher.PushFrame(frame);
                check(!preview.IsVisible, "Native Escape closes the blank-line preview without consent");
            }) == 1 && Lines(reader).Any(string.IsNullOrEmpty), "Escape leaves the source's blank rows in the reader");
            check(Load(reader, empty, true) == 1 && Lines(reader).SequenceEqual(new[] { "" }) && state.CurrentLine == 0,
                "Cleaning an entirely blank file leaves a safe empty document");
            check(File.ReadAllBytes(path).SequenceEqual(originalBytes), "Approving, declining and reopening never modify the TXT bytes");
            check(Load(reader, legacyEncoding, true) == 1 && Lines(reader).SequenceEqual(new[] { "第一章 标题", "中文正文" }),
                "GB18030 decoding still feeds the same safe blank-line filtering flow");
            var legacy = CreateReader(new AppState { CloseHotkey = "", FilePath = path, CurrentLine = 3, CharactersPerLine = 20 });
            try
            {
                check(RespondDuring(legacy.Show, true) == 1 && Lines(legacy)[((AppState)Field(legacy, "_state")).CurrentLine] == "  正文一",
                    "Legacy saved line numbers migrate to source offsets before removing blank lines");
            }
            finally { DisposeReader(legacy); }
        }
        finally
        {
            DisposeReader(reader);
            File.Delete(path);
            File.Delete(second);
            File.Delete(plain);
            File.Delete(empty);
            File.Delete(legacyEncoding);
        }
    }

    private static void CheckDocuments(Action<bool, string> check)
    {
        var samples = new[] { "", "\n", "a\n", "a\r\n\r\nb", " \t\n　\r正文", "    正文\n\n尾段" };
        foreach (var width in new[] { 1, 2, 5, 20 })
        {
            check(samples.All(content =>
            {
                var expected = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .SelectMany(line => Enumerable.Range(0, (line.Length + width - 1) / width)
                        .Select(index => line.Substring(index * width, Math.Min(width, line.Length - index * width)))).ToList();
                if (expected.Count == 0) expected.Add("");
                var document = ReadingDocument.Create(content, width);
                document.RemoveBlankLines();
                var matches = document.Lines.SequenceEqual(expected) && document.Lines.SequenceEqual(ReadingDocument.Create(content, width, true).Lines);
                document.RemoveBlankLines();
                return matches && document.Lines.SequenceEqual(expected) && Enumerable.Range(0, document.Lines.Count)
                    .All(line => document.FindLine(document.SourceOffset(line)) == line);
            }), $"Blank-line filtering preserves text, mixed newlines and progress anchors at width {width}");
        }
        var manyBlanks = ReadingDocument.Create(new string('\n', 20000) + "第一章 长篇\n正文", 20);
        check(manyBlanks.BlankLineCount == 20000 && manyBlanks.BeforePreview.Split(Environment.NewLine).Length <= 8 &&
            manyBlanks.AfterPreview.Contains("正文"), "Long blank runs still produce a bounded preview with meaningful following text");
        var progress = ReadingDocument.Create("开头\n\n下一段", 2, true);
        check(progress.FindLine(3) == 1 && progress.FindLine(int.MaxValue) == progress.Lines.Count - 1,
            "Positions on removed blank lines advance to following content and EOF stays bounded");
    }

    internal static void RenderPreview(string path)
    {
        var document = ReadingDocument.Create("第一章 春日来信\r\n\r\n　\r\n  窗外的风轻轻吹过。\r\n她翻开了书的下一页。", 20);
        var preview = new BlankLinePreviewWindow("示例小说.txt", document);
        try
        {
            preview.Show();
            preview.UpdateLayout();
            var surface = (FrameworkElement)preview.Template.FindName("DialogSurface", preview);
            SettingsAppearanceRegression.Save(SettingsAppearanceRegression.Render(surface), path);
            SettingsAppearanceRegression.Save(SettingsAppearanceRegression.Render(surface, 1.5), path + "-150.png");
            Console.WriteLine($"BLANK-PREVIEW {surface.ActualWidth:0}x{surface.ActualHeight:0}");
        }
        finally { preview.Close(); }
    }

    private static MainWindow CreateReader(AppState state)
    {
        var reader = new MainWindow(state);
        ((DispatcherTimer)Field(reader, "_saveTimer")).Interval = TimeSpan.FromDays(1);
        return reader;
    }

    private static void DisposeReader(MainWindow reader)
    {
        ((DispatcherTimer)Field(reader, "_saveTimer")).Stop();
        Call(reader, "UnregisterHotkeys");
        ((IDisposable)Field(reader, "_trayIcon")).Dispose();
        reader.GetType().GetField("_isExiting", Private)!.SetValue(reader, true);
        reader.Close();
    }

    private static List<string> Lines(MainWindow reader) => (List<string>)Field(reader, "_lines");

    private static object Field(object target, string name) => target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
