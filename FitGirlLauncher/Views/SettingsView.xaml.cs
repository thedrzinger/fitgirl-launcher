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
        var folder = UiDialogs.ChooseFolder("Choose your game library folder");
        if (folder != null && DataContext is MainViewModel vm)
            vm.Settings.LibraryPath = folder;
    }
}
