namespace FloatingTxtReader;

public sealed class AppState
{
    public const string DefaultCloseHotkey = "Ctrl+Alt+Q";

    public string? FilePath { get; set; }
    public int CurrentLine { get; set; }
    public double WindowLeft { get; set; } = 180;
    public double WindowTop { get; set; } = 180;
    public double WindowWidth { get; set; } = 420;
    public double WindowHeight { get; set; } = 56;
    public bool IsLocked { get; set; }
    public int VisibleLines { get; set; } = 1;
    public int CharactersPerLine { get; set; } = 36;
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public double FontSize { get; set; } = 22;
    public string FontWeight { get; set; } = "Normal";
    public string FontStyle { get; set; } = "Normal";
    public string ForegroundColor { get; set; } = "#FFFFFF";
    public double TextOpacity { get; set; } = 0.95;
    public double LineHeightMultiplier { get; set; } = 1.45;
    public string CloseHotkey { get; set; } = DefaultCloseHotkey;
}
