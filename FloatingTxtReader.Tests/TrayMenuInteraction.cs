using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FloatingTxtReader;

internal static class TrayMenuInteraction
{
    internal static void Run(Application application)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var state = new AppState { CloseHotkey = "", CharactersPerLine = 20 };
        var reader = new MainWindow(state);
        var menu = (TrayMenu)typeof(MainWindow).GetField("_trayMenu", flags)!.GetValue(reader)!;
        var saveTimer = (DispatcherTimer)typeof(MainWindow).GetField("_saveTimer", flags)!.GetValue(reader)!;
        menu.FileItem.IsEnabled = false;
        menu.ExitItem.IsEnabled = false;
        menu.ReadingSettingsItem.IsEnabled = false;
        menu.HotkeySettingsItem.IsEnabled = false;
        typeof(MainWindow).GetField("_lines", flags)!.SetValue(reader, Enumerable.Range(0, 100).Select(i => $"合成测试文本 {i}").ToList());
        typeof(MainWindow).GetField("_chapters", flags)!.SetValue(reader, new List<(int Line, string Title)> { (0, "第一章"), (30, "第二章"), (60, "第三章") });
        var status = new TextBlock { Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap };
        var lastMenuKey = "无";
        var panel = new StackPanel();
        var window = new Window { Title = "TXT 菜单交互验证", Width = 430, Height = 240, Left = 120, Top = 180, Content = panel };
        void Update()
        {
            saveTimer.Stop();
            status.Text = $"菜单：{(menu.IsOpen ? "打开" : "关闭")}\n当前行：{state.CurrentLine}\n前台 HWND：{GetForegroundWindow()}\n鼠标捕获：{System.Windows.Input.Mouse.Captured?.GetType().Name ?? "无"}\n菜单实际按键：{lastMenuKey}";
        }
        var open = new Button { Content = "在本进程打开菜单", Margin = new Thickness(16), Height = 36 };
        panel.Children.Add(open); panel.Children.Add(status);
        void OpenMenu()
        {
            var tray = typeof(MainWindow).GetField("_trayIcon", flags)!.GetValue(reader)!;
            var assembly = tray.GetType().Assembly;
            var buttons = assembly.GetType("System.Windows.Forms.MouseButtons")!;
            var args = Activator.CreateInstance(assembly.GetType("System.Windows.Forms.MouseEventArgs")!, Enum.Parse(buttons, "Right"), 1, 0, 0, 0)!;
            tray.GetType().GetMethod("OnMouseUp", flags)!.Invoke(tray, new[] { args });
            Update();
        }
        open.Click += (_, _) => OpenMenu();
        menu.Opened += (_, _) => window.Dispatcher.BeginInvoke(new Action(Update));
        menu.Closed += (_, _) => Update();
        menu.NextChapterButton.Click += (_, _) => Update();
        menu.PreviousChapterButton.Click += (_, _) => Update();
        menu.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler((_, _) => Update()), true);
        menu.AddHandler(System.Windows.Input.Keyboard.PreviewKeyDownEvent,
            new System.Windows.Input.KeyEventHandler((_, e) => { lastMenuKey = e.Key.ToString(); Update(); }), true);
        reader.Show(); reader.Hide();
        var outside = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--tray-click-target {Environment.ProcessId}")
        {
            RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
        })!;
        outside.OutputDataReceived += (_, e) =>
        {
            if (e.Data == "OPEN") window.Dispatcher.BeginInvoke(new Action(OpenMenu));
        };
        outside.BeginOutputReadLine();
        window.Closed += (_, _) =>
        {
            menu.Close();
            ((DispatcherTimer)typeof(MainWindow).GetField("_saveTimer", flags)!.GetValue(reader)!).Stop();
            typeof(MainWindow).GetMethod("UnregisterHotkeys", flags)!.Invoke(reader, null);
            ((IDisposable)typeof(MainWindow).GetField("_trayIcon", flags)!.GetValue(reader)!).Dispose();
            typeof(MainWindow).GetField("_isExiting", flags)!.SetValue(reader, true);
            reader.Close();
            if (!outside.HasExited) outside.Kill();
            application.Shutdown();
        };
        Update();
        application.Run(window);
    }

    internal static void RunOutside(Application application, int ownerProcess)
    {
        var panel = new StackPanel { Margin = new Thickness(20) };
        var open = new Button { Content = "从其他进程触发托盘右键", Height = 40, Margin = new Thickness(0, 0, 0, 18) };
        var outside = new Button { Content = "外部区域：点击这里", Height = 70 };
        var status = new TextBlock { Text = "这是独立进程；测试用，不读写个人配置。", Margin = new Thickness(0, 16, 0, 0) };
        panel.Children.Add(open); panel.Children.Add(outside); panel.Children.Add(status);
        var window = new Window { Title = "TXT 外部点击测试", Width = 400, Height = 280, Left = 620, Top = 180, Content = panel };
        open.Click += (_, _) =>
        {
            AllowSetForegroundWindow(ownerProcess);
            Console.WriteLine("OPEN"); Console.Out.Flush();
        };
        outside.Click += (_, _) => status.Text = "已收到外部实际点击。";
        window.Closed += (_, _) => application.Shutdown();
        application.Run(window);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
}
