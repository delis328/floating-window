using System.IO;
using System.Windows;

namespace FloatingTxtReader;

public partial class BlankLinePreviewWindow : Window
{
    internal bool RemoveBlankLines { get; private set; }

    internal BlankLinePreviewWindow(string filePath, ReadingDocument document)
    {
        InitializeComponent();
        FileNameText.Text = Path.GetFileName(filePath);
        FileNameText.ToolTip = filePath;
        BeforeText.Text = document.BeforePreview;
        AfterText.Text = string.IsNullOrEmpty(document.AfterPreview) ? "（没有可显示的文字）" : document.AfterPreview;
        SummaryText.Text = $"检测到 {document.BlankLineCount} 行空白（含仅有空格的行），是否去除？";
    }

    private void Keep_Click(object sender, RoutedEventArgs e) => Close();

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        RemoveBlankLines = true;
        Close();
    }
}
