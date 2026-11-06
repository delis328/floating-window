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
            var menuItems = ((IEnumerable)menu.GetType().GetProperty("Items")!.GetValue(menu)!).Cast<object>().ToArray();
            var settingsItem = menuItems.SingleOrDefault(item => (string)item.GetType().GetProperty("Text")!.GetValue(item)! == "设置");
            check(settingsItem is not null, "Tray menu groups infrequent settings into one submenu");
            var settingsItems = ((IEnumerable)settingsItem!.GetType().GetProperty("DropDownItems")!.GetValue(settingsItem)!).Cast<object>().ToArray();
            check(settingsItems.Select(item => (string)item.GetType().GetProperty("Text")!.GetValue(item)!)
                .SequenceEqual(new[] { "阅读设置...", "快捷键设置..." }), "Settings submenu preserves both settings entries");
            Field<DispatcherTimer>(reader, "_saveTimer").Stop();
            reader.Show();
            settingsItems[0].GetType().GetMethod("PerformClick")!.Invoke(settingsItems[0], null);
            var readingSettings = Field<System.Windows.Window>(reader, "_settingsWindow");
            check(readingSettings.IsVisible, "Reading settings remain accessible through submenu");
            readingSettings.Close();
            settingsItems[1].GetType().GetMethod("PerformClick")!.Invoke(settingsItems[1], null);
            var hotkeySettings = Field<System.Windows.Window>(reader, "_hotkeySettingsWindow");
            check(hotkeySettings.IsVisible, "Hotkey settings remain accessible through submenu");
            hotkeySettings.Close();
            check(menuItems.Count(item => item.GetType().Name == "ToolStripSeparator") == 3,
                "Tray menu separates current file, reading, window controls and utilities");
            var renderedSize = (System.Drawing.Size)menu.GetType().GetMethod("GetPreferredSize")!
                .Invoke(menu, new object[] { System.Drawing.Size.Empty })!;
            menu.GetType().GetProperty("Size")!.SetValue(menu, renderedSize);
            menu.GetType().GetMethod("PerformLayout", Type.EmptyTypes)!.Invoke(menu, null);
            ForceHandles(menu);
            using (var bitmap = new System.Drawing.Bitmap(renderedSize.Width, renderedSize.Height))
            {
                menu.GetType().GetMethod("DrawToBitmap")!.Invoke(menu,
                    new object[] { bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, renderedSize) });
                check(menuItems.Where(item => item.GetType().Name == "ToolStripSeparator").All(item =>
                {
                    var bounds = (System.Drawing.Rectangle)item.GetType().GetProperty("Bounds")!.GetValue(item)!;
                    var y = bounds.Top + bounds.Height / 2;
                    var center = bitmap.GetPixel(bounds.Left + bounds.Width / 2, y);
                    var edge = bitmap.GetPixel(bounds.Left + 1, y);
                    return edge.R - center.R >= 12 && edge.G - center.G >= 12 && edge.B - center.B >= 12;
                }), "All three group separators draw visible inset lines on the rounded menu");
            }
            check((string)menuItems[^1].GetType().GetProperty("Text")!.GetValue(menuItems[^1])! == "退出应用",
                "Exit stays last in tray menu");
            var settingsDropdown = settingsItem.GetType().GetProperty("DropDown")!.GetValue(settingsItem)!;
            ForceHandles(settingsDropdown);
            var control = host.GetType().GetProperty("Control")!.GetValue(host)!;
            ForceHandles(control);
            var menuRegion = menu.GetType().GetProperty("Region")!.GetValue(menu)!;
            check(menuRegion is not null && !(bool)menuRegion.GetType().GetMethod("IsVisible", new[] { typeof(float), typeof(float) })!
                .Invoke(menuRegion, new object[] { 0f, 0f })!, "Rounded tray menu excludes outer corners from its hit region");
            var childButtons = ((IEnumerable)control.GetType().GetProperty("Controls")!.GetValue(control)!).Cast<object>().ToArray();
            check(childButtons.All(button =>
            {
                var region = button.GetType().GetProperty("Region")!.GetValue(button);
                return region is not null && !(bool)region.GetType().GetMethod("IsVisible", new[] { typeof(float), typeof(float) })!
                    .Invoke(region, new object[] { 0f, 0f })!;
            }), "Chapter buttons have rounded native hit regions");
            var textColor = (System.Drawing.Color)menu.GetType().GetProperty("ForeColor")!.GetValue(menu)!;
            var background = (System.Drawing.Color)menu.GetType().GetProperty("BackColor")!.GetValue(menu)!;
            var disabledText = (System.Drawing.Color)typeof(TrayMenuStyle).GetField("Muted", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var buttonBackground = (System.Drawing.Color)typeof(TrayMenuStyle).GetField("ButtonSurface", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            check(Contrast(textColor, background) >= 4.5 && Contrast(disabledText, buttonBackground) >= 4.5 && childButtons.All(button =>
                Contrast((System.Drawing.Color)button.GetType().GetProperty("ForeColor")!.GetValue(button)!,
                    (System.Drawing.Color)button.GetType().GetProperty("BackColor")!.GetValue(button)!) >= 4.5),
                "Soft menu and enabled/disabled button text retain readable contrast");
            var buttons = ((IEnumerable)control.GetType().GetProperty("Controls")!.GetValue(control)!).Cast<object>()
                .OrderBy(button => ((System.Drawing.Rectangle)button.GetType().GetProperty("Bounds")!.GetValue(button)!).X)
                .Select(button => (System.Drawing.Rectangle)button.GetType().GetProperty("Bounds")!.GetValue(button)!).ToArray();
            check(buttons.Length == 3 && buttons[1].Left - buttons[0].Right >= 4 && buttons[2].Left - buttons[1].Right >= 4 &&
                buttons[1].Width == buttons[0].Width * 2 && Math.Abs(buttons[2].Width - buttons[0].Width) <= 3,
                "Chapter buttons keep 1:2:1 proportions with gaps between hit areas");
            RebuildMenu();
            check((bool)settingsDropdown.GetType().GetProperty("IsDisposed")!.GetValue(settingsDropdown)!,
                "Rebuilding tray menu also releases settings submenu controls");
            check((bool)host.GetType().GetProperty("IsDisposed")!.GetValue(host)! &&
                (bool)control.GetType().GetProperty("IsDisposed")!.GetValue(control)!, "Rebuilding tray menu disposes old host and native controls");
            host = RebuildMenu();
            ForceHandles(host.GetType().GetProperty("Control")!.GetValue(host)!);
            var handlesBefore = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
            var gdiBefore = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
            for (var i = 0; i < 100; i++)
            {
                host = RebuildMenu();
                ForceHandles(host.GetType().GetProperty("Control")!.GetValue(host)!);
            }
            var after = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
            check(after <= handlesBefore + 8, $"Tray menu USER handles stay bounded after 100 rebuilds ({handlesBefore} -> {after})");
            var gdiAfter = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
            check(gdiAfter <= gdiBefore + 8, $"Rounded menu GDI resources stay bounded after 100 rebuilds ({gdiBefore} -> {gdiAfter})");
            var originalPath = state.FilePath;
            var normalSize = (System.Drawing.Size)menu.GetType().GetMethod("GetPreferredSize")!.Invoke(menu, new object[] { System.Drawing.Size.Empty })!;
            state.FilePath = new string('长', 80) + ".txt";
            RebuildMenu();
            var fileItem = ((IEnumerable)menu.GetType().GetProperty("Items")!.GetValue(menu)!).Cast<object>().First();
            check(((string)fileItem.GetType().GetProperty("Text")!.GetValue(fileItem)!).Length < 40 &&
                ((string)fileItem.GetType().GetProperty("ToolTipText")!.GetValue(fileItem)!).Contains(state.FilePath),
                "Long TXT names stay compact while tooltip preserves full path");
            var longNameSize = (System.Drawing.Size)menu.GetType().GetMethod("GetPreferredSize")!.Invoke(menu, new object[] { System.Drawing.Size.Empty })!;
            check(longNameSize.Width <= normalSize.Width + 4, "Long TXT names do not widen tray menu");
            state.FilePath = originalPath;

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
    private static double Contrast(System.Drawing.Color first, System.Drawing.Color second)
    {
        static double Linear(byte value) => value / 255d <= .04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
        static double Luminance(System.Drawing.Color color) => .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, uint flag);
}
