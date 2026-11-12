using System.Windows;
using System.Windows.Input;

namespace FloatingTxtReader;

public partial class SettingsTheme : ResourceDictionary
{
    public SettingsTheme() => InitializeComponent();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        e.Handled = true;
        try { Window.GetWindow((DependencyObject)sender)?.DragMove(); }
        catch (InvalidOperationException) { /* The mouse button may be released before dragging starts. */ }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Window.GetWindow((DependencyObject)sender)?.Close();
}
