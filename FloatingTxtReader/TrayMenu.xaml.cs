using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FloatingTxtReader;

public partial class TrayMenu : ContextMenu
{
    public TrayMenu()
    {
        InitializeComponent();
        Closed += (_, _) => SettingsItem.IsSubmenuOpen = false;
        SettingsItem.SubmenuOpened += (_, _) =>
        {
            var popup = (Popup)SettingsItem.Template.FindName("PART_Popup", SettingsItem);
            popup.PlacementTarget = this;
            popup.VerticalOffset = SettingsItem.TranslatePoint(new System.Windows.Point(), this).Y - ((Border)popup.Child).Padding.Top;
        };
    }

    internal void Close()
    {
        SettingsItem.IsSubmenuOpen = false;
        IsOpen = false;
    }
}
