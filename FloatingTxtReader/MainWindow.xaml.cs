using System.IO;
using System.Diagnostics;
using System.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace FloatingTxtReader;

public partial class MainWindow : Window
{
    private const int WheelDeltaPerLine = 120;
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private static readonly Regex ChapterHeadingPattern = new(
        @"^\s*(?:(?:\u7B2C\s*)?(?:[0-9\uFF10-\uFF19]+|[\u96F6\u3007\u4E00\u4E8C\u4E09\u56DB\u4E94\u516D\u4E03\u516B\u4E5D\u5341\u767E\u5343\u4E07\u4EBF\u4E24]+)\s*\u5377\s*)?(?:\u7B2C\s*)?(?:[0-9\uFF10-\uFF19]+|[\u96F6\u3007\u4E00\u4E8C\u4E09\u56DB\u4E94\u516D\u4E03\u516B\u4E5D\u5341\u767E\u5343\u4E07\u4EBF\u4E24]+)\s*(?:\u7AE0|\u8BDD)(?=\s|$|[\uFF1A:\u3001.\uFF0E])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly AppState _state;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _scrollTimer;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly DispatcherTimer _fastForwardTimer;
    private readonly Stopwatch _fastForwardWatch = new();
    private readonly HashSet<int> _registeredHotkeys = [];
    private TimeSpan _lastFastForwardStep;
    private List<string> _lines = [];
    private List<(int Line, string Title)> _chapters = [];
    private bool _isExiting;
    private SettingsWindow? _settingsWindow;
    private HotkeySettingsWindow? _hotkeySettingsWindow;
    private ChapterListWindow? _chapterListWindow;
    private int _wheelRemainder;
    private int _pendingScrollLines;
    private bool _keepTrayMenuOpen;
    private IntPtr _windowHandle;

    public MainWindow(AppState state)
    {
        _state = state;
        _state.VisibleLines = Math.Clamp(_state.VisibleLines, 1, 30);
        _state.CharactersPerLine = Math.Clamp(_state.CharactersPerLine, 1, 500);
        _state.FastForwardLinesPerSecond = Math.Clamp(_state.FastForwardLinesPerSecond, 1, AppState.MaxFastForwardLinesPerSecond);
        _state.WindowWidth = Math.Max(_state.WindowWidth, 80);
        _state.WindowHeight = Math.Max(_state.WindowHeight, 56);
        if (!string.IsNullOrWhiteSpace(_state.CloseHotkey) && !Hotkeys.TryParse(_state.CloseHotkey, out _, out _))
        {
            _state.CloseHotkey = AppState.DefaultCloseHotkey;
        }
        InitializeComponent();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        SourceInitialized += Window_SourceInitialized;

        Left = state.WindowLeft;
        Top = state.WindowTop;
        Width = state.WindowWidth;
        Height = state.WindowHeight;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SettingsStore.Save(_state);
        };
        _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _scrollTimer.Tick += (_, _) => FlushPendingScroll();
        _fastForwardTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        _fastForwardTimer.Tick += (_, _) => FastForwardTick(IsFastForwardKeyHeld());
        _trayIcon = CreateTrayIcon();

        ApplyAppearance();
        if (!string.IsNullOrWhiteSpace(state.FilePath) && File.Exists(state.FilePath))
        {
            LoadFile(state.FilePath);
        }
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var tray = new Forms.NotifyIcon
        {
            Icon = LoadApplicationIcon(),
            Text = "TXT \u60AC\u6D6E\u9605\u8BFB\u5668",
            Visible = true,
        };
        tray.ContextMenuStrip = new Forms.ContextMenuStrip();
        tray.ContextMenuStrip.Opening += (_, _) => BuildTrayMenu(tray.ContextMenuStrip!);
        tray.ContextMenuStrip.Closing += (_, e) =>
        {
            if (_keepTrayMenuOpen)
            {
                e.Cancel = true;
                _keepTrayMenuOpen = false;
            }
        };
        tray.DoubleClick += (_, _) => ShowSettings();
        return tray;
    }

