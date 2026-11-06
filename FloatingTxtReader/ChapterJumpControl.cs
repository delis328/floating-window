using Forms = System.Windows.Forms;

namespace FloatingTxtReader;

public sealed class ChapterJumpControl : Forms.UserControl
{
    private readonly Forms.Button _previousChapterButton = new();
    private readonly Forms.Button _nextChapterButton = new();
    private readonly Forms.Button _nextFiveChaptersButton = new();

    public event Action<int>? JumpRequested;

    public ChapterJumpControl()
    {
        Height = 34;
        Width = 256;
        BackColor = TrayMenuStyle.Surface;
        Margin = new Forms.Padding(0);
        Padding = new Forms.Padding(0);

        _previousChapterButton.Text = "\u4E0A\u4E00\u7AE0";
        _nextChapterButton.Text = "\u4E0B\u4E00\u7AE0";
        _nextFiveChaptersButton.Text = "\u4E0B\u4E94\u7AE0";
        _previousChapterButton.Margin = new Forms.Padding(0);
        _nextChapterButton.Margin = new Forms.Padding(0);
        _nextFiveChaptersButton.Margin = new Forms.Padding(0);
        TrayMenuStyle.Apply(_previousChapterButton, false);
        TrayMenuStyle.Apply(_nextChapterButton, true);
        TrayMenuStyle.Apply(_nextFiveChaptersButton, false);
        _previousChapterButton.Click += (_, _) => JumpRequested?.Invoke(-1);
        _nextChapterButton.Click += (_, _) => JumpRequested?.Invoke(1);
        _nextFiveChaptersButton.Click += (_, _) => JumpRequested?.Invoke(5);
        Controls.Add(_nextFiveChaptersButton);
        Controls.Add(_nextChapterButton);
        Controls.Add(_previousChapterButton);
        ResizeButtons();
    }

    public void SetAvailable(bool canGoPrevious, bool canGoNext)
    {
        _previousChapterButton.Enabled = canGoPrevious;
        _nextChapterButton.Enabled = canGoNext;
        _nextFiveChaptersButton.Enabled = canGoNext;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ResizeButtons();
    }

    private void ResizeButtons()
    {
        const int gap = 4;
        var quarter = Math.Max(1, (ClientSize.Width - gap * 2) / 4);
        _previousChapterButton.SetBounds(0, 0, quarter, ClientSize.Height);
        _nextChapterButton.SetBounds(quarter + gap, 0, quarter * 2, ClientSize.Height);
        var lastButtonLeft = quarter * 3 + gap * 2;
        _nextFiveChaptersButton.SetBounds(lastButtonLeft, 0, ClientSize.Width - lastButtonLeft, ClientSize.Height);
    }
}
