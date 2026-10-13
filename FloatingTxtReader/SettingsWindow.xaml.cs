using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FloatingTxtReader;

public partial class SettingsWindow : Window
{
    private readonly AppState _state;
    private readonly Func<AppState, string?> _apply;

    public SettingsWindow(AppState state, Func<AppState, string?> apply)
    {
        _state = new AppState
        {
            VisibleLines = state.VisibleLines,
            CharactersPerLine = state.CharactersPerLine,
            FontFamily = state.FontFamily,
            FontSize = state.FontSize,
            FontWeight = state.FontWeight,
            FontStyle = state.FontStyle,
            ForegroundColor = state.ForegroundColor,
            TextOpacity = state.TextOpacity,
            LineHeightMultiplier = state.LineHeightMultiplier,
            CloseHotkey = state.CloseHotkey,
        };
        _apply = apply;
        InitializeComponent();

        VisibleLinesBox.Text = _state.VisibleLines.ToString(CultureInfo.InvariantCulture);
        CharactersPerLineBox.Text = _state.CharactersPerLine.ToString(CultureInfo.InvariantCulture);
        FontFamilyBox.Text = _state.FontFamily;
        FontSizeBox.Text = _state.FontSize.ToString(CultureInfo.InvariantCulture);
        FontWeightBox.SelectedIndex = _state.FontWeight == "Bold" ? 1 : 0;
        FontStyleBox.SelectedIndex = _state.FontStyle == "Italic" ? 1 : 0;
        ColorBox.SelectedIndex = string.Equals(_state.ForegroundColor, "#000000", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        TextOpacityBox.Text = _state.TextOpacity.ToString(CultureInfo.InvariantCulture);
        LineHeightBox.Text = _state.LineHeightMultiplier.ToString(CultureInfo.InvariantCulture);
        HotkeyBox.Text = _state.CloseHotkey;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(VisibleLinesBox.Text, out var visibleLines) || visibleLines is < 1 or > 30 ||
            !int.TryParse(CharactersPerLineBox.Text, out var charactersPerLine) || charactersPerLine is < 1 or > 500 ||
            !double.TryParse(FontSizeBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var fontSize) || fontSize is < 8 or > 96 ||
            !double.TryParse(TextOpacityBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var textOpacity) || textOpacity is < 0.1 or > 1 ||
            !double.TryParse(LineHeightBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var lineHeight) || lineHeight is < 1 or > 3 ||
            string.IsNullOrWhiteSpace(FontFamilyBox.Text))
        {
            ErrorText.Text = "\u8BF7\u8F93\u5165\u6709\u6548\u503C\u3002";
            return;
        }

        _state.VisibleLines = visibleLines;
        _state.CharactersPerLine = charactersPerLine;
        _state.FontFamily = FontFamilyBox.Text.Trim();
        _state.FontSize = fontSize;
        _state.FontWeight = FontWeightBox.SelectedIndex == 1 ? "Bold" : "Normal";
        _state.FontStyle = FontStyleBox.SelectedIndex == 1 ? "Italic" : "Normal";
        _state.ForegroundColor = ColorBox.SelectedIndex == 1 ? "#000000" : "#FFFFFF";
        _state.TextOpacity = textOpacity;
        _state.LineHeightMultiplier = lineHeight;
        _state.CloseHotkey = HotkeyBox.Text;
        var error = _apply(_state);
        if (error is not null)
        {
            ErrorText.Text = error;
            return;
        }

        Close();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.Tab or Key.Escape)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(key))
        {
            e.Handled = true;
            return;
        }

        HotkeyBox.Text = FormatHotkey(Keyboard.Modifiers, key);
        ErrorText.Text = string.Empty;
        e.Handled = true;
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift;

    private static string FormatHotkey(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(key switch
        {
            Key.Oem3 => "\u6CE2\u6D6A\u952E",
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(CultureInfo.InvariantCulture),
            _ => key.ToString(),
        });
        return string.Join("+", parts);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