    private static System.Drawing.Icon LoadApplicationIcon()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Fall back to the system icon when running outside the published exe.
        }

        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }

    private void BuildTrayMenu(Forms.ContextMenuStrip menu)
    {
        StopFastForward();
        menu.Items.Clear();
        var currentFileLabel = string.IsNullOrWhiteSpace(_state.FilePath)
            ? "\u5F53\u524D TXT\uFF1A\u672A\u7ED1\u5B9A"
            : $"\u5F53\u524D TXT\uFF1A{Path.GetFileName(_state.FilePath)}";
        menu.Items.Add(currentFileLabel, null, (_, _) => ChooseFile());
        menu.Items.Add("\u9605\u8BFB\u8BBE\u7F6E...", null, (_, _) => ShowSettings());
        menu.Items.Add("快捷键设置...", null, (_, _) => ShowHotkeySettings());
        var chapterJumpControl = new ChapterJumpControl();
        chapterJumpControl.SetAvailable(FindPreviousChapterLine() >= 0, FindNextChapterLine() >= 0);
        chapterJumpControl.MouseDown += (_, _) => _keepTrayMenuOpen = true;
        chapterJumpControl.JumpRequested += count =>
        {
            _keepTrayMenuOpen = true;
            GoToChapterOffset(count);
            chapterJumpControl.SetAvailable(FindPreviousChapterLine() >= 0, FindNextChapterLine() >= 0);
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => _keepTrayMenuOpen = false));
        };
        menu.Items.Add(new Forms.ToolStripControlHost(chapterJumpControl)
        {
            AutoSize = false,
            Size = new System.Drawing.Size(240, 30),
            Padding = new Forms.Padding(0),
        });
        menu.Items.Add(new Forms.ToolStripMenuItem("\u7AE0\u8282\u5217\u8868...", null, (_, _) => ShowChapterList())
        {
            Enabled = _chapters.Count > 0,
        });
        menu.Items.Add(_state.IsLocked ? "\u89E3\u9501\u7A97\u53E3" : "\u9501\u5B9A\u7A97\u53E3", null, (_, _) => ToggleLock());
        menu.Items.Add("\u590D\u4F4D\u4F4D\u7F6E", null, (_, _) => ResetWindowPosition());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("\u9000\u51FA", null, (_, _) => ExitApplication());
    }

    private void ChooseFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true)
        {
            _state.FilePath = dialog.FileName;
            _state.CurrentLine = 0;
            LoadFile(dialog.FileName);
            SaveState();
        }
    }

    private void LoadFile(string filePath)
    {
        try
        {
            var content = ReadTextFile(filePath);
            _lines = WrapLines(content, Math.Max(1, _state.CharactersPerLine));
            BuildChapterIndex();
            _chapterListWindow?.Close();
            _chapterListWindow = null;
            if (_state.CurrentLine == 0)
            {
                _state.CurrentLine = FindFirstContentLine();
            }
            GoToLine(_state.CurrentLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            _lines = [];
            _chapters = [];
            ReaderText.Text = $"\u65E0\u6CD5\u8BFB\u53D6\u6587\u672C\u6587\u4EF6: {ex.Message}";
        }
    }

    private static string ReadTextFile(string path)
    {
        try
        {
            using var reader = new StreamReader(path, new UTF8Encoding(false, true), true);
            return reader.ReadToEnd();
        }
        catch (DecoderFallbackException)
        {
            return File.ReadAllText(path, Encoding.GetEncoding("GB18030"));
        }
    }

    private static List<string> WrapLines(string content, int charactersPerLine)
    {
        var lines = new List<string>();
        foreach (var sourceLine in content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (sourceLine.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            for (var index = 0; index < sourceLine.Length; index += charactersPerLine)
            {
                lines.Add(sourceLine.Substring(index, Math.Min(charactersPerLine, sourceLine.Length - index)));
            }
        }
        return lines;
    }

    private int FindFirstContentLine()
    {
        for (var index = 0; index < _lines.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(_lines[index]))
            {
                return index;
            }
        }
        return 0;
    }

    private void BuildChapterIndex()
    {
        _chapters = [];
        for (var index = 0; index < _lines.Count; index++)
        {
            var candidate = _lines[index].TrimStart('\uFEFF').TrimStart();
            if (ChapterHeadingPattern.IsMatch(candidate))
            {
                _chapters.Add((index, candidate.Trim()));
            }
        }
    }

    private int FindNextChapterLine()
    {
        foreach (var (line, _) in _chapters)
        {
            if (line > _state.CurrentLine)
            {
                return line;
            }
        }
        return -1;
    }

    private int FindPreviousChapterLine() => FindPreviousChapterLineBefore(_state.CurrentLine);

    private void GoToChapterOffset(int chapterOffset)
    {
        var targetLine = _state.CurrentLine;
        if (chapterOffset < 0)
        {
            for (var index = 0; index < -chapterOffset; index++)
            {
                var previousChapterLine = FindPreviousChapterLineBefore(targetLine);
                if (previousChapterLine < 0)
                {
                    break;
                }
                targetLine = previousChapterLine;
            }
        }
        else
        {
            for (var index = 0; index < chapterOffset; index++)
            {
                var nextChapterLine = _chapters.FirstOrDefault(chapter => chapter.Line > targetLine, (Line: -1, Title: string.Empty)).Line;
                if (nextChapterLine < 0)
                {
                    break;
                }
                targetLine = nextChapterLine;
            }
        }

        if (targetLine != _state.CurrentLine)
        {
            GoToLine(targetLine);
        }
    }

    private int FindPreviousChapterLineBefore(int line)
    {
        for (var index = _chapters.Count - 1; index >= 0; index--)
        {
            if (_chapters[index].Line < line)
            {
                return _chapters[index].Line;
            }
        }
        return -1;
    }

    private void RenderLines()
    {
        if (_lines.Count == 0)
        {
            ReaderText.Text = "\u8BF7\u4ECE\u6258\u76D8\u83DC\u5355\u9009\u62E9 TXT \u6587\u4EF6";
            return;
        }

        var maxLine = Math.Max(0, _lines.Count - Math.Max(1, _state.VisibleLines));
        _state.CurrentLine = Math.Clamp(_state.CurrentLine, 0, maxLine);
        if (_state.VisibleLines == 1)
        {
            ReaderText.Text = _lines[_state.CurrentLine];
            return;
        }

        ReaderText.Text = string.Join(Environment.NewLine, _lines.Skip(_state.CurrentLine).Take(_state.VisibleLines));
    }

    private void FitWindowToCharacters()
    {
        var characterCount = Math.Max(1, _state.CharactersPerLine);
        var typeface = new Typeface(
            ReaderText.FontFamily,
            ReaderText.FontStyle,
            ReaderText.FontWeight,
            ReaderText.FontStretch);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var cjkWidth = new FormattedText(
            "\u6C49",
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            ReaderText.FontSize,
            System.Windows.Media.Brushes.Transparent,
            pixelsPerDip).WidthIncludingTrailingWhitespace;
        var latinWidth = new FormattedText(
            "W",
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            ReaderText.FontSize,
            System.Windows.Media.Brushes.Transparent,
            pixelsPerDip).WidthIncludingTrailingWhitespace;
        var widthPerCharacter = Math.Max(cjkWidth, latinWidth);
        var requiredWidth = Math.Ceiling(
            widthPerCharacter * characterCount + Surface.Padding.Left + Surface.Padding.Right +
            Surface.BorderThickness.Left + Surface.BorderThickness.Right + 2);
        Width = Math.Max(80, requiredWidth);

        var handleHeight = DragHandleHitArea.Visibility == Visibility.Visible ? DragHandleHitArea.Height : 0;
        var requiredHeight = Math.Ceiling(
            handleHeight + ReaderText.LineHeight * Math.Max(1, _state.VisibleLines) +
            Surface.Padding.Top + Surface.Padding.Bottom +
            Surface.BorderThickness.Top + Surface.BorderThickness.Bottom + 2);
        Height = Math.Max(MinHeight, requiredHeight);
    }

    private void GoToLine(int lineIndex)
    {
        _state.CurrentLine = lineIndex;
        RenderLines();
        SaveState();
    }

    private void ToggleLock()
    {
        _state.IsLocked = !_state.IsLocked;
        Opacity = 1;
        SetReaderVisible(!_state.IsLocked || IsMouseOver);
        UpdateLockVisual();
        FitWindowToCharacters();
        SaveState();
    }

    private void ResetWindowPosition()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Bottom - Height - 96;
        SaveState();
    }

    private void ShowSettings()
    {
        StopFastForward();
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_state, ApplySettings);
        _settingsWindow.Owner = this;
        _settingsWindow.Show();
    }

    private void ShowChapterList()
    {
        StopFastForward();
        if (_chapters.Count == 0)
        {
            return;
        }

        if (_chapterListWindow is { IsVisible: true })
        {
            _chapterListWindow.Activate();
            return;
        }

        _chapterListWindow = new ChapterListWindow(_chapters, _state.CurrentLine, GoToChapterLine);
        _chapterListWindow.Owner = this;
        _chapterListWindow.Show();
    }

    private void GoToChapterLine(int line)
    {
        if (line >= 0 && line < _lines.Count)
        {
            GoToLine(line);
        }
    }

    private string? ApplySettings(AppState updatedState)
    {
        _state.VisibleLines = updatedState.VisibleLines;
        _state.CharactersPerLine = updatedState.CharactersPerLine;
        _state.FontFamily = updatedState.FontFamily;
        _state.FontSize = updatedState.FontSize;
        _state.FontWeight = updatedState.FontWeight;
        _state.FontStyle = updatedState.FontStyle;
        _state.ForegroundColor = updatedState.ForegroundColor;
        _state.TextOpacity = updatedState.TextOpacity;
        _state.LineHeightMultiplier = updatedState.LineHeightMultiplier;
        if (!string.IsNullOrWhiteSpace(_state.FilePath) && File.Exists(_state.FilePath))
        {
            LoadFile(_state.FilePath);
        }
        ApplyAppearance();
        RenderLines();
        SaveState();
        return null;
    }

    private void ShowHotkeySettings()
    {
        if (_hotkeySettingsWindow is { IsVisible: true })
        {
            _hotkeySettingsWindow.Activate();
            return;
        }

        StopFastForward();
        UnregisterHotkeys();
        _hotkeySettingsWindow = new HotkeySettingsWindow(_state, ApplyHotkeys);
        _hotkeySettingsWindow.Owner = this;
        _hotkeySettingsWindow.Closed += (_, _) =>
        {
            _hotkeySettingsWindow = null;
            if (!_isExiting)
            {
                UnregisterHotkeys();
                RegisterHotkeys();
            }
        };
        _hotkeySettingsWindow.Show();
    }

    private string? ApplyHotkeys(AppState updatedState)
    {
        var error = Hotkeys.Validate(updatedState);
        if (error is not null) return error;
        StopFastForward();
        UnregisterHotkeys();
        foreach (var binding in Hotkeys.GetBindings(updatedState))
        {
            if (string.IsNullOrWhiteSpace(binding.Shortcut)) continue;
            if (!TryRegisterHotkey(binding))
            {
                UnregisterHotkeys();
                return $"{binding.Label}：{binding.Shortcut} 已被其他程序占用或被系统保留，请更换。";
            }
        }
        foreach (var binding in Hotkeys.GetBindings(updatedState))
        {
            Hotkeys.SetShortcut(_state, binding.Id, binding.Shortcut);
        }
        _state.FastForwardLinesPerSecond = updatedState.FastForwardLinesPerSecond;
        SaveState();
        return null;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_windowHandle)?.AddHook(WindowMessageHook);
        RegisterHotkeys();
    }

    private void RegisterHotkeys()
    {
        var unavailable = new List<string>();
        foreach (var binding in Hotkeys.GetBindings(_state))
        {
            if (!string.IsNullOrWhiteSpace(binding.Shortcut) && !TryRegisterHotkey(binding))
                unavailable.Add($"{binding.Label}：{binding.Shortcut}");
        }
        if (unavailable.Count > 0)
        {
            Dispatcher.BeginInvoke(new Action(() => System.Windows.MessageBox.Show(
                "以下快捷键无效、重复或被占用，未能启用：\n" + string.Join("\n", unavailable) +
                "\n请从托盘菜单打开“快捷键设置”修改。", "快捷键提示", MessageBoxButton.OK, MessageBoxImage.Warning)));
        }
    }

    private bool TryRegisterHotkey(HotkeyBinding binding)
    {
        if (_registeredHotkeys.Contains(binding.Id)) return true;
        if (_windowHandle == IntPtr.Zero || !Hotkeys.TryParse(binding.Shortcut, out var modifiers, out var key) ||
            !RegisterHotKey(_windowHandle, binding.Id, modifiers | ModNoRepeat, key)) return false;
        _registeredHotkeys.Add(binding.Id);
        return true;
    }

    private void UnregisterHotkeys()
    {
        foreach (var id in _registeredHotkeys) UnregisterHotKey(_windowHandle, id);
        _registeredHotkeys.Clear();
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var id = (int)wParam.ToInt64();
        if (message != WmHotkey || !_registeredHotkeys.Contains(id)) return IntPtr.Zero;
        handled = true;
        if (_hotkeySettingsWindow is { IsVisible: true }) return IntPtr.Zero;
        if (id != Hotkeys.FastForward) StopFastForward();
        switch (id)
        {
            case Hotkeys.Close: ExitApplication(); break;
            case Hotkeys.Hide:
                if (IsVisible) Hide();
                else Show();
                break;
            case Hotkeys.PreviousLine when CanNavigateWithHotkey(): MoveByLine(-1); break;
            case Hotkeys.NextLine when CanNavigateWithHotkey(): MoveByLine(1); break;
            case Hotkeys.PreviousChapter when CanNavigateWithHotkey(): GoToChapterOffset(-1); break;
            case Hotkeys.NextChapter when CanNavigateWithHotkey(): GoToChapterOffset(1); break;
            case Hotkeys.FastForward when CanNavigateWithHotkey(): StartFastForward(); break;
        }
        return IntPtr.Zero;
    }

    private bool CanNavigateWithHotkey() => IsVisible && _lines.Count > 0 &&
        _settingsWindow is not { IsVisible: true } && _chapterListWindow is not { IsVisible: true };

    private bool MoveByLine(int offset)
    {
        var maxLine = Math.Max(0, _lines.Count - Math.Max(1, _state.VisibleLines));
        var line = Math.Clamp(_state.CurrentLine + offset, 0, maxLine);
        if (line == _state.CurrentLine) return false;
        GoToLine(line);
        return true;
    }

    private void StartFastForward()
    {
        StopFastForward();
        if (!IsFastForwardKeyHeld() || !MoveByLine(1)) return;
        _lastFastForwardStep = TimeSpan.Zero;
        _fastForwardWatch.Restart();
        _fastForwardTimer.Start();
    }

    private void FastForwardTick(bool isKeyHeld)
    {
        if (!CanNavigateWithHotkey() || !isKeyHeld)
        {
            StopFastForward();
            return;
        }
        var elapsed = _fastForwardWatch.Elapsed;
        var interval = Hotkeys.FastForwardInterval(_state.FastForwardLinesPerSecond);
        if (elapsed - _lastFastForwardStep < interval) return;
        // Keep fixed time slots; skip missed slots rather than burst through overdue lines.
        _lastFastForwardStep = TimeSpan.FromTicks(elapsed.Ticks - elapsed.Ticks % interval.Ticks);
        if (!MoveByLine(1)) StopFastForward();
    }

    private bool IsFastForwardKeyHeld()
    {
        if (!Hotkeys.TryParse(_state.FastForwardHotkey, out var modifiers, out var key) || !IsKeyDown((int)key)) return false;
        uint heldModifiers = 0;
        if (IsKeyDown(0x12)) heldModifiers |= 1;
        if (IsKeyDown(0x11)) heldModifiers |= 2;
        if (IsKeyDown(0x10)) heldModifiers |= 4;
        if (IsKeyDown(0x5B) || IsKeyDown(0x5C)) heldModifiers |= 8;
        return heldModifiers == modifiers;
    }

    private static bool IsKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private void StopFastForward()
    {
        _fastForwardTimer.Stop();
        _fastForwardWatch.Reset();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private void ApplyAppearance()
    {
        ReaderText.FontFamily = new System.Windows.Media.FontFamily(_state.FontFamily);
        ReaderText.FontSize = Math.Clamp(_state.FontSize, 8, 96);
        ReaderText.FontWeight = _state.FontWeight == "Bold" ? FontWeights.Bold : FontWeights.Normal;
        ReaderText.FontStyle = _state.FontStyle == "Italic" ? FontStyles.Italic : FontStyles.Normal;
        ReaderText.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_state.ForegroundColor));
        SetReaderVisible(!_state.IsLocked || IsMouseOver);
        ReaderText.LineHeight = ReaderText.FontSize * Math.Clamp(_state.LineHeightMultiplier, 1, 3);
        UpdateLockVisual();
        FitWindowToCharacters();
    }

    private void SetReaderVisible(bool visible)
    {
        ReaderText.Opacity = visible ? Math.Clamp(_state.TextOpacity, 0.1, 1) : 0;
    }

    private void UpdateLockVisual()
    {
        if (_state.IsLocked)
        {
            Surface.BorderBrush = System.Windows.Media.Brushes.Transparent;
            Surface.BorderThickness = new Thickness(0);
        }
        else
        {
            Surface.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(210, 230, 238, 248));
            Surface.BorderThickness = new Thickness(2);
        }

        DragHandleHitArea.Visibility = _state.IsLocked ? Visibility.Collapsed : Visibility.Visible;
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_state.IsLocked && e.LeftButton == MouseButtonState.Pressed)
        {
            e.Handled = true;
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // The OS can cancel a drag if focus changes during the gesture.
            }
        }
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_state.IsLocked || !IsMouseOver || _lines.Count == 0)
        {
            return;
        }

        _wheelRemainder += e.Delta;
        var lineDelta = _wheelRemainder / WheelDeltaPerLine;
        if (lineDelta != 0)
        {
            _wheelRemainder %= WheelDeltaPerLine;
            _pendingScrollLines -= lineDelta;
            _scrollTimer.Start();
        }

        e.Handled = true;
    }

    private void FlushPendingScroll()
    {
        _scrollTimer.Stop();
        if (!_state.IsLocked || !IsMouseOver || _pendingScrollLines == 0 || _lines.Count == 0)
        {
            _pendingScrollLines = 0;
            return;
        }

        var maxLine = Math.Max(0, _lines.Count - Math.Max(1, _state.VisibleLines));
        var nextLine = Math.Clamp(_state.CurrentLine + _pendingScrollLines, 0, maxLine);
        _pendingScrollLines = 0;
        if (nextLine == _state.CurrentLine)
        {
            return;
        }

        _state.CurrentLine = nextLine;
        RenderLines();
        SaveState();
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_state.IsLocked)
        {
            SetReaderVisible(true);
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateLockVisual();
        Opacity = 1;
        SetReaderVisible(!_state.IsLocked || IsMouseOver);
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _wheelRemainder = 0;
        _pendingScrollLines = 0;
        _scrollTimer.Stop();
        if (_state.IsLocked)
        {
            SetReaderVisible(false);
        }
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_state.IsLocked && ReaderText.Opacity == 0)
        {
            Window_MouseEnter(sender, e);
        }
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        _state.WindowLeft = Left;
        _state.WindowTop = Top;
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _state.WindowWidth = Width;
        _state.WindowHeight = Height;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        StopFastForward();
        Hide();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _saveTimer.Stop();
        SettingsStore.Save(_state);
        StopFastForward();
        UnregisterHotkeys();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void SaveState()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
