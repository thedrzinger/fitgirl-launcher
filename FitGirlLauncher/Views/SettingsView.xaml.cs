using System.Windows;
using System.Windows.Controls;
using FitGirlLauncher.Services;
using FitGirlLauncher.ViewModels;

namespace FitGirlLauncher.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LibraryPathRow row })
            return;

        var folder = UiDialogs.ChooseFolder("Choose a game library folder");
        if (folder != null)
            row.Path = folder;
    }
}
