using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FloatingTxtReader;

public partial class ChapterListWindow : Window
{
    private readonly IReadOnlyList<(int Line, string Title)> _chapters;
    private readonly Action<int> _goToLine;
    private readonly List<(int Line, string Title)> _visibleChapters = [];
    private int _currentChapterLine;

    public ChapterListWindow(IReadOnlyList<(int Line, string Title)> chapters, int currentLine, Action<int> goToLine)
    {
        _chapters = chapters;
        _goToLine = goToLine;
        foreach (var chapter in chapters)
        {
            if (chapter.Line <= currentLine)
            {
                _currentChapterLine = chapter.Line;
            }
        }

        InitializeComponent();
        ApplyFilter(string.Empty);
    }

    private void ApplyFilter(string filter)
    {
        _visibleChapters.Clear();
        _visibleChapters.AddRange(string.IsNullOrWhiteSpace(filter)
            ? _chapters
            : _chapters.Where(chapter => chapter.Title.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase)));

        ChapterList.ItemsSource = _visibleChapters.Select(chapter => chapter.Title).ToList();
        if (_visibleChapters.Count == 0)
        {
            return;
        }

        var selectedIndex = _visibleChapters.FindIndex(chapter => chapter.Line == _currentChapterLine);
        ChapterList.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        ChapterList.ScrollIntoView(ChapterList.SelectedItem);
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter(FilterBox.Text);

    private void FilterBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.Enter)
        {
            e.Handled = true;
            Jump();
        }
        else if (e.Key is Key.Down or Key.Up && ChapterList.Items.Count > 0)
        {
            e.Handled = true;
            ChapterList.SelectedIndex = Math.Clamp(
                ChapterList.SelectedIndex + (e.Key == Key.Down ? 1 : -1),
                0,
                ChapterList.Items.Count - 1);
            ChapterList.ScrollIntoView(ChapterList.SelectedItem);
        }
    }

    private void ChapterList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Jump();

    private void Jump_Click(object sender, RoutedEventArgs e) => Jump();

    private void Jump()
    {
        var selectedIndex = ChapterList.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= _visibleChapters.Count)
        {
            return;
        }

        _goToLine(_visibleChapters[selectedIndex].Line);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
