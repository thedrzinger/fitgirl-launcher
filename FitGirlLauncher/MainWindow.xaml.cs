using System.Windows;
using System.Windows.Controls.Primitives;
using FitGirlLauncher.Models;
using FitGirlLauncher.ViewModels;

namespace FitGirlLauncher;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        SettingsPane.DataContext = _viewModel;
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is NavItem item)
            _viewModel.SelectNav(item);
    }
}
