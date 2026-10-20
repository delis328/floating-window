using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace FloatingTxtReader;

public partial class HotkeySettingsWindow : Window
{
    private readonly Dictionary<int, TextBox> _boxes = new();
    private readonly Func<AppState, string?> _apply;

    public HotkeySettingsWindow(AppState state, Func<AppState, string?> apply)
    {
        _apply = apply;
        InitializeComponent();
        FastForwardSpeedBox.Text = state.FastForwardLinesPerSecond.ToString(CultureInfo.InvariantCulture);
        SpeedRangeText.Text = $"1-{AppState.MaxFastForwardLinesPerSecond}，按住保持匀速";
        foreach (var binding in Hotkeys.GetBindings(state))
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            row.Children.Add(new TextBlock
            {
                Text = binding.Label,
                Width = 145,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var box = new TextBox
            {
                Text = binding.Shortcut,
                IsReadOnly = true,
                VerticalContentAlignment = VerticalAlignment.Center,
                MinHeight = 28,
                ToolTip = "按下快捷键录入；无修饰键时按 Backspace 或 Delete 清空",
            };
            box.PreviewKeyDown += HotkeyBox_PreviewKeyDown;
            _boxes.Add(binding.Id, box);
            var clearButton = new Button { Content = "清空", Width = 48, Margin = new Thickness(8, 0, 0, 0) };
            clearButton.Click += (_, _) => { box.Text = string.Empty; ErrorText.Text = string.Empty; };
            DockPanel.SetDock(clearButton, Dock.Right);
            row.Children.Add(clearButton);
            row.Children.Add(box);
            BindingsPanel.Children.Add(row);
        }
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.Tab or Key.Escape) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Hotkeys.IsModifierKey(key)) return;
        ((TextBox)sender).Text = Keyboard.Modifiers == ModifierKeys.None && key is Key.Back or Key.Delete
            ? string.Empty
            : Hotkeys.Format(Keyboard.Modifiers, key);
        ErrorText.Text = string.Empty;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(FastForwardSpeedBox.Text, out var speed) || speed is < 1 or > AppState.MaxFastForwardLinesPerSecond)
        {
            ErrorText.Text = $"快进速度请输入 1-{AppState.MaxFastForwardLinesPerSecond} 的整数（行 / 秒）。";
            return;
        }
        var updatedState = new AppState { FastForwardLinesPerSecond = speed };
        foreach (var (id, box) in _boxes) Hotkeys.SetShortcut(updatedState, id, box.Text);
        var error = Hotkeys.Validate(updatedState) ?? _apply(updatedState);
        if (error is not null)
        {
            ErrorText.Text = error;
            return;
        }
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
