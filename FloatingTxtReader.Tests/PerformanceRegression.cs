using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using FloatingTxtReader;

internal static class PerformanceRegression
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(Action<bool, string> check)
    {
        var wrap = (Func<string, int, List<string>>)typeof(MainWindow)
            .GetMethod("WrapLines", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate(typeof(Func<string, int, List<string>>));
        var samples = new[] { "", "\n", "\r", "\r\n", "\r\r\n", "a\n\n", "a\r\nb\rc\n", "\uFEFF第一卷 第一话 标题\r\n正文", "  \t\r\n\r\n", "甲乙😀丙丁\n123456789" };
        foreach (var width in new[] { 1, 2, 5, 20, 36, 500 })
            check(samples.All(sample => wrap(sample, width).SequenceEqual(ReferenceWrap(sample, width))), $"Preserve newline, empty line and character splitting at width {width}");
        var random = new Random(41);
        var alphabet = "甲乙ab\r\n \t";
        check(Enumerable.Range(0, 300).All(_ =>
        {
            var sample = new string(Enumerable.Range(0, random.Next(120)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            var width = random.Next(1, 40);
            return wrap(sample, width).SequenceEqual(ReferenceWrap(sample, width));
        }), "Randomized wrapping agrees with original implementation");
        var largeText = string.Concat(Enumerable.Repeat(new string('文', 120) + "\r\n", 1000));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var referenceLines = ReferenceWrap(largeText, 20);
        var referenceAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        var optimizedLines = wrap(largeText, 20);
        var optimizedAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
        check(optimizedLines.SequenceEqual(referenceLines) && optimizedAllocation < referenceAllocation * .75,
            $"Wrapping avoids full-text copies and split allocations ({referenceAllocation} -> {optimizedAllocation} bytes)");

        var state = new AppState { CloseHotkey = "", CharactersPerLine = 20 };
        var reader = new MainWindow(state);
        var path = Path.Combine(Path.GetTempPath(), $"FloatingTxtReader-regression-{Guid.NewGuid():N}.txt");
        try
        {
            const string content = "第一卷 第一话 标题\r\n甲乙丙丁戊己庚辛壬癸\r\n\r\n第二卷 第一话：继续\n正文";
            File.WriteAllText(path, content);
            state.FilePath = path;
            Call(reader, "LoadFile", path);
            state.CurrentLine = 1;
            var lines = Field<List<string>>(reader, "_lines");
            var chapters = Field<List<(int Line, string Title)>>(reader, "_chapters");
            check(chapters.Count == 2 && chapters[0].Title == "第一卷 第一话 标题" && chapters[1].Title == "第二卷 第一话：继续",
                "Chapter index retains cross-volume titles");
            using (var lockedFile = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Call(reader, "ApplySettings", new AppState { CharactersPerLine = 20, TextOpacity = .7, VisibleLines = 2, FontSize = 16 });
                check(ReferenceEquals(lines, Field<List<string>>(reader, "_lines")), "Appearance settings do not read a locked TXT or replace wrapped lines");
                check(ReferenceEquals(chapters, Field<List<(int Line, string Title)>>(reader, "_chapters")), "Appearance settings preserve chapter index");
                check(state.CurrentLine == 1 && state.TextOpacity == .7, "Appearance changes preserve progress and apply new values");
            }
            Call(reader, "ApplySettings", new AppState { CharactersPerLine = 6 });
            check(Field<List<string>>(reader, "_lines").SequenceEqual(ReferenceWrap(content, 6)), "Changing character count rebuilds correct lines");
            var expectedChapters = new List<(int Line, string Title)>();
            var pattern = (System.Text.RegularExpressions.Regex)typeof(MainWindow).GetField("ChapterHeadingPattern", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var wrapped = ReferenceWrap(content, 6);
            for (var i = 0; i < wrapped.Count; i++)
            {
                var candidate = wrapped[i].TrimStart('\uFEFF').TrimStart();
                if (pattern.IsMatch(candidate)) expectedChapters.Add((i, candidate.Trim()));
            }
            check(Field<List<(int Line, string Title)>>(reader, "_chapters").SequenceEqual(expectedChapters), "Rebuilt chapter index preserves wrapped-line semantics");

            var tray = Field<object>(reader, "_trayIcon");
            var menu = tray.GetType().GetProperty("ContextMenuStrip")!.GetValue(tray)!;
            object RebuildMenu()
            {
                Call(reader, "BuildTrayMenu", menu);
                var items = (IEnumerable)menu.GetType().GetProperty("Items")!.GetValue(menu)!;
                return items.Cast<object>().Single(item => item.GetType().Name == "ToolStripControlHost");
            }
            var host = RebuildMenu();
            var control = host.GetType().GetProperty("Control")!.GetValue(host)!;
            ForceHandles(control);
            RebuildMenu();
            check((bool)host.GetType().GetProperty("IsDisposed")!.GetValue(host)! &&
                (bool)control.GetType().GetProperty("IsDisposed")!.GetValue(control)!, "Rebuilding tray menu disposes old host and native controls");
            host = RebuildMenu();
            ForceHandles(host.GetType().GetProperty("Control")!.GetValue(host)!);
            var handlesBefore = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
            for (var i = 0; i < 100; i++)
            {
                host = RebuildMenu();
                ForceHandles(host.GetType().GetProperty("Control")!.GetValue(host)!);
            }
            var after = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
            check(after <= handlesBefore + 8, $"Tray menu USER handles stay bounded after 100 rebuilds ({handlesBefore} -> {after})");

            SetField(reader, "_lines", Enumerable.Range(0, 120).Select(i => $"line {i}").ToList());
            state.VisibleLines = 1;
            var headings = new List<(int Line, string Title)> { (10, "第一卷 第一话"), (30, "第一卷 第二话"), (70, "第二卷 第一话") };
            SetField(reader, "_chapters", headings);
            var navigationMatches = true;
            foreach (var current in new[] { 0, 10, 11, 29, 30, 69, 70, 71, 119 })
            {
                state.CurrentLine = current;
                navigationMatches &= (int)Call(reader, "FindPreviousChapterLine")! == headings.Where(c => c.Line < current).Select(c => c.Line).DefaultIfEmpty(-1).Last();
                navigationMatches &= (int)Call(reader, "FindNextChapterLine")! == headings.Where(c => c.Line > current).Select(c => c.Line).DefaultIfEmpty(-1).First();
                foreach (var offset in new[] { -5, -1, 0, 1, 5 })
                {
                    var candidates = offset < 0 ? headings.Where(c => c.Line < current).Reverse().ToList() : headings.Where(c => c.Line > current).ToList();
                    var expected = offset == 0 || candidates.Count == 0 ? current : candidates[Math.Min(Math.Abs(offset), candidates.Count) - 1].Line;
                    state.CurrentLine = current;
                    Call(reader, "GoToChapterOffset", offset);
                    navigationMatches &= state.CurrentLine == expected;
                }
            }
            check(navigationMatches, "Previous/next/five chapter navigation preserves boundaries and cross-volume behavior");
            state.CurrentLine = 10;
            host = RebuildMenu();
            var jump = host.GetType().GetProperty("Control")!.GetValue(host)!;
            Field<Action<int>>(jump, "JumpRequested")(1);
            Field<Action<int>>(jump, "JumpRequested")(1);
            check(state.CurrentLine == 70 && Field<bool>(reader, "_keepTrayMenuOpen"), "Tray chapter buttons still support consecutive cross-volume jumps");
            SetField(reader, "_chapters", new List<(int Line, string Title)>());
            state.CurrentLine = 5;
            Call(reader, "GoToChapterOffset", 5);
            check(state.CurrentLine == 5 && (int)Call(reader, "FindNextChapterLine")! == -1, "Empty chapter index leaves navigation unchanged");

            var list = new ChapterListWindow(headings, 30, _ => { });
            try
            {
                Call(list, "ApplyFilter", "  第二卷  ");
                var visible = Field<List<(int Line, string Title)>>(list, "_visibleChapters");
                check(visible.Count == 1 && visible[0].Line == 70, "Chapter filter trims keyword without changing matches");
                Call(list, "ApplyFilter", " \t ");
                check(visible.Count == headings.Count, "Blank chapter filter restores all chapters");
            }
            finally { list.Close(); }
        }
        finally
        {
            Field<DispatcherTimer>(reader, "_saveTimer").Stop();
            Call(reader, "StopFastForward"); Call(reader, "UnregisterHotkeys");
            var tray = Field<object>(reader, "_trayIcon");
            ((IDisposable)tray.GetType().GetProperty("ContextMenuStrip")!.GetValue(tray)!).Dispose();
            ((IDisposable)tray).Dispose();
            SetField(reader, "_isExiting", true); reader.Close();
            File.Delete(path);
        }
    }

    private static List<string> ReferenceWrap(string content, int width)
    {
        var lines = new List<string>();
        foreach (var line in content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (line.Length == 0) lines.Add(string.Empty);
            for (var i = 0; i < line.Length; i += width) lines.Add(line.Substring(i, Math.Min(width, line.Length - i)));
        }
        return lines;
    }
    private static void ForceHandles(object control)
    {
        _ = control.GetType().GetProperty("Handle")!.GetValue(control);
        foreach (var child in (IEnumerable)control.GetType().GetProperty("Controls")!.GetValue(control)!) ForceHandles(child);
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, uint flag);
}
