using System.Globalization;
using System.Windows.Input;

namespace FloatingTxtReader;

internal readonly record struct HotkeyBinding(int Id, string Label, string Shortcut);

internal static class Hotkeys
{
    public const int Close = 1;
    public const int Hide = 2;
    public const int PreviousLine = 3;
    public const int NextLine = 4;
    public const int PreviousChapter = 5;
    public const int NextChapter = 6;
    public const int FastForward = 7;

    public static HotkeyBinding[] GetBindings(AppState state) =>
    [
        new(Close, "快速关闭", state.CloseHotkey ?? ""),
        new(Hide, "快速隐藏 / 恢复", state.HideHotkey ?? ""),
        new(PreviousLine, "上一行", state.PreviousLineHotkey ?? ""),
        new(NextLine, "下一行", state.NextLineHotkey ?? ""),
        new(PreviousChapter, "上一章", state.PreviousChapterHotkey ?? ""),
        new(NextChapter, "下一章", state.NextChapterHotkey ?? ""),
        new(FastForward, "快进行（按住）", state.FastForwardHotkey ?? ""),
    ];

    public static void SetShortcut(AppState state, int id, string value)
    {
        switch (id)
        {
            case Close: state.CloseHotkey = value; break;
            case Hide: state.HideHotkey = value; break;
            case PreviousLine: state.PreviousLineHotkey = value; break;
            case NextLine: state.NextLineHotkey = value; break;
            case PreviousChapter: state.PreviousChapterHotkey = value; break;
            case NextChapter: state.NextChapterHotkey = value; break;
            case FastForward: state.FastForwardHotkey = value; break;
        }
    }

    public static string? Validate(AppState state)
    {
        if (state.FastForwardLinesPerSecond is < 1 or > AppState.MaxFastForwardLinesPerSecond)
            return $"快进速度请输入 1-{AppState.MaxFastForwardLinesPerSecond} 的整数（行 / 秒）。";
        var shortcuts = new Dictionary<(uint Modifiers, uint Key), string>();
        foreach (var binding in GetBindings(state))
        {
            if (string.IsNullOrWhiteSpace(binding.Shortcut)) continue;
            if (!TryParse(binding.Shortcut, out var modifiers, out var key))
                return $"{binding.Label}：快捷键无效，请重新录入（F12 为系统保留键）。";
            if (!shortcuts.TryAdd((modifiers, key), binding.Label))
                return $"{binding.Label}与{shortcuts[(modifiers, key)]}的快捷键重复。";
        }
        return null;
    }

    public static bool TryParse(string? value, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        Key? key = null;
        foreach (var part in value.Split('+', StringSplitOptions.TrimEntries))
        {
            uint modifier = part.ToUpperInvariant() switch
            {
                "ALT" => 1,
                "CTRL" => 2,
                "SHIFT" => 4,
                "WIN" => 8,
                _ => 0,
            };
            if (modifier != 0)
            {
                if ((modifiers & modifier) != 0) return false;
                modifiers |= modifier;
                continue;
            }

            Key parsedKey;
            if (part is "波浪键" or "~") parsedKey = Key.Oem3;
            else if (part.Length == 1 && part[0] is >= '0' and <= '9')
                parsedKey = (Key)((int)Key.D0 + part[0] - '0');
            else if (part.Length == 0 || char.IsDigit(part[0]) ||
                !Enum.TryParse(part, true, out parsedKey) || !Enum.IsDefined(parsedKey)) return false;

            if (key is not null || IsModifierKey(parsedKey) || parsedKey is Key.None or Key.F12) return false;
            key = parsedKey;
        }
        if (key is null) return false;
        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key.Value);
        return virtualKey != 0;
    }

    public static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key switch
        {
            Key.Oem3 => "波浪键",
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(CultureInfo.InvariantCulture),
            _ => key.ToString(),
        });
        return string.Join("+", parts);
    }

    public static TimeSpan FastForwardInterval(int linesPerSecond) =>
        TimeSpan.FromSeconds(1d / Math.Clamp(linesPerSecond, 1, AppState.MaxFastForwardLinesPerSecond));
}
